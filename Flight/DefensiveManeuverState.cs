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
        private Vector3 toThreat;
        private float impactTime;
        private bool infrared, radar, semiActive;
        private int notchSide;
        private float notchAltitude;
        private float nextIntercept;

        public DefensiveManeuverState(WingMember member) : base(member)
        {
            stateDisplayName = "MISSILE - DEFENSIVE";
        }

        public override void EnterState(Pilot pilot)
        {
            // Release hover to restore energy for missile evasion.
            BeginFlight(pilot);
            notchAltitude = Mathf.Max(100f, aircraft.radarAlt);

            // Terrain recovery shares this controller's threat and jammer cadence. Re-entering
            // defence preserves scan and ECM pulse timing.
            RefreshThreat();

            string detail = threat != null ? threat.GetSeekerType() : null;
            WingComms.Say(member, WingComms.Call.Panic, detail);

            if (Plugin.Settings.VerboseLogging.Value)
            {
                Plugin.LogVerbose(
                    $"[Panic] {aircraft.unitName} id={aircraft.GetInstanceID()} defensive against " +
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

            // Retain defensive ownership across brief warning gaps, but never retain stale roll/pitch
            // or an IR idle throttle. Keep flying and recovering until a threat reappears.
            if (!ServiceCountermeasures(pilot))
            {
                AutopilotMath.RecoverFlight(aircraft, controlInputs);
                return;
            }

            FlyDefensive();
        }

        /// <summary>Service expendables and ECM independently of flight control. Terrain recovery can
        /// retain missile protection without executing evasive steering or reducing recovery power.</summary>
        internal bool ServiceCountermeasures(Pilot pilot)
        {
            // A terrain warning may win before this cached defensive state has ever been entered.
            if (aircraft == null) BindControls(pilot);
            if (aircraft == null || aircraft.disabled) return false;
            RefreshThreat();
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            if (warning == null || !warning.IsWarning() || threat == null || threat.disabled)
            {
                StopCountermeasures();
                return false;
            }

            toThreat = threat.GlobalPosition() - aircraft.GlobalPosition();
            Vector3 relativeVelocity = threat.rb != null && aircraft.rb != null
                ? threat.rb.velocity - aircraft.rb.velocity
                : Vector3.zero;
            float closing = toThreat.sqrMagnitude > 1f
                ? Mathf.Max(Vector3.Dot(-toThreat.normalized, relativeVelocity), 1f)
                : 1f;
            impactTime = toThreat.magnitude / closing;
            // Classify the missile even without an expendable so radar threats still activate ECM.
            string seekerType = threat.GetSeekerType();
            infrared = seekerType == "IR";
            semiActive = seekerType == "SARH";
            radar = seekerType == "SARH" || seekerType == "ARH";

            // Native ejectors own dispensing cadence; the pulser restores the selected expendable.
            float cmWindow = WingTuning.ChaffWindowSeconds;
            if (PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.EarlyWarning))
                cmWindow += PilotPerks.EarlyWarningReactionLead(true);
            SetCountermeasures(expendableIndex >= 0 &&
                (infrared || impactTime < cmWindow));
            if (radar) jammer.Pulse(aircraft);
            return true;
        }

        private void RefreshThreat()
        {
            if (Time.timeSinceLevelLoad < nextThreatRefresh) return;
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
            notchSide = 0;
            expendableIndex = -1;

            // Resolve expendables explicitly; native name-sorted selection may choose RadarJammer
            // instead of chaff for shared ARH/SARH threat types.
            if (aircraft.countermeasureManager == null) return;

            if (!CombatFacade.Countermeasures.TryFindExpendable(
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
            bool intercept = false;
            if (semiActive && CombatFacade.Roe.Current == WingRoe.Hold)
            {
                // Keep shooting while defensive; the normal slot engagement loop is suspended here.
                if (Time.timeSinceLevelLoad >= nextIntercept)
                {
                    nextIntercept = Time.timeSinceLevelLoad + 1f;
                    if (CombatFacade.Weapons.InterceptMissiles(aircraft, pilot, aircraft))
                        WingComms.Say(member, WingComms.Call.Defending);
                }

                WingRegistry wing = WingCommandManager.Instance?.Wing;
                Aircraft player = wing?.Leader;
                float distance = player != null && !player.disabled && player.Player != null
                    ? FastMath.Distance(aircraft.GlobalPosition(), player.GlobalPosition()) : -1f;
                float leash = Plugin.Settings?.LeashDistance.Value ?? WingTuning.LeashRadius;
                bool covered = false;
                if (distance >= 0f && distance <= leash)
                {
                    foreach (WingMember other in wing.Members)
                    {
                        if (other == member || !other.IsCommandable || other.IsSurface ||
                            other.Aircraft.Player != null || other.IsPanicking ||
                            !WingOrderRules.UsesFormationSlot(other.Order)) continue;
                        if (FastMath.Distance(other.Aircraft.GlobalPosition(), player.GlobalPosition()) <= leash)
                        {
                            covered = true;
                            break;
                        }
                    }
                }
                intercept = MissileDefencePolicy.PreferInterception(CombatFacade.Roe.Current, "SARH",
                    CombatFacade.Weapons.HasMissileDefence(aircraft), impactTime, covered, distance, leash);
            }

            Vector3 away = -toThreat;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) away = -aircraft.transform.forward;
            away.Normalize();

            Vector3 beamA = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 beamB = -beamA;
            Vector3 beam = Vector3.Dot(beamA, aircraft.transform.forward) >=
                           Vector3.Dot(beamB, aircraft.transform.forward) ? beamA : beamB;

            // Radar threats use beam/clutter; IR threats reduce power and flare. Below 3 seconds to
            // impact, break across the missile line of sight.
            bool terminal = impactTime < 3.0f;

            Vector3 direction;
            if (intercept)
            {
                // Preserve the firing heading until impact is imminent or nearby wing cover permits a notch.
                direction = Vector3.ProjectOnPlane(aircraft.transform.forward, Vector3.up).normalized;
            }
            else if (semiActive)
            {
                // SARH depends on the illuminating radar, which can be remote from the launcher
                // and missile. The native seeker already exposes that exact evasion point.
                Vector3 source = threat.GetEvasionPoint() - aircraft.GlobalPosition();
                Aircraft leader = member.Leader;
                Vector3 toLeader = leader != null && !leader.disabled
                    ? leader.GlobalPosition() - aircraft.GlobalPosition() : Vector3.zero;
                var notch = RadarDefenceGeometry.Notch(source.x, source.z,
                    aircraft.transform.forward.x, aircraft.transform.forward.z, toLeader.x, toLeader.z,
                    impactTime >= WingTuning.ChaffWindowSeconds &&
                    WingOrderRules.UsesFormationSlot(member.Order), notchSide);
                notchSide = notch.side;
                direction = new Vector3(notch.x, 0f, notch.z);
            }
            else if (radar)
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
            if (!semiActive)
            {
                if (radar && aircraft.radarAlt > 140f) vertical = -0.15f;
                if (terminal || aircraft.radarAlt < 120f) vertical = 0.25f;
            }
            direction = (direction + Vector3.up * vertical).normalized;

            // Idle during terminal IR evasion to reduce engine heat and aid flares.
            controlInputs.throttle = infrared ? (terminal ? 0f : 0.15f) : 1f;

            bool rotary = WingRegistry.IsRotary(aircraft);
            float runDistance = rotary ? RotaryRunDistance : FixedWingRunDistance;
            GlobalPosition destination = aircraft.GlobalPosition() + direction * runDistance;

            if (!rotary)
            {
                bool hasBreakTurn = PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.BreakTurn);
                float bankLimit = SharpTurnPolicy.DefensiveBankLimit(terminal, hasBreakTurn, aircraft.radarAlt);

                if (terminal && hasBreakTurn)
                {
                    controlInputs.brake = 1f;
                }

                aircraft.autopilot.AutoAim(
                    destination: destination,
                    aimVelocity: true,
                    ignoreCollisions: false,
                    runwayAlign: false,
                    effort: 2f,
                    bankAllowed: bankLimit,
                    followTerrain: radar,
                    altitudeHold: AutopilotMath.CruiseHold(aircraft,
                        semiActive ? notchAltitude :
                        radar ? Mathf.Max(aircraft.maxRadius, 100f) : aircraft.radarAlt),
                    targetVelocity: Vector3.zero);
            }
            else
            {
                aircraft.autopilot.AutoAim(
                    destination: destination,
                    altitudeHold: AutopilotMath.RotaryAgl(
                        aircraft, semiActive ? notchAltitude : radar ? 50f : aircraft.radarAlt, 25f, 1000f),
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

        internal void StopCountermeasures()
        {
            if (!countermeasuresActive || aircraft == null || aircraft.countermeasureManager == null)
                return;

            if (aircraft.countermeasureTrigger)
                aircraft.Countermeasures(false, (byte)Mathf.Max(expendableIndex, 0));
            countermeasuresActive = false;
        }
    }
}
