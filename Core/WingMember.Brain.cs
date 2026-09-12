using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingMember
    {
        // Behaviour arbitration.
        internal DefensiveManeuverState DefensiveController => defensiveState;

        /// <summary>Resolve this member's flight behaviour once per frame, comparing delivery, defence,
        /// leash, and leader reflexes in one pass.</summary>
        public void Tick()
        {
            if (!Alive) return;
            // Transfer launch ownership before arbitration, independently of recruitment and UI
            // housekeeping.
            if (deliveryPending)
            {
                ActivateWhenAirborne();
            }
            Resolve(force: false);
        }

        /// <summary>Sample once, evaluate without side effects, then commit one control handoff.</summary>
        private void Resolve(bool force)
        {
            if (Pilot == null || Aircraft == null || !Aircraft.LocalSim || Aircraft.Player != null) return;
            // Keep recovery's parked refit hold through reflex changes; a new directive cancels refit.
            if (RefitPending && Pilot.currentState is PilotParkedState) return;
            bool warned = MissileWarned;
            float now = Time.timeSinceLevelLoad;
            float verticalSpeed = Aircraft.rb != null ? Aircraft.rb.velocity.y : 0f;
            float terrainUrgency = Aircraft.autopilot?.GetTerrainWarningSystem()?.urgency ?? 0f;
            // Urgent terrain telemetry must not wait for the optional Performance decision cadence.
            float effectiveAlt = Aircraft.radarAlt;
            if (PersonnelFacade.Roster.HasPerk(Aircraft, PilotPerk.TerrainHugger))
                effectiveAlt -= PilotPerks.TerrainFloorOffset(true);
            bool terrainDanger = !deliveryPending && !IsSurface &&
                TerrainAbortPolicy.AllowsRecovery(Order) &&
                TerrainAbortPolicy.ImmediateDanger(effectiveAlt, verticalSpeed, terrainUrgency);
            bool controlLost = !deliveryPending &&
                ((enteredState is WingPilotState && !ReferenceEquals(Pilot.currentState, enteredState)) ||
                 IsRegisteredBehaviourStale(IsSurface ? WingBehaviours.Surface : brain.Current.BehaviourId));
            if (!brain.BeginUpdate(now, warned, controlLost, force || terrainDanger,
                WingFidelity.Full ? 0f : WingFidelity.Interval(0.25f))) return;

            // Retire dead-target orders even while recall or deck hold prevents the attack state from
            // running. Safety behaviours retain control.
            if (WingOrderRules.TargetTaskComplete(Order,
                AssignedTarget != null && !AssignedTarget.disabled, deliveryPending))
            {
                if (Order == WingOrder.JamTarget)
                    WingComms.Say(this, WingComms.Call.JammingOff);
                else if (AssignedTarget != null)
                    WingComms.Say(this, WingComms.Call.Splash, AssignedTarget.unitName);
                if (!TryAdvanceQueue(directiveSerial))
                    Complete(WingOrder.Formation);
            }

            List<WingReflexTrace> trace = Plugin.Settings.VerboseLogging.Value
                ? traceBuffer ??= new List<WingReflexTrace>() : null;
            WingFlightSituation telemetry = Sample(warned, now);
            WingDecision decision = brain.Evaluate(in telemetry, directiveSerial,
                controlLost, WingFidelity.Full, trace);

            // The lifecycle adapter retires stale intent and re-evaluates; the brain never edits
            // directives.
            if (decision.DefenceCleared)
            {
                WingComms.Say(this, WingComms.Call.DefensiveClear);
                int previousRevision = directiveSerial;
                RetireStaleOrder();
                if (previousRevision != directiveSerial)
                {
                    telemetry = Sample(warned, now);
                    decision = brain.Evaluate(in telemetry, directiveSerial,
                        controlLost, WingFidelity.Full, trace);
                }
            }

            brain.Commit(in decision, now);
            if (decision.NeedsControlUpdate) EnterBehaviour(decision.Resolution.BehaviourId);
            if (trace != null && decision.BehaviourChanged)
                Plugin.LogVerbose($"[Wing] {Name} id={Aircraft.GetInstanceID()} leaderId={Leader?.GetInstanceID() ?? 0} " +
                    $"{brain.Current}  |  {Ladder(trace)}");
        }

        private List<WingReflexTrace> traceBuffer;

        /// <summary>Format reflex bands, scores, and the winner on one diagnostic line; an asterisk marks
        /// the winner.</summary>
        private static string Ladder(List<WingReflexTrace> trace)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < trace.Count; i++)
            {
                WingReflexTrace t = trace[i];
                if (i > 0) sb.Append(' ');
                sb.Append(t.Band).Append(':').Append(Short(t.Id))
                  .Append('=').Append(t.Score.ToString("0.00"));
                if (t.Won) sb.Append('*');
            }
            return sb.ToString();
        }

        /// <summary>Omit the plugin prefix already identified by the log.</summary>
        private static string Short(string id)
        {
            int dot = id.LastIndexOf('.');
            return dot >= 0 && dot < id.Length - 1 ? id.Substring(dot + 1) : id;
        }

        private WingFlightSituation Sample(bool warned, float now)
        {
            Aircraft leader = Leader;
            float leaderDistance = -1f;
            if (leader != null && !leader.disabled)
            {
                leaderDistance = Mathf.Sqrt(
                    FastMath.SquareDistance(Aircraft.GlobalPosition(), leader.GlobalPosition()));
            }

            RefreshSlowSamples(now);

            AircraftParameters p = Aircraft.GetAircraftParameters();
            float takeoffSpeed = p != null ? p.takeoffSpeed : 70f;
            bool isRotary = WingRegistry.IsRotary(Aircraft);
            Vector3 airVelocity = Aircraft.rb != null ? Aircraft.rb.velocity :
                Aircraft.transform.forward * Aircraft.speed;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level != null) airVelocity -= level.GetWind(Aircraft.GlobalPosition());
            float forwardAirspeed = Mathf.Max(0f, Vector3.Dot(airVelocity, Aircraft.transform.forward));
            float minimumAirspeed = !isRotary && p != null
                ? FormationGuidance.MinimumAirspeed(Aircraft.definition.aircraftInfo?.stallSpeed ?? 0f, p.landingSpeed)
                : 0f;

            var situation = new WingSituation(
                order: Order,
                roe: CombatFacade.Roe.Current,
                deliveryPending: deliveryPending,
                missileWarned: warned,
                secondsSinceMissileWarning: brain.SecondsSinceWarning(now),
                leaderOnDeck: owner != null && owner.LeaderOnDeck,
                leaderPresent: leaderDistance >= 0f,
                targetAlive: AssignedTarget != null && !AssignedTarget.disabled,
                leaderDistance: leaderDistance,
                leashRadius: Plugin.Settings != null ? Plugin.Settings.LeashDistance.Value : WingTuning.LeashRadius,
                radarAlt: Aircraft.radarAlt,
                memberIsSurface: IsSurface,
                memberIsRotary: isRotary,
                airspeed: forwardAirspeed,
                takeoffSpeed: takeoffSpeed,
                fuel: sampledFuel,
                ammo: sampledAmmo,
                integrity: sampledIntegrity,
                secondsInBehaviour: brain.SecondsInBehaviour(now))
                .WithEngagementIdle(now - engageActivityAt)
                .WithFlightSafety(Aircraft.autopilot?.GetTerrainWarningSystem()?.urgency ?? 0f,
                    Aircraft.rb != null ? Aircraft.rb.velocity.y : 0f,
                    !isRotary && !IsSurface ? FixedWingFormation.BankOf(Aircraft) : 0f,
                    minimumAirspeed, brain.Defensive);

            Vector3 toLeader = leader != null ? leader.GlobalPosition() - Aircraft.GlobalPosition() : Vector3.zero;
            float closing = leader?.rb != null && Aircraft.rb != null && toLeader.sqrMagnitude > 1f
                ? Vector3.Dot(Aircraft.rb.velocity - leader.rb.velocity, toLeader.normalized) : 0f;
            return new WingFlightSituation(in situation,
                SlotError > 0f ? SlotError : Mathf.Max(0f, leaderDistance), WingTuning.CaptureDistance,
                closing, leader != null ? leader.speed : 0f, PersonnelFacade.Roster.SkillBonus(Aircraft) / 0.24f)
                .WithMinimumAirspeed(minimumAirspeed);
        }

        private int sampledAmmo = 1;
        private float sampledIntegrity = 1f;
        private float sampledFuel = 1f;
        private float nextSlowSample;

        /// <summary>Cache ammunition, integrity, and fuel on a slow timer. Each traverses aircraft
        /// collections; reflexes and influences share these samples.</summary>
        private void RefreshSlowSamples(float now)
        {
            if (now < nextSlowSample) return;
            nextSlowSample = now + WingFidelity.Interval(1f);

            sampledAmmo = Ammo;
            sampledIntegrity = Integrity;
            sampledFuel = Fuel;
        }

        /// <summary>Whether an airborne missile currently targets this aircraft.</summary>
        private bool MissileWarned
        {
            get
            {
                MissileWarning warning = Aircraft != null
                    ? Aircraft.GetMissileWarningSystem()
                    : null;
                return warning != null && warning.IsWarning();
            }
        }

    }
}
