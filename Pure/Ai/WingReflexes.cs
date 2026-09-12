namespace WingCommand
{
    /// <summary>Stateless built-in reflexes shared by all members; bands and scores replace
    /// update-order-dependent overrides.</summary>
    internal static class WingReflexes
    {
        /// <summary>Register built-in reflexes idempotently.</summary>
        public static void RegisterDefaults()
        {
            // Register built-ins through the same public API used by extensions.
            WingAi.Register(new DeliveryHold());
            WingAi.Register(new MissileBreak());
            WingAi.Register(new TerrainAbort());
            WingAi.Register(new LeaderLost());
            WingAi.Register(new DeckHold());
            WingAi.Register(new LeashRecall());
            WingAi.Register(new IdleCombatRejoin());
            WingAi.Register(new StandingTask());
            WingInfluences.RegisterDefaults();
        }

        /// <summary>Preserve native taxi/launch ownership for pending deliveries, ahead of missile
        /// manoeuvres. The deliveryPending control lock remains authoritative. Retain standing orders for
        /// airborne activation; reject transient manoeuvres and commandability-dependent automation while
        /// pending.</summary>
        private sealed class DeliveryHold : IWingReflex, IWingReflexLifecycle
        {
            public string Id => "wingcommand.delivery-hold";
            public WingReflexBand Band => WingReflexBand.Survival;
            public int Priority => 100;
            public string BehaviourId => WingBehaviours.Held;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent) =>
                s.DeliveryPending ? 1f : 0f;
            public bool CanHold(in WingSituation s) => s.DeliveryPending;
            public bool InterruptsMinimumHold => true;
        }

        /// <summary>Survival reflex for missiles targeting this aircraft. IsPanicking derives from its
        /// ownership; lower-priority bands cannot override it.</summary>
        private sealed class MissileBreak : IWingReflex, IWingReflexLifecycle
        {
            public string Id => "wingcommand.missile-break";
            public WingReflexBand Band => WingReflexBand.Survival;
            public int Priority => 40;
            public string BehaviourId => WingBehaviours.MissileBreak;
            public float MinimumSeconds => WingTuning.PanicMinimumSeconds;
            public bool RequiresSmartMode => false;

            public bool CanHold(in WingSituation s) => !s.DeliveryPending &&
                s.RadarAlt >= WingTuning.PanicFloorAlt &&
                (s.MissileWarned || s.SecondsSinceMissileWarning < WingTuning.PanicClearSeconds);
            public bool InterruptsMinimumHold => true;

            public float Score(in WingSituation s, bool incumbent)
            {
                // Reject hard breaks below the landing/crash floor.
                if (s.RadarAlt < WingTuning.PanicFloorAlt) return 0f;

                if (s.MissileWarned) return 0.9f;

                // Bridge brief warning gaps so reacquisition cannot release controls mid-break.
                return incumbent && s.SecondsSinceMissileWarning < WingTuning.PanicClearSeconds
                    ? 0.9f
                    : 0f;
            }
        }

        /// <summary>Pull up before turning when low and far from the leader. Near-slot low flight uses
        /// formation's own terrain bank limits.</summary>
        private sealed class TerrainAbort : IWingReflex, IWingReflexLifecycle
        {
            public string Id => "wingcommand.terrain-abort";
            public WingReflexBand Band => WingReflexBand.Survival;
            public int Priority => 50;
            public string BehaviourId => WingBehaviours.TerrainAbort;
            public float MinimumSeconds => 1.5f;
            public bool RequiresSmartMode => false;
            public bool CanHold(in WingSituation s) => TerrainAbortPolicy.AllowsRecovery(in s, incumbent: true) &&
                (!s.MissileWarned || TerrainAbortPolicy.TerrainThreat(in s, incumbent: true));
            public bool InterruptsMinimumHold => true;

            public float Score(in WingSituation s, bool incumbent) =>
                TerrainAbortPolicy.ShouldRecover(in s, incumbent)
                    ? 1f : 0f;
        }

        /// <summary>Safety hold when no leader exists, preserving standing intent. Restoring a leader
        /// releases the hold automatically so prior tasks resume.</summary>
        private sealed class LeaderLost : IWingReflex
        {
            public string Id => "wingcommand.leader-lost";
            public WingReflexBand Band => WingReflexBand.Safety;
            public string BehaviourId => WingBehaviours.DeckHold;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent)
            {
                if (s.LeaderPresent) return 0f;

                // Keep independent destination orders active without a leader.
                switch (s.Order)
                {
                    case WingOrder.FireForEffect:
                    case WingOrder.ReturnToBase:
                    case WingOrder.LandHere:
                    case WingOrder.DeliverCargo:
                    case WingOrder.MoveToPoint:
                    case WingOrder.SeekAndDestroy:
                    case WingOrder.OrbitHere:
                    case WingOrder.StandDown:
                        return 0f;
                    default:
                        return 1f;
                }
            }
        }

        /// <summary>Hold formation members above a grounded leader without replacing their directives;
        /// resume automatically after takeoff.</summary>
        private sealed class DeckHold : IWingReflex
        {
            public string Id => "wingcommand.deck-hold";
            public WingReflexBand Band => WingReflexBand.Safety;
            public string BehaviourId => WingBehaviours.DeckHold;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent)
            {
                if (!s.LeaderPresent || !s.LeaderOnDeck) return 0f;

                // Override only formation-slot orders; explicit attacks, holds, and RTB survive leader
                // landing.
                return WingOrderRules.UsesFormationSlot(s.Order) ? 1f : 0f;
            }
        }

        /// <summary>Temporarily rejoin after exceeding the hunting leash. Active behaviour supplies
        /// station-keeping weapons authority while the combat directive remains retained.</summary>
        private sealed class LeashRecall : IWingReflex, IWingReflexLifecycle
        {
            public string Id => "wingcommand.leash-recall";
            public WingReflexBand Band => WingReflexBand.Cohesion;
            public string BehaviourId => WingBehaviours.Rejoin;

            // Minimum recall hold complements the wider release threshold to prevent boundary chatter.
            public float MinimumSeconds => WingTuning.LeashHoldSeconds;
            public bool RequiresSmartMode => false;
            public bool CanHold(in WingSituation s) => !s.DeliveryPending && s.LeaderPresent &&
                s.LeashRadius > 0f && WingOrderRules.SendsWingmanHunting(s.Order);
            public bool InterruptsMinimumHold => false;

            public float Score(in WingSituation s, bool incumbent)
            {
                if (!s.LeaderPresent || s.LeashRadius <= 0f || s.LeaderDistance < 0f) return 0f;
                if (!WingOrderRules.SendsWingmanHunting(s.Order)) return 0f;

                // Use incumbent to select separate recall and release thresholds without mutable reflex
                // state.
                float threshold = incumbent
                    ? s.LeashRadius * WingTuning.LeashReleaseFraction
                    : s.LeashRadius;

                if (s.LeaderDistance <= threshold) return 0f;

                // Increase within-band urgency with proportional leash overshoot.
                float over = (s.LeaderDistance - threshold) / s.LeashRadius;
                return over < 0.02f ? 0.02f : over > 1f ? 1f : over;
            }
        }

        /// <summary>Temporarily regroup during combat inactivity while preserving the attack
        /// order.</summary>
        private sealed class IdleCombatRejoin : IWingReflex
        {
            public string Id => "wingcommand.idle-combat-rejoin";
            public WingReflexBand Band => WingReflexBand.Cohesion;
            public string BehaviourId => WingBehaviours.Rejoin;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;
            public float Score(in WingSituation s, bool incumbent) =>
                s.LeaderPresent && !s.DeliveryPending &&
                (s.Order == WingOrder.Engage || s.Order == WingOrder.Attack) &&
                s.SecondsWithoutEngagement >= WingTuning.EngageIdleSeconds ? 0.25f : 0f;
        }

        /// <summary>Always score the standing task so arbitration has a non-null default beneath temporary
        /// overrides.</summary>
        private sealed class StandingTask : IWingReflex
        {
            public string Id => "wingcommand.standing-task";
            public WingReflexBand Band => WingReflexBand.Task;
            public string BehaviourId => WingBehaviours.Task;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent) => 1f;
        }
    }
}
