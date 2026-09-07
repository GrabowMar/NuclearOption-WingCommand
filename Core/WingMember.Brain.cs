using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingMember
    {
        // ------------------------------------------------------------------- arbitration

        /// <summary>
        /// The one place this wingman decides what to fly. Called once per frame from the
        /// wing's update.
        ///
        /// Everything that used to reach in and switch a pilot state on its own - the
        /// missile check, the leash check, the leader-on-deck sweep, the delivery lockout -
        /// now arrives as a reflex score and is compared against the others in one pass.
        /// </summary>
        public void Tick()
        {
            if (!Alive) return;
            // Launch ownership is a flight lifecycle transition. Do it before arbitration,
            // independently of the recruitment/UI queue's housekeeping pass.
            if (deliveryPending)
            {
                ActivateWhenAirborne();
            }
            Resolve(force: false);
        }

        /// <summary>Sample once, decide without side effects, then apply one control handoff.</summary>
        private void Resolve(bool force)
        {
            if (Pilot == null || Aircraft == null) return;
            // Recovery owns a refit waiting on the ground for its departure lane. Keep
            // that parked hold through reflex changes; a new directive cancels the refit.
            if (RefitPending && Pilot.currentState is PilotParkedState) return;
            bool warned = MissileWarned;
            float now = Time.timeSinceLevelLoad;
            bool controlLost = !deliveryPending &&
                ((enteredState is WingPilotState && !ReferenceEquals(Pilot.currentState, enteredState)) ||
                 IsRegisteredBehaviourStale(IsSurface ? WingBehaviours.Surface : brain.Current.BehaviourId));
            if (!brain.BeginUpdate(now, warned, controlLost, force,
                WingFidelity.Full ? 0f : WingFidelity.Interval(0.25f))) return;

            // A designated unit can die while recall/deck hold owns flight, so the attack
            // state may never run again to report completion. Retire that one-shot intent
            // here before sampling; warning and other safety owners keep their controls.
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

            // The brain never edits intent. The task lifecycle adapter retires obsolete
            // one-shot orders, then evaluates again before committing any transition.
            if (decision.LeavesMissileBreak)
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
                Plugin.LogVerbose($"[Wing] {Name} {brain.Current}  |  {Ladder(trace)}");
        }

        private List<WingReflexTrace> traceBuffer;

        /// <summary>
        /// The whole ladder on one line: who scored what, and who won. Reads as
        /// <c>survival:missile-break=0.90* safety:deck-hold=0.00 task:standing-task=1.00</c>.
        /// </summary>
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

        /// <summary>Drop the owning plugin's prefix; the log line already says whose wing it is.</summary>
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

            var situation = new WingSituation(
                order: Order,
                roe: RoeRules.Current,
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
                secondsInBehaviour: brain.SecondsInBehaviour(now)).WithEngagementIdle(now - engageActivityAt);

            Vector3 toLeader = leader != null ? leader.GlobalPosition() - Aircraft.GlobalPosition() : Vector3.zero;
            float closing = leader?.rb != null && Aircraft.rb != null && toLeader.sqrMagnitude > 1f
                ? Vector3.Dot(Aircraft.rb.velocity - leader.rb.velocity, toLeader.normalized) : 0f;
            float minimumAirspeed = !isRotary && p != null
                ? FormationGuidance.MinimumAirspeed(Aircraft.definition.aircraftInfo?.stallSpeed ?? 0f, p.landingSpeed)
                : 0f;
            return new WingFlightSituation(in situation,
                SlotError > 0f ? SlotError : Mathf.Max(0f, leaderDistance), WingTuning.CaptureDistance,
                closing, leader != null ? leader.speed : 0f, WingPilotRoster.SkillBonus(Aircraft) / 0.24f)
                .WithMinimumAirspeed(minimumAirspeed);
        }

        private int sampledAmmo = 1;
        private float sampledIntegrity = 1f;
        private float sampledFuel = 1f;
        private float nextSlowSample;

        /// <summary>
        /// The three expensive fields of the situation, refreshed on a slow timer.
        ///
        /// Each of them walks a collection: ammunition every weapon station, condition every
        /// airframe part, and fuel every tank twice over — <c>Aircraft.GetFuelLevel</c> sums
        /// capacity and level across the lot on every call. None of the three moves fast
        /// enough to be worth that per member per frame.
        ///
        /// Condition influences use fuel and integrity; extension reflexes and influences
        /// share these samples instead of walking the live aircraft independently.
        /// </summary>
        private void RefreshSlowSamples(float now)
        {
            if (now < nextSlowSample) return;
            nextSlowSample = now + WingFidelity.Interval(1f);

            sampledAmmo = Ammo;
            sampledIntegrity = Integrity;
            sampledFuel = Fuel;
        }

        /// <summary>True when a missile is airborne and this aircraft is its target.</summary>
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
