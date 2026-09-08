using UnityEngine;

namespace WingCommand
{
 /// <summary>Temporary missile-defence flight state that preserves standing intent. Reflex arbitration
 /// determines entry and release, then resumes the directive when appropriate.</summary>
    internal sealed class DefensiveManeuverState : WingPilotState
    {
        // Do not scale safety-critical threat refresh with fidelity mode.
        private const float ThreatRefreshSeconds = 0.2f;
        private const float FixedWingRunDistance = 8000f;
        private const float RotaryRunDistance = 4000f;

        private readonly RadarJammerPulser jammer = new RadarJammerPulser();
        private Missile threat;

     /// <summary>Matching expendable station index, or -1 if unavailable.</summary>
        private int expendableIndex = -1;
        private float nextThreatRefresh;
        private bool countermeasuresActive;

        public DefensiveManeuverState(WingMember member) : base(member)
        {
            stateDisplayName = "MISSILE - DEFENSIVE";
        }

        public override void EnterState(Pilot pilot)
        {
            // Release hover to restore energy for missile evasion.
            BeginFlight(pilot);

            nextThreatRefresh = 0f;
            threat = null;
            expendableIndex = -1;
            jammer.Reset();
            RefreshThreat(force: true);

            string detail = threat != null ? threat.GetSeekerType() : null;
            WingComms.Say(member, WingComms.Call.Panic, detail);

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Plugin.LogVerbose(
                    $"[Panic] {aircraft.unitName} defensive against " +
                    (threat != null ? threat.unitName + " (" + detail + ")" : "missile warning"));
            }
        }

     /// <summary>Stop countermeasures on exit. The reflex owns release timing; WingMember handles
     /// all-clear chatter and stale-order retirement.</summary>
        public override void LeaveState()
        {
            StopCountermeasures();
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.disabled) return;

            RefreshThreat(force: false);
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            bool warned = warning != null && warning.IsWarning();

            // Stop dispensing without a live warning, but retain the last break while reflex hold time
            // bridges temporary tracking gaps.
            if (!warned || threat == null || threat.disabled)
            {
                StopCountermeasures();
                return;
            }

            FlyDefensive();
        }

        private void RefreshThreat(bool force)
        {
            if (!force && Time.timeSinceLevelLoad < nextThreatRefresh) return;
            nextThreatRefresh = Time.timeSinceLevelLoad + ThreatRefreshSeconds;

            MissileWarning warning = aircraft != null ? aircraft.GetMissileWarningSystem() : null;
            if (warning == null || !warning.TryGetNearestIncoming(out Missile nearest))
            {
                threat = null;
                return;
            }

            if (nearest == threat) return;

            StopCountermeasures();
            threat = nearest;
            expendableIndex = -1;

            // Resolve expendables explicitly; native name-sorted selection may choose RadarJammer
            // instead of chaff for shared ARH/SARH threat types.
            if (aircraft.countermeasureManager == null) return;

            if (!CountermeasureAccess.TryFindExpendable(
                    aircraft.countermeasureManager, threat.GetSeekerType(),
                    out expendableIndex, out string reason) &&
                !string.IsNullOrEmpty(reason))
            {
                Plugin.Logger.LogWarning(
                    "[CM] Could not resolve a dispenser on " + aircraft.unitName + ": " + reason);
            }
        }

        private void FlyDefensive()
        {
            Vector3 toThreat = threat.GlobalPosition() - aircraft.GlobalPosition();
            Vector3 relativeVelocity = threat.rb != null && aircraft.rb != null
                ? threat.rb.velocity - aircraft.rb.velocity
                : Vector3.zero;
            float closing = toThreat.sqrMagnitude > 1f
                ? Mathf.Max(Vector3.Dot(-toThreat.normalized, relativeVelocity), 1f)
                : 1f;
            float impactTime = toThreat.magnitude / closing;

            Vector3 away = -toThreat;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) away = -aircraft.transform.forward;
            away.Normalize();

            Vector3 beamA = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 beamB = -beamA;
            Vector3 beam = Vector3.Dot(beamA, aircraft.transform.forward) >=
                           Vector3.Dot(beamB, aircraft.transform.forward) ? beamA : beamB;

            // Classify the seeker from the missile, even when no matching expendable exists, so radar
            // threats still activate ECM.
            string seekerType = threat.GetSeekerType();
            bool infrared = seekerType == "IR";
            bool radar = seekerType == "SARH" || seekerType == "ARH";

            // Radar threats use beam/clutter; IR threats reduce power and flare. Below 3 seconds to
            // impact, break across the missile line of sight.
            bool terminal = impactTime < 3.0f;

            Vector3 direction;
            if (radar)
            {
                direction = (beam + away * 0.15f).normalized;
            }
            else if (terminal)
            {
                // Slice across the line of sight to force terminal tracking overshoot.
                direction = (beam * 0.90f + away * 0.20f).normalized;
            }
            else
            {
                direction = (away + beam * 0.65f).normalized;
            }

            float vertical = 0f;
            if (radar && aircraft.radarAlt > 140f) vertical = -0.15f;
            if (terminal || aircraft.radarAlt < 120f) vertical = 0.25f;
            direction = (direction + Vector3.up * vertical).normalized;

            // Idle during terminal IR evasion to reduce engine heat and aid flares.
            controlInputs.throttle = infrared ? (terminal ? 0f : 0.15f) : 1f;

            // Hold dispense only within useful threat windows; native ejectors enforce their own
            // cadence.
            bool dispense = expendableIndex >= 0 &&
                            (infrared || impactTime < WingTuning.ChaffWindowSeconds);
            SetCountermeasures(dispense);

            // Pulse ECM during radar warnings; the pulser restores chaff selection. The final
            // 0.1-second pulse expires after warning clearance.
            if (radar) jammer.Pulse(aircraft);

            bool rotary = WingRegistry.IsRotary(aircraft);
            float runDistance = rotary ? RotaryRunDistance : FixedWingRunDistance;
            GlobalPosition destination = aircraft.GlobalPosition() + direction * runDistance;

            if (!rotary)
            {
                float bankLimit = terminal
                    ? FixedWingFormation.MaxSafeBank
                    : WingTuning.DefensiveBankAllowed;

                aircraft.autopilot.AutoAim(
                    destination: destination,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 2f,
                    bankAllowed: bankLimit,
                    followTerrain: radar,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft,
                        radar ? Mathf.Max(aircraft.maxRadius, 100f) : aircraft.radarAlt),
                    targetVelocity: Vector3.zero);
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: destination,
                    altitudeHold: AutopilotMath.RotaryAgl(
                        aircraft, radar ? 50f : aircraft.radarAlt, 25f, 1000f),
                    aimDirection: direction,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }
        }

     /// <summary>Set the dispense trigger on the resolved expendable index; activeIndex is temporarily
     /// borrowed by the jammer pulser.</summary>
        private void SetCountermeasures(bool active)
        {
            if (aircraft == null || aircraft.countermeasureManager == null) return;
            if (active == countermeasuresActive) return;
            if (active && (expendableIndex < 0 || expendableIndex > byte.MaxValue)) return;

            aircraft.Countermeasures(active, (byte)Mathf.Max(expendableIndex, 0));
            countermeasuresActive = active;
        }

        private void StopCountermeasures()
        {
            if (!countermeasuresActive || aircraft == null || aircraft.countermeasureManager == null)
                return;

            if (aircraft.countermeasureTrigger)
                aircraft.Countermeasures(false, (byte)Mathf.Max(expendableIndex, 0));
            countermeasuresActive = false;
        }
    }
}
