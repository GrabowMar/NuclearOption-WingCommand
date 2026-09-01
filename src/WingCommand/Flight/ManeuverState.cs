using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Flies one scripted manoeuvre and then rejoins. Transient: it is never a resting
    /// state, and every path out of it ends in <c>member.Apply(WingOrder.Formation)</c>.
    ///
    /// Two implementation styles live here. The level breaks and the wing waggle steer
    /// through <c>AutoAim</c>, the same primitive the formation controller uses. The
    /// aerobatic manoeuvres drive <see cref="ControlInputs"/> pitch/roll rate commands
    /// through a per-kind phase machine, tracking progress by integrating the body rates
    /// and confirming attitude with <see cref="FixedWingFormation.BankOf"/>. A hard radar
    /// altitude floor and an overall timeout abort any manoeuvre wings-level rather than
    /// letting a wingman fly a stunt into the ground.
    /// </summary>
    internal sealed class ManeuverState : WingPilotState
    {
        /// <summary>No manoeuvre may run longer than this before it is abandoned level.</summary>
        private const float MaxManeuverSeconds = 18f;

        /// <summary>Airspeed fraction below which a vertical manoeuvre bails out.</summary>
        private const float StallFraction = 0.12f;

        private ManeuverKind kind;

        private float startedAt;
        private int phase;
        private float pitchIntegral;
        private float rollIntegral;
        private Vector3 entryForward = Vector3.forward;
        private float entryRadarAlt;
        private bool fixedWing;
        private AircraftParameters parameters;
        private bool aborted;
        private string abortReason;

        private enum Step { Running, Done, Failed }

        public ManeuverState(WingMember member) : base(member)
        {
            stateDisplayName = "manoeuvring";
        }

        /// <summary>Choose which manoeuvre to fly. Call before switching to this state.</summary>
        public void SetManeuver(ManeuverKind value)
        {
            kind = value;
            stateDisplayName = ManeuverCatalog.Label(value).ToLowerInvariant();
        }

        public override void EnterState(Pilot pilot)
        {
            BeginFlight(pilot);

            fixedWing = !WingRegistry.IsRotary(aircraft);
            parameters = aircraft.GetAircraftParameters();
            startedAt = Time.timeSinceLevelLoad;
            phase = 0;
            pitchIntegral = 0f;
            rollIntegral = 0f;
            entryRadarAlt = aircraft.radarAlt;
            aborted = false;
            abortReason = null;

            Vector3 fwd = aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f
                ? aircraft.rb.velocity
                : aircraft.transform.forward;
            entryForward = Flatten(fwd);

            // Reasons the manoeuvre cannot be flown. Recorded, not acted on here: switching
            // pilot state from inside EnterState is re-entrant, so the first FixedUpdate
            // tick does the rejoin instead - the same pattern AttackRunState uses.
            if (ManeuverCatalog.BreakDirection(kind) == 0 &&
                kind != ManeuverKind.WingWaggle &&
                !Plugin.Settings.AerobaticsEnabled.Value)
            {
                Abort("aerobatics are disabled in config");
                return;
            }
            if (!fixedWing && !ManeuverCatalog.RotaryCapable(kind))
            {
                Abort("this airframe cannot fly that manoeuvre");
                return;
            }

            float floor = Mathf.Max(Plugin.Settings.ManeuverAltitudeFloor.Value,
                                    ManeuverCatalog.MinEntryAltitudeAgl(kind));
            if (aircraft.radarAlt < floor)
            {
                Abort("not enough height");
                return;
            }
            float minSpeedFraction = Mathf.Max(
                Plugin.Settings.ManeuverMinSpeedFraction.Value,
                ManeuverCatalog.MinEntrySpeedFraction(kind));
            if (fixedWing && aircraft.speed < parameters.maxSpeed * minSpeedFraction)
            {
                Abort("too slow to start it cleanly");
                return;
            }

            WingComms.Say(member, WingComms.Call.Maneuvering, ManeuverCatalog.Label(kind));
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
                    $"[Maneuver] {aircraft.unitName} -> {kind} " +
                    $"(alt {aircraft.radarAlt:F0} m, speed {aircraft.speed:F0} m/s)");
        }

        public override void LeaveState()
        {
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            if (aborted)
            {
                Finish(unable: true, abortReason);
                return;
            }

            // Hard floor and timeout apply in every phase of every manoeuvre.
            if (aircraft.radarAlt < Plugin.Settings.ManeuverHardFloor.Value)
            {
                RecoverWingsLevel();
                Finish(unable: true, "reached the hard deck");
                return;
            }
            if (Time.timeSinceLevelLoad - startedAt > MaxManeuverSeconds)
            {
                RecoverWingsLevel();
                Finish(unable: false, "timed out");
                return;
            }

            Step step;
            switch (kind)
            {
                case ManeuverKind.BreakLeft:
                case ManeuverKind.BreakRight:  step = FlyBreak();          break;
                case ManeuverKind.WingWaggle:  step = FlyWaggle();         break;
                case ManeuverKind.Loop:        step = FlyLoop();           break;
                case ManeuverKind.Immelmann:   step = FlyImmelmann();      break;
                case ManeuverKind.SplitS:      step = FlySplitS();         break;
                case ManeuverKind.BarrelRoll:  step = FlyRoll(barrel: true);  break;
                case ManeuverKind.AileronRoll: step = FlyRoll(barrel: false); break;
                default:                        step = Step.Done;          break;
            }

            if (step == Step.Done) Finish(unable: false, "complete");
            else if (step == Step.Failed) Finish(unable: true, "recovered early");
        }

        // ------------------------------------------------------------------ manoeuvres

        private Step FlyBreak()
        {
            int dir = ManeuverCatalog.BreakDirection(kind);   // -1 left, +1 right
            Vector3 breakDir = Quaternion.AngleAxis(dir * 135f, Vector3.up) * entryForward;
            GlobalPosition dest = aircraft.GlobalPosition() +
                                  breakDir * (fixedWing ? 8000f : 3000f);

            float turned = Vector3.Angle(entryForward, Flatten(Heading()));

            if (fixedWing)
            {
                controlInputs.throttle = 1f;
                aircraft.autopilot.AutoAim(
                    destination: dest,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 2f,
                    bankAllowed: FixedWingFormation.MaxSafeBank,
                    followTerrain: false,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft, entryRadarAlt),
                    targetVelocity: Vector3.zero);
                aircraft.FilterInputs();
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: dest,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt, 25f, 2000f),
                    aimDirection: breakDir,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }

            float limit = fixedWing ? 5f : 7f;
            return (turned >= 115f || Time.timeSinceLevelLoad - startedAt > limit)
                ? Step.Done : Step.Running;
        }

        private Step FlyWaggle()
        {
            float t = Time.timeSinceLevelLoad - startedAt;
            GlobalPosition ahead = aircraft.GlobalPosition() + entryForward * 3000f;

            if (fixedWing)
            {
                // Small bankAllowed keeps the autopilot honest while the roll override
                // below does the visible work; a large value would let the roll-noise
                // term fight it. The waggle is a rate command on top of the hold.
                aircraft.autopilot.AutoAim(
                    destination: ahead,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 2f,
                    bankAllowed: 10f,
                    followTerrain: false,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft, entryRadarAlt),
                    targetVelocity: Vector3.zero);
                controlInputs.roll = Mathf.Sin(t * Mathf.PI * 2f * 0.8f) * 0.6f;
                aircraft.FilterInputs();
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: ahead,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt, 25f, 2000f),
                    aimDirection: entryForward,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
                controlInputs.yaw = Mathf.Sin(t * Mathf.PI * 2f * 0.7f) * 0.5f;
                aircraft.FilterInputs();
            }

            return t >= 2.6f ? Step.Done : Step.Running;
        }

        private Step FlyLoop()
        {
            controlInputs.throttle = 1f;
            controlInputs.pitch = 1f;
            controlInputs.roll = Mathf.Clamp(-BodyRollRate() * 0.3f, -0.4f, 0.4f);
            controlInputs.yaw = 0f;
            aircraft.FilterInputs();

            pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;

            if (aircraft.speed < parameters.maxSpeed * StallFraction &&
                pitchIntegral < Mathf.PI * 1.5f)
            {
                RecoverWingsLevel();
                return Step.Failed;
            }

            return pitchIntegral >= Mathf.PI * 2f - 0.35f ? Step.Done : Step.Running;
        }

        private Step FlyImmelmann()
        {
            controlInputs.throttle = 1f;

            if (phase == 0)
            {
                controlInputs.pitch = 1f;
                controlInputs.roll = Mathf.Clamp(-BodyRollRate() * 0.3f, -0.4f, 0.4f);
                aircraft.FilterInputs();

                pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;

                if (aircraft.speed < parameters.maxSpeed * StallFraction)
                {
                    RecoverWingsLevel();
                    return Step.Failed;
                }
                if (pitchIntegral >= Mathf.PI - 0.35f) phase = 1;
                return Step.Running;
            }

            // Half a loop done, inverted and heading-reversed: roll upright.
            float bank = FixedWingFormation.BankOf(aircraft);
            controlInputs.pitch = 0.1f;
            controlInputs.roll = Mathf.Clamp(-bank / 45f, -1f, 1f);
            aircraft.FilterInputs();
            return Mathf.Abs(bank) < 12f ? Step.Done : Step.Running;
        }

        private Step FlySplitS()
        {
            if (phase == 0)
            {
                // Roll inverted at low power so the pull that follows brings the nose down.
                controlInputs.throttle = 0.2f;
                controlInputs.pitch = 0f;
                controlInputs.roll = 1f;
                aircraft.FilterInputs();

                if (Mathf.Abs(FixedWingFormation.BankOf(aircraft)) > 160f)
                {
                    phase = 1;
                    pitchIntegral = 0f;
                }
                return Step.Running;
            }

            if (phase == 1)
            {
                controlInputs.throttle = 0.35f;
                controlInputs.pitch = 1f;
                controlInputs.roll = Mathf.Clamp(-BodyRollRate() * 0.3f, -0.5f, 0.5f);
                aircraft.FilterInputs();

                pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;
                if (pitchIntegral >= Mathf.PI - 0.4f) phase = 2;
                return Step.Running;
            }

            // Nose back through the horizon the other way: level the wings and power up.
            float bank = FixedWingFormation.BankOf(aircraft);
            controlInputs.throttle = 1f;
            controlInputs.pitch = 0.05f;
            controlInputs.roll = Mathf.Clamp(-bank / 45f, -1f, 1f);
            aircraft.FilterInputs();
            return Mathf.Abs(bank) < 12f ? Step.Done : Step.Running;
        }

        private Step FlyRoll(bool barrel)
        {
            controlInputs.throttle = barrel
                ? 1f
                : Mathf.Clamp01(parameters.cruiseThrottle + 0.1f);
            controlInputs.pitch = barrel ? 0.35f : 0.12f;
            controlInputs.roll = 1f;
            aircraft.FilterInputs();

            rollIntegral += Mathf.Abs(BodyRollRate()) * Time.fixedDeltaTime;
            if (rollIntegral < Mathf.PI * 2f - 0.3f) return Step.Running;

            float bank = FixedWingFormation.BankOf(aircraft);
            controlInputs.roll = Mathf.Clamp(-bank / 45f, -1f, 1f);
            controlInputs.pitch = 0.05f;
            aircraft.FilterInputs();
            return Mathf.Abs(bank) < 12f ? Step.Done : Step.Running;
        }

        // ------------------------------------------------------------------ helpers

        private void RecoverWingsLevel()
        {
            if (fixedWing)
            {
                float bank = FixedWingFormation.BankOf(aircraft);
                controlInputs.roll = Mathf.Clamp(-bank / 45f, -1f, 1f);
                controlInputs.pitch = 0.15f;
                controlInputs.throttle = 1f;
                aircraft.FilterInputs();
            }
        }

        private void Finish(bool unable, string reason)
        {
            // Both endings rejoin the wing, so both use the same call - the distinction
            // (a clean finish versus an early recovery) is only useful in the log.
            WingComms.Say(member, WingComms.Call.ManeuverDone);

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
                    $"[Maneuver] {(aircraft != null ? aircraft.unitName : "?")} {kind} " +
                    (unable ? "unable" : "done") + " (" + reason + ")");

            member.Apply(WingOrder.Formation);
        }

        private void Abort(string reason)
        {
            aborted = true;
            abortReason = reason;
        }

        /// <summary>Body-frame pitch rate in rad/s, positive nose-up (a pull).</summary>
        private float BodyPitchRate() =>
            aircraft.rb != null
                ? -Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.right)
                : 0f;

        /// <summary>Body-frame roll rate in rad/s about the nose.</summary>
        private float BodyRollRate() =>
            aircraft.rb != null
                ? Vector3.Dot(aircraft.rb.angularVelocity, aircraft.transform.forward)
                : 0f;

        private Vector3 Heading() =>
            aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f
                ? aircraft.rb.velocity
                : aircraft.transform.forward;

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }
    }
}
