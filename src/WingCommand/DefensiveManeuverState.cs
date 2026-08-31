using System;
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
        // RadarJammer stays active for 0.1 seconds after Fire. Pulse below that lifetime,
        // rather than once per physics tick, to keep continuous coverage at a known cadence.
        private const float RadarJammerPulseSeconds = 0.075f;
        private const float FixedWingRunDistance = 8000f;
        private const float RotaryRunDistance = 4000f;

        private readonly WingMember member;
        private Missile threat;
        private string countermeasureType;
        private float enteredAt;
        private float clearSince;
        private float nextThreatRefresh;
        private bool countermeasuresActive;
        private int radarJammerIndex;
        private bool radarJammerResolved;
        private bool radarJammerErrorReported;
        private float nextRadarJammerPulse;

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

            // Nothing here hovers, and this is the state that most needs the energy: a
            // thrust-vectoring wingman still configured for a hover cannot outrun anything.
            HoverAssist.Release(aircraft);

            if (aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            enteredAt = Time.timeSinceLevelLoad;
            clearSince = 0f;
            nextThreatRefresh = 0f;
            threat = null;
            countermeasureType = string.Empty;
            radarJammerIndex = -1;
            radarJammerResolved = false;
            radarJammerErrorReported = false;
            nextRadarJammerPulse = 0f;
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

            // Classify the threat from the missile, not from whether an expendable station
            // could be selected. An ECM-equipped aircraft with no chaff can legitimately get
            // an empty ChooseCountermeasure result; treating that as an unknown seeker is the
            // exact path that used to leave its jammer idle against a radar missile.
            string seekerType = threat.GetSeekerType();
            bool infrared = seekerType == "IR";
            bool radar = seekerType == "SARH" || seekerType == "ARH";

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

            // RadarJammer.Fire lasts one tenth of a second. Re-deploy at a fixed cadence
            // while a SARH/ARH threat is live, temporarily
            // selecting the jammer and then restoring the chaff station selected above.
            // When the warning clears, the final pulse expires on its own.
            if (radar) RunRadarJammer();

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

        /// <summary>Keep the native ECM countermeasure active during a radar-missile break.</summary>
        private void RunRadarJammer()
        {
            CountermeasureManager manager = aircraft != null
                ? aircraft.countermeasureManager
                : null;
            if (manager == null) return;

            int index = ResolveRadarJammer(manager);
            if (index < 0 || index > byte.MaxValue) return;
            if (Time.timeSinceLevelLoad < nextRadarJammerPulse) return;
            nextRadarJammerPulse = Time.timeSinceLevelLoad + RadarJammerPulseSeconds;

            byte previous = manager.activeIndex;
            try
            {
                manager.activeIndex = (byte)index;
                manager.DeployCountermeasure(aircraft);
            }
            catch (Exception e)
            {
                if (!radarJammerErrorReported)
                {
                    radarJammerErrorReported = true;
                    Plugin.Logger.LogWarning(
                        "[Panic] Could not activate ECM on " + aircraft.unitName + ": " +
                        e.GetType().Name + " - " + e.Message);
                }
            }
            finally
            {
                // The ordinary flare/chaff trigger is still held by SetCountermeasures.
                // Leaving the jammer selected here would silently turn that trigger into a
                // second ECM driver and stop the selected expendable from being released.
                manager.activeIndex = previous;
            }
        }

        private int ResolveRadarJammer(CountermeasureManager manager)
        {
            if (radarJammerResolved) return radarJammerIndex;

            radarJammerResolved = true;
            radarJammerIndex = -1;

            if (!CountermeasureAccess.TryFindRadarJammer(manager, out radarJammerIndex,
                                                         out string reason) &&
                !string.IsNullOrEmpty(reason))
            {
                if (!radarJammerErrorReported)
                {
                    radarJammerErrorReported = true;
                    Plugin.Logger.LogWarning(
                        "[Panic] Could not inspect ECM on " + aircraft.unitName + ": " + reason);
                }
            }

            return radarJammerIndex;
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
