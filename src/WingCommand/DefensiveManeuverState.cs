using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Temporary self-preservation interrupt for a wingman under missile attack.
    ///
    /// This state does not replace the standing order. Formation, attack, orbit, cargo and
    /// RTB are merely paused, then resumed by <see cref="WingMember.ResumeAfterPanic"/> once
    /// the warning has stayed clear. That distinction is what makes defensive behavior feel
    /// reactive instead of making the AI forget what the player told it to do.
    /// </summary>
    internal sealed class DefensiveManeuverState : PilotBaseState
    {
        private const float MinimumDefenceSeconds = 2f;
        private const float ThreatRefreshSeconds = 0.2f;
        private const float FixedWingRunDistance = 8000f;
        private const float RotaryRunDistance = 4000f;

        private readonly WingMember member;
        private Missile threat;
        private string countermeasureType;
        private float enteredAt;
        private float clearSince;
        private float nextThreatRefresh;
        private bool countermeasuresActive;

        public DefensiveManeuverState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "MISSILE - DEFENSIVE";
        }

        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();
            aircraft.SetFlightAssist(enabled: true);
            if (aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            enteredAt = Time.timeSinceLevelLoad;
            clearSince = 0f;
            nextThreatRefresh = 0f;
            threat = null;
            countermeasureType = string.Empty;
            RefreshThreat(force: true);

            string detail = threat != null ? threat.GetSeekerType() : null;
            WingComms.Say(member, WingComms.Call.Panic, detail);

            if (Plugin.Config2.VerboseLogging.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[Panic] {aircraft.unitName} defensive against " +
                    (threat != null ? threat.unitName + " (" + detail + ")" : "missile warning"));
            }
        }

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

            if (!warned || threat == null || threat.disabled)
            {
                if (clearSince <= 0f) clearSince = Time.timeSinceLevelLoad;
                StopCountermeasures();

                if (Time.timeSinceLevelLoad - enteredAt >= MinimumDefenceSeconds &&
                    Time.timeSinceLevelLoad - clearSince >= Plugin.Config2.PanicClearSeconds.Value)
                {
                    WingComms.Say(member, WingComms.Call.DefensiveClear);
                    member.ResumeAfterPanic();
                }
                return;
            }

            clearSince = 0f;
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
            countermeasureType = aircraft.countermeasureManager != null
                ? aircraft.countermeasureManager.ChooseCountermeasure(threat)
                : string.Empty;
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

            bool infrared = countermeasureType == "IR";
            bool radar = countermeasureType == "SARH" || countermeasureType == "ARH";

            // Radar: place the threat on the beam and descend toward clutter. IR: unload
            // the engine, dispense flares, and open aspect away from the missile. Unknown
            // seekers get a conservative blend of both rather than no reaction.
            Vector3 direction = radar
                ? (beam + away * 0.18f).normalized
                : (away + beam * 0.65f).normalized;

            float vertical = 0f;
            if (radar && aircraft.radarAlt > 180f) vertical = -0.12f;
            if (impactTime < 2.5f || aircraft.radarAlt < 100f) vertical = 0.22f;
            direction = (direction + Vector3.up * vertical).normalized;

            controlInputs.throttle = infrared ? 0.15f : 1f;

            // Dispense continuously only inside the useful window. ChooseCountermeasure has
            // already selected flare/chaff for the seeker; an empty type means this airframe
            // has no matching station and avoids an invalid activeIndex.
            bool dispense = !string.IsNullOrEmpty(countermeasureType) &&
                            (infrared || impactTime < 8f);
            SetCountermeasures(dispense);

            float runDistance = aircraft.autopilot is AutopilotPlane
                ? FixedWingRunDistance
                : RotaryRunDistance;
            GlobalPosition destination = aircraft.GlobalPosition() + direction * runDistance;

            if (aircraft.autopilot is AutopilotPlane)
            {
                aircraft.autopilot.AutoAim(
                    destination: destination,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 2f,
                    bankAllowed: FixedWingFormation.MaxSafeBank,
                    followTerrain: radar,
                    altitudeHold: Mathf.Clamp(
                        radar ? Mathf.Max(aircraft.maxRadius, 120f) : aircraft.radarAlt,
                        aircraft.maxRadius, 8000f),
                    targetVelocity: Vector3.zero);
            }
            else
            {
                AircraftParameters parameters = aircraft.GetAircraftParameters();
                float height = Mathf.Clamp(
                    Mathf.Max(parameters.minimumRadarAlt, radar ? 50f : aircraft.radarAlt),
                    25f, 1000f);
                aircraft.autopilot.AutoAim(
                    destination: destination,
                    altitudeHold: height,
                    aimDirection: direction,
                    targetVelocity: Vector3.zero,
                    followTerrain: true);
            }
        }

        private void SetCountermeasures(bool active)
        {
            if (aircraft == null || aircraft.countermeasureManager == null) return;
            if (active == countermeasuresActive) return;

            aircraft.Countermeasures(active, aircraft.countermeasureManager.activeIndex);
            countermeasuresActive = active;
        }

        private void StopCountermeasures()
        {
            if (!countermeasuresActive || aircraft == null || aircraft.countermeasureManager == null)
                return;

            if (aircraft.countermeasureTrigger)
                aircraft.Countermeasures(false, aircraft.countermeasureManager.activeIndex);
            countermeasuresActive = false;
        }
    }
}
