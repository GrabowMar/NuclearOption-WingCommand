using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Native tactical turns and JSON aerobatics, bounded by entry gates, hard deck, and timeout.</summary>
    internal sealed class ManeuverState : WingPilotState
    {
        /// <summary>No manoeuvre may run longer than this before it is abandoned level.</summary>
        private const float MaxManeuverSeconds = 18f;

        /// <summary>Airspeed fraction below which a vertical manoeuvre bails out.</summary>
        private const float StallFraction = 0.12f;

        private ManeuverKind kind;

        private float startedAt;
        private int phase;
        private float phaseStartedAt;
        private ManeuverRecipe recipe;
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
            phaseStartedAt = startedAt;
            recipe = ManeuverCatalog.RotaryCapable(kind) ? null : ManeuverScriptLoader.Get(kind);
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
                kind != ManeuverKind.MaskTerrain &&
                !WingFidelity.Manoeuvres)
            {
                Abort("aerobatics are off in Performance mode");
                return;
            }
            if (!fixedWing && !ManeuverCatalog.RotaryCapable(kind))
            {
                Abort("this airframe cannot fly that manoeuvre");
                return;
            }

            float floor = kind == ManeuverKind.MaskTerrain
                ? ManeuverCatalog.MinEntryAltitudeAgl(kind)
                : Mathf.Max(WingTuning.ManeuverEntryFloor,
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
            float hardFloor = kind == ManeuverKind.MaskTerrain ? 20f : WingTuning.ManeuverHardFloor;
            if (aircraft.radarAlt < hardFloor)
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
                case ManeuverKind.MaskTerrain: step = FlyMaskTerrain();    break;
                default: step = FlyScript(); break;
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

        private Step FlyMaskTerrain()
        {
            float elapsed = Time.timeSinceLevelLoad - startedAt;
            GlobalPosition ahead = aircraft.GlobalPosition() + entryForward * 4000f;

            if (fixedWing)
            {
                controlInputs.throttle = 0.95f;
                aircraft.autopilot.AutoAim(
                    destination: ahead,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 1.5f,
                    bankAllowed: Mathf.Min(35f, FixedWingFormation.MaxSafeBank),
                    followTerrain: true,
                    altitudeHold: 35f,
                    targetVelocity: Vector3.zero);
                aircraft.FilterInputs();
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: ahead,
                    altitudeHold: AutopilotMath.RotaryAgl(aircraft, 25f, 20f, 100f),
                    aimDirection: entryForward,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }

            if (elapsed >= 12f)
            {
                RecoverWingsLevel();
                return Step.Done;
            }

            return Step.Running;
        }

        private Step FlyScript()
        {
            if (recipe == null || aircraft.speed < parameters.maxSpeed * StallFraction)
            {
                RecoverWingsLevel();
                return Step.Failed;
            }
            var command = recipe.phases[phase];
            float rollRate = BodyRollRate();
            pitchIntegral += Mathf.Max(BodyPitchRate(), 0f) * Time.fixedDeltaTime * Mathf.Rad2Deg;
            rollIntegral += Mathf.Abs(rollRate) * Time.fixedDeltaTime * Mathf.Rad2Deg;
            float bankError = Mathf.DeltaAngle(FixedWingFormation.BankOf(aircraft), command.bankTarget);
            float noseY = aircraft.transform.forward.y;
            controlInputs.throttle = command.throttle;
            controlInputs.pitch = Mathf.Clamp(command.pitch - noseY * command.pitchLevelGain,
                command.minPitch, command.maxPitch);
            controlInputs.roll = Mathf.Clamp(command.roll + bankError * command.bankGain - rollRate * command.rollDamping,
                -command.rollLimit, command.rollLimit);
            controlInputs.yaw = 0f;
            aircraft.FilterInputs();
            if (command.Complete(pitchIntegral, rollIntegral, Time.timeSinceLevelLoad - phaseStartedAt,
                                 bankError, rollRate, noseY))
            {
                phase++;
                pitchIntegral = rollIntegral = 0f;
                phaseStartedAt = Time.timeSinceLevelLoad;
                if (phase == recipe.phases.Length)
                {
                    RecoverWingsLevel();
                    return Step.Done;
                }
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

            CompleteTask(WingOrder.Formation);
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
