using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Temporary self-preservation interrupt for a wingman under missile attack.
    ///
    /// This state does not replace the standing order. Formation, attack, orbit, cargo and
    /// RTB are merely paused: the missile-break reflex outscores everything while a missile
    /// is airborne, and the arbiter resolves straight back to the standing directive once it
    /// stops. That distinction is what makes defensive behaviour feel reactive instead of
    /// making the AI forget what the player told it to do.
    /// </summary>
    internal sealed class DefensiveManeuverState : WingPilotState
    {
        // Missile evasion is safety-critical: this interval is deliberately NOT routed
        // through WingBrain.Interval, so Performance mode never slows the threat refresh.
        private const float ThreatRefreshSeconds = 0.2f;
        private const float FixedWingRunDistance = 8000f;
        private const float RotaryRunDistance = 4000f;

        private readonly RadarJammerPulser jammer = new RadarJammerPulser();
        private Missile threat;

        /// <summary>Station holding the expendable that answers this threat, or -1.</summary>
        private int expendableIndex = -1;
        private float nextThreatRefresh;
        private bool countermeasuresActive;

        public DefensiveManeuverState(WingMember member) : base(member)
        {
            stateDisplayName = "MISSILE - DEFENSIVE";
        }

        public override void EnterState(Pilot pilot)
        {
            // This is the state that most needs the energy, so the hover regime always comes off.
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
                Plugin.Logger.LogInfo(
                    $"[Panic] {aircraft.unitName} defensive against " +
                    (threat != null ? threat.unitName + " (" + detail + ")" : "missile warning"));
            }
        }

        /// <summary>
        /// The break is over — the arbiter has given the aircraft to something else.
        ///
        /// This state no longer decides when that happens, and no longer announces it. It
        /// used to run its own clear timer and call back into the member to resume, which
        /// made it both a behaviour and half of the precedence system. The reflex owns the
        /// timing, and <see cref="WingMember"/> owns the all-clear call and the retirement of
        /// a stale order — it can tell a real release from a teardown, and this cannot.
        ///
        /// So all that is left is putting the countermeasures away.
        /// </summary>
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

            // Nothing to run from this instant. Stop dispensing, but keep flying the last
            // commanded break: the reflex holds this state for a couple of seconds after the
            // warning drops, precisely so a missile that is briefly lost and re-acquired
            // does not get the controls handed back mid-turn.
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

            // Resolve the dispenser ourselves rather than asking ChooseCountermeasure.
            // ChaffEjector and RadarJammer declare the same { "ARH", "SARH" } threat types,
            // and the game picks the first match from a list sorted by display name - so on
            // an aircraft carrying both, whether a radar missile gets chaff or a held
            // trigger on the jammer came down to alphabetical order. See
            // CountermeasureAccess.TryFindExpendable.
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

            // Classify the threat from the missile, not from whether an expendable station
            // could be selected. An ECM-equipped aircraft with no chaff can legitimately get
            // an empty ChooseCountermeasure result; treating that as an unknown seeker is the
            // exact path that used to leave its jammer idle against a radar missile.
            string seekerType = threat.GetSeekerType();
            bool infrared = seekerType == "IR";
            bool radar = seekerType == "SARH" || seekerType == "ARH";

            // Radar: place the threat on the beam and descend toward clutter. IR: unload
            // the engine, dispense flares, and open aspect away from the missile.
            // When terminal (impact < 3s), execute a maximum-G break across the missile LOS.
            bool terminal = impactTime < 3.0f;

            Vector3 direction;
            if (radar)
            {
                direction = (beam + away * 0.15f).normalized;
            }
            else if (terminal)
            {
                // Terminal break: hard slice across threat line-of-sight to force tracking overshoot.
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

            // Idle throttle on terminal IR evasion to cool engine and maximize flare effectiveness.
            controlInputs.throttle = infrared ? (terminal ? 0f : 0.15f) : 1f;

            // Dispense only inside the useful window. The ejectors rate-limit themselves
            // (ChaffEjector.Fire refuses inside its own ejectionInterval), so holding the
            // trigger across the window paces the load rather than dumping it.
            //
            // The radar window was eight seconds of predicted time-to-impact, computed as
            // range over closing rate. An ARH detected at thirty kilometres sits far outside
            // that for most of its flight and only starts getting chaff once the notch is
            // already committed, which is the wrong half of the engagement.
            bool dispense = expendableIndex >= 0 &&
                            (infrared || impactTime < WingTuning.ChaffWindowSeconds);
            SetCountermeasures(dispense);

            // RadarJammer.Fire lasts one tenth of a second. Re-deploy at a fixed cadence
            // while a SARH/ARH threat is live; the pulser temporarily selects the jammer
            // and restores the chaff station selected above. When the warning clears, the
            // final pulse expires on its own.
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

        /// <summary>
        /// Hold or release the dispense trigger on the station we resolved, naming the index
        /// explicitly rather than reusing whatever <c>activeIndex</c> happens to be. The
        /// jammer pulser borrows that field and restores it, so reading it here was reading
        /// a value another system owns.
        /// </summary>
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
