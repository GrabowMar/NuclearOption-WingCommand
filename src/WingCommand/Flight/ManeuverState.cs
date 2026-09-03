using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Flies one scripted manoeuvre and then rejoins. Transient: it is never a resting
    /// state, and every path out of it ends in <c>member.Complete(WingOrder.Formation)</c>.
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
        private Vector3 notchDirection;
        private static readonly List<Unit> scratchUnits = new List<Unit>(32);

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

            if (kind == ManeuverKind.NotchThreat)
                notchDirection = ResolveNotchDirection(aircraft, entryForward);

            // Reasons the manoeuvre cannot be flown. Recorded, not acted on here: switching
            // pilot state from inside EnterState is re-entrant, so the first FixedUpdate
            // tick does the rejoin instead - the same pattern AttackRunState uses.
            if (ManeuverCatalog.BreakDirection(kind) == 0 &&
                kind != ManeuverKind.WingWaggle &&
                kind != ManeuverKind.NotchThreat &&
                !WingBrain.Manoeuvres)
            {
                Abort("aerobatics are off in Performance mode");
                return;
            }
            if (!fixedWing && !ManeuverCatalog.RotaryCapable(kind))
            {
                Abort("this airframe cannot fly that manoeuvre");
                return;
            }

            float floor = Mathf.Max(WingTuning.ManeuverEntryFloor,
                                    ManeuverCatalog.MinEntryAltitudeAgl(kind));
            if (aircraft.radarAlt < floor)
            {
                Abort("not enough height");
                return;
            }
            float minSpeedFraction = Mathf.Max(
                WingTuning.ManeuverMinSpeedFraction,
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
            if (aircraft.radarAlt < WingTuning.ManeuverHardFloor)
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
                case ManeuverKind.NotchThreat: step = FlyNotch();          break;
                case ManeuverKind.WingWaggle:  step = FlyWaggle();         break;
                case ManeuverKind.Loop:        step = FlyLoop();           break;
                case ManeuverKind.Immelmann:   step = FlyImmelmann();      break;
                case ManeuverKind.SplitS:      step = FlySplitS();         break;
                case ManeuverKind.BarrelRoll:  step = FlyBarrelRoll();     break;
                case ManeuverKind.AileronRoll: step = FlyAileronRoll();    break;
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

            float turned = Vector3.Angle(entryForward, Flatten(Heading()));

            if (fixedWing)
            {
                // Ensure the break turn destination accounts for terrain clearance and doesn't
                // drag the nose down through the horizon during an 88-degree bank turn.
                float safeY = aircraft.radarAlt < entryRadarAlt
                    ? aircraft.GlobalPosition().y + (entryRadarAlt - aircraft.radarAlt) * 0.6f
                    : aircraft.GlobalPosition().y;

                GlobalPosition dest = new GlobalPosition(
                    aircraft.GlobalPosition().x + breakDir.x * 8000f,
                    safeY,
                    aircraft.GlobalPosition().z + breakDir.z * 8000f);

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
                GlobalPosition dest = aircraft.GlobalPosition() + breakDir * 3000f;
                aircraft.autopilot.AutoAim(
                    destination: dest,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt, 25f, 2000f),
                    aimDirection: breakDir,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }

            float limit = fixedWing ? 5f : 7f;
            if (turned >= 115f || Time.timeSinceLevelLoad - startedAt > limit)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
        }

        private Step FlyNotch()
        {
            if (fixedWing)
            {
                float safeY = aircraft.radarAlt < entryRadarAlt
                    ? aircraft.GlobalPosition().y + (entryRadarAlt - aircraft.radarAlt) * 0.6f
                    : aircraft.GlobalPosition().y;

                GlobalPosition dest = new GlobalPosition(
                    aircraft.GlobalPosition().x + notchDirection.x * 8000f,
                    safeY,
                    aircraft.GlobalPosition().z + notchDirection.z * 8000f);

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
                GlobalPosition dest = aircraft.GlobalPosition() + notchDirection * 3000f;
                aircraft.autopilot.AutoAim(
                    destination: dest,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt, 25f, 2000f),
                    aimDirection: notchDirection,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }

            float limit = fixedWing ? 6f : 8f;
            float currentHeadingDelta = Vector3.Angle(Flatten(Heading()), notchDirection);
            if ((currentHeadingDelta <= 15f && Time.timeSinceLevelLoad - startedAt >= 2.5f) ||
                Time.timeSinceLevelLoad - startedAt > limit)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
        }

        private static Vector3 ResolveNotchDirection(Aircraft aircraft, Vector3 forward)
        {
            Vector3 threatPos = Vector3.zero;
            bool foundThreat = false;

            MissileWarning mws = aircraft.GetMissileWarningSystem();
            if (mws != null && mws.IsWarning())
            {
                if (mws.TryGetNearestIncoming(out Missile incoming) && incoming != null && !incoming.disabled)
                {
                    threatPos = incoming.transform.position;
                    foundThreat = true;
                }
                else if (mws.knownMissiles != null && mws.knownMissiles.Count > 0)
                {
                    for (int i = 0; i < mws.knownMissiles.Count; i++)
                    {
                        Missile m = mws.knownMissiles[i];
                        if (m != null && !m.disabled)
                        {
                            threatPos = m.transform.position;
                            foundThreat = true;
                            break;
                        }
                    }
                }
            }

            if (!foundThreat)
            {
                Unit bestEmitter = null;
                float bestDistSq = float.MaxValue;
                Vector3 acPos = aircraft.transform.position;

                scratchUnits.Clear();
                BattlefieldGrid.GetUnitsInRangeNonAlloc(aircraft.GlobalPosition(), 25000f, scratchUnits);
                for (int i = 0; i < scratchUnits.Count; i++)
                {
                    Unit u = scratchUnits[i];
                    if (u == null || u.disabled || u == aircraft) continue;
                    if (u.NetworkHQ == null || u.NetworkHQ == aircraft.NetworkHQ) continue;

                    if (u.definition != null && (u.definition.typeIdentity.radar > 0.3f || u.definition.typeIdentity.air > 0.5f))
                    {
                        float dSq = (u.transform.position - acPos).sqrMagnitude;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestEmitter = u;
                        }
                    }
                }
                scratchUnits.Clear();

                if (bestEmitter != null)
                {
                    threatPos = bestEmitter.transform.position;
                    foundThreat = true;
                }
            }

            if (!foundThreat)
            {
                return Quaternion.AngleAxis(-90f, Vector3.up) * forward;
            }

            Vector3 toThreat = Flatten(threatPos - aircraft.transform.position);
            if (toThreat.sqrMagnitude < 1f)
            {
                return Quaternion.AngleAxis(-90f, Vector3.up) * forward;
            }
            toThreat.Normalize();

            Vector3 optA = Quaternion.AngleAxis(90f, Vector3.up) * toThreat;
            Vector3 optB = Quaternion.AngleAxis(-90f, Vector3.up) * toThreat;

            return Vector3.Dot(optA, forward) >= Vector3.Dot(optB, forward) ? optA : optB;
        }

        private Step FlyWaggle()
        {
            float t = Time.timeSinceLevelLoad - startedAt;
            GlobalPosition ahead = aircraft.GlobalPosition() + entryForward * 3000f;

            if (fixedWing)
            {
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

                // Smoothly envelope waggle cycles to damp roll rate and settle dead-level.
                float envelope = Mathf.Clamp01(1f - (t - 2.0f) / 0.8f);
                float wave = Mathf.Sin(t * Mathf.PI * 2f * 1.0f) * 0.65f * envelope;
                float bank = FixedWingFormation.BankOf(aircraft);
                float damping = BodyRollRate() * 0.35f;

                controlInputs.roll = Mathf.Clamp(wave - (bank / 30f) * (1f - envelope) - damping, -1f, 1f);
                aircraft.FilterInputs();

                if (t >= 2.8f && Mathf.Abs(bank) < 8f && Mathf.Abs(BodyRollRate()) < 0.25f)
                    return Step.Done;
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: ahead,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, aircraft.radarAlt, 25f, 2000f),
                    aimDirection: entryForward,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);

                float envelope = Mathf.Clamp01(1f - (t - 2.0f) / 0.8f);
                controlInputs.yaw = Mathf.Sin(t * Mathf.PI * 2f * 0.8f) * 0.5f * envelope;
                aircraft.FilterInputs();

                if (t >= 2.8f) return Step.Done;
            }

            return t >= 3.5f ? Step.Done : Step.Running;
        }

        private Step FlyLoop()
        {
            pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;

            if (aircraft.speed < parameters.maxSpeed * StallFraction &&
                pitchIntegral < Mathf.PI * 1.5f)
            {
                RecoverWingsLevel();
                return Step.Failed;
            }

            float bank = FixedWingFormation.BankOf(aircraft);
            float rollCorrection = Mathf.Clamp(-bank * 0.04f - BodyRollRate() * 0.45f, -0.6f, 0.6f);

            if (pitchIntegral < Mathf.PI)
            {
                // Climb into vertical: full afterburner/power and positive G pull.
                controlInputs.throttle = 1f;
                controlInputs.pitch = 1f;
                controlInputs.roll = rollCorrection;
                controlInputs.yaw = 0f;
            }
            else if (pitchIntegral < Mathf.PI * 1.75f)
            {
                // Downhill side: reduce throttle to prevent overspeeding and high-G compression.
                controlInputs.throttle = 0.3f;
                controlInputs.pitch = 1f;
                controlInputs.roll = rollCorrection;
                controlInputs.yaw = 0f;
            }
            else
            {
                // Level-off: restore throttle and taper pitch to ease smoothly into level flight.
                float remaining = Mathf.Max(Mathf.PI * 2f - pitchIntegral, 0f);
                float pitchRamp = Mathf.Clamp(remaining / (Mathf.PI * 0.25f), 0.1f, 1f);

                controlInputs.throttle = 0.85f;
                controlInputs.pitch = pitchRamp;
                controlInputs.roll = Mathf.Clamp(-bank * 0.05f - BodyRollRate() * 0.5f, -1f, 1f);
                controlInputs.yaw = 0f;

                if (pitchIntegral >= Mathf.PI * 2f - 0.2f &&
                    Mathf.Abs(aircraft.transform.forward.y) < 0.2f &&
                    Mathf.Abs(bank) < 10f)
                {
                    RecoverWingsLevel();
                    return Step.Done;
                }
            }

            aircraft.FilterInputs();
            return pitchIntegral >= Mathf.PI * 2f + 0.3f ? Step.Done : Step.Running;
        }

        private Step FlyImmelmann()
        {
            if (phase == 0)
            {
                // Pitch up through half-loop to inverted at apex.
                controlInputs.throttle = 1f;
                controlInputs.pitch = 1f;
                float bank = FixedWingFormation.BankOf(aircraft);
                controlInputs.roll = Mathf.Clamp(-bank * 0.035f - BodyRollRate() * 0.4f, -0.5f, 0.5f);
                aircraft.FilterInputs();

                pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;

                if (aircraft.speed < parameters.maxSpeed * StallFraction)
                {
                    RecoverWingsLevel();
                    return Step.Failed;
                }

                if (pitchIntegral >= Mathf.PI - 0.3f)
                {
                    phase = 1;
                    rollIntegral = 0f;
                }
                return Step.Running;
            }

            // Phase 1: Half a loop complete, inverted at altitude - roll upright smoothly.
            float currentBank = FixedWingFormation.BankOf(aircraft);
            float errorToLevel = Mathf.DeltaAngle(currentBank, 0f);

            // Maintain enough nose-up elevator so the nose stays on the horizon during the roll.
            controlInputs.throttle = 0.95f;
            controlInputs.pitch = 0.25f;
            controlInputs.roll = Mathf.Clamp(errorToLevel * 0.035f - BodyRollRate() * 0.45f, -1f, 1f);
            aircraft.FilterInputs();

            if (Mathf.Abs(errorToLevel) < 8f && Mathf.Abs(BodyRollRate()) < 0.25f)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
        }

        private Step FlySplitS()
        {
            if (phase == 0)
            {
                // Phase 0: Roll inverted with roll-rate damping at idle power.
                controlInputs.throttle = 0.2f;
                controlInputs.pitch = 0f;

                float bank = FixedWingFormation.BankOf(aircraft);
                float errorToInverted = Mathf.DeltaAngle(bank, 180f);
                controlInputs.roll = Mathf.Clamp(errorToInverted * 0.035f - BodyRollRate() * 0.4f, -1f, 1f);
                aircraft.FilterInputs();

                if (Mathf.Abs(errorToInverted) < 15f && Mathf.Abs(BodyRollRate()) < 0.5f)
                {
                    phase = 1;
                    pitchIntegral = 0f;
                }
                return Step.Running;
            }

            if (phase == 1)
            {
                // Phase 1: Inverted half-loop downward.
                controlInputs.throttle = 0.3f;
                controlInputs.pitch = 1f;
                controlInputs.roll = Mathf.Clamp(-BodyRollRate() * 0.45f, -0.4f, 0.4f);
                aircraft.FilterInputs();

                pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime;
                if (pitchIntegral >= Mathf.PI - 0.35f)
                {
                    phase = 2;
                }
                return Step.Running;
            }

            // Phase 2: Pull out level, power up, and arrest descent.
            float noseY = aircraft.transform.forward.y;
            float rollErr = Mathf.DeltaAngle(FixedWingFormation.BankOf(aircraft), 0f);

            controlInputs.throttle = 1f;
            controlInputs.pitch = Mathf.Clamp(0.5f - noseY * 1.5f, 0.1f, 1f);
            controlInputs.roll = Mathf.Clamp(rollErr * 0.04f - BodyRollRate() * 0.45f, -1f, 1f);
            aircraft.FilterInputs();

            if (noseY >= -0.05f && Mathf.Abs(rollErr) < 8f && Mathf.Abs(BodyRollRate()) < 0.25f)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
        }

        private Step FlyAileronRoll()
        {
            controlInputs.throttle = Mathf.Clamp01(parameters.cruiseThrottle + 0.15f);

            rollIntegral += Mathf.Abs(BodyRollRate()) * Time.fixedDeltaTime;

            if (rollIntegral < Mathf.PI * 2f - 0.6f)
            {
                // Axial roll with waterline pitch bias.
                controlInputs.pitch = 0.12f;
                controlInputs.roll = 1f;
            }
            else
            {
                // Damped deceleration into wings level.
                float rollError = Mathf.DeltaAngle(FixedWingFormation.BankOf(aircraft), 0f);
                controlInputs.pitch = 0.05f;
                controlInputs.roll = Mathf.Clamp(rollError * 0.04f - BodyRollRate() * 0.45f, -1f, 1f);

                if (Mathf.Abs(rollError) < 8f && Mathf.Abs(BodyRollRate()) < 0.25f)
                {
                    RecoverWingsLevel();
                    return Step.Done;
                }
            }

            aircraft.FilterInputs();
            return rollIntegral >= Mathf.PI * 2f + 0.5f ? Step.Done : Step.Running;
        }

        private Step FlyBarrelRoll()
        {
            controlInputs.throttle = 1f;

            if (phase == 0)
            {
                // Phase 0: Pitch up into initial climb.
                controlInputs.pitch = 0.85f;
                controlInputs.roll = 0.2f;
                aircraft.FilterInputs();

                if (aircraft.transform.forward.y > 0.22f || Time.timeSinceLevelLoad - startedAt > 0.5f)
                {
                    phase = 1;
                    rollIntegral = 0f;
                }
                return Step.Running;
            }

            if (phase == 1)
            {
                // Phase 1: Coordinated corkscrew (constant positive G pitch + steady roll).
                rollIntegral += Mathf.Abs(BodyRollRate()) * Time.fixedDeltaTime;
                controlInputs.pitch = 0.55f;
                controlInputs.roll = 0.8f;
                aircraft.FilterInputs();

                if (rollIntegral >= Mathf.PI * 2f - 0.5f)
                {
                    phase = 2;
                }
                return Step.Running;
            }

            // Phase 2: Smooth level-off and roll damping.
            float rollError = Mathf.DeltaAngle(FixedWingFormation.BankOf(aircraft), 0f);
            controlInputs.pitch = Mathf.Clamp(0.15f - aircraft.transform.forward.y * 0.5f, 0.05f, 0.4f);
            controlInputs.roll = Mathf.Clamp(rollError * 0.04f - BodyRollRate() * 0.45f, -1f, 1f);
            aircraft.FilterInputs();

            if (Mathf.Abs(rollError) < 8f && Mathf.Abs(BodyRollRate()) < 0.25f)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
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

            member.Complete(WingOrder.Formation);
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
