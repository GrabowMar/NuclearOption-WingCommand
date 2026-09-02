namespace WingCommand
{
    /// <summary>
    /// The reflexes this mod ships, and the five overrides they replace.
    ///
    /// Each one used to be a bespoke mechanism scattered across two files — a boolean here,
    /// a HashSet there, a direct <c>SwitchState</c> that bypassed the order system
    /// entirely. They are collected here as one shape so that the precedence between them
    /// is a table you can read rather than the order of three lines in an update loop.
    ///
    /// All of them are stateless. One instance serves every wingman in the wing.
    /// </summary>
    internal static class WingReflexes
    {
        /// <summary>Register the built-in set. Idempotent.</summary>
        public static void RegisterDefaults()
        {
            // Through the public call, exactly as a third-party plugin would. If the core
            // took a shortcut here the public path would be the untested one.
            WingAi.Register(new DeliveryHold());
            WingAi.Register(new MissileBreak());
            WingAi.Register(new LeaderLost());
            WingAi.Register(new DeckHold());
            WingAi.Register(new LeashRecall());
            WingAi.Register(new StandingTask());
        }

        /// <summary>
        /// A hangar delivery still under the airbase's taxi and launch AI.
        ///
        /// Scores above the missile break, which is not a tie-break accident: this aircraft
        /// is not ours to fly yet, and commandeering a parked one to make it dodge would
        /// drive it off the apron.
        ///
        /// It does <b>not</b> replace the <c>deliveryPending</c> lockout, which is still
        /// enforced in <c>Apply</c>, <c>Complete</c>, <c>IsCommandable</c> and the dispatcher
        /// — and should be. This reflex stops the arbiter flying the aircraft; those stop it
        /// being given orders in the first place, which is a different question with a worse
        /// failure mode.
        /// </summary>
        private sealed class DeliveryHold : IWingReflex
        {
            public string Id => "wingcommand.delivery-hold";
            public WingReflexBand Band => WingReflexBand.Survival;
            public string BehaviourId => WingBehaviours.Held;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent) =>
                s.DeliveryPending ? 1f : 0f;
        }

        /// <summary>
        /// A missile is in the air and this aircraft is the one it is chasing.
        ///
        /// <c>IsPanicking</c> used to be a stored boolean that four unrelated checks had to
        /// remember to guard on, where forgetting one silently disabled missile defence. It
        /// still exists and callers still branch on it, but it is <i>derived</i> from this
        /// reflex winning rather than set by hand, so it can no longer disagree with what
        /// the aircraft is doing. As a Survival
        /// reflex it cannot be outranked by anything a future release or another plugin
        /// adds, because bands are compared before scores.
        /// </summary>
        private sealed class MissileBreak : IWingReflex
        {
            public string Id => "wingcommand.missile-break";
            public WingReflexBand Band => WingReflexBand.Survival;
            public string BehaviourId => WingBehaviours.MissileBreak;
            public float MinimumSeconds => WingTuning.PanicMinimumSeconds;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent)
            {
                // Below the floor the aircraft is landing or already crashing, and a hard
                // break is the worse of the two outcomes.
                if (s.RadarAlt < WingTuning.PanicFloorAlt) return 0f;

                if (s.MissileWarned) return 0.9f;

                // Hold through a gap in the warning. A missile that is briefly lost and
                // re-acquired is one missile, not two, and handing the controls back in
                // between is how a wingman gets killed by the shot it had already beaten.
                return incumbent && s.SecondsSinceMissileWarning < WingTuning.PanicClearSeconds
                    ? 0.9f
                    : 0f;
            }
        }

        /// <summary>
        /// There is no leader — the player was shot down and is choosing a new seat.
        ///
        /// A formation slot is defined relative to a leader, so without one there is nothing
        /// to fly. This used to be <c>WingRegistry.HoldForTakeover</c>, which walked the wing
        /// applying an <c>OrbitHere</c> order to every member — overwriting each player-issued
        /// directive with no record of what it had been, so the only way back was a blanket
        /// re-order to Formation that flattened the lot. Exactly the mechanism the deck hold
        /// was rewritten to remove, one file away and untouched by that rewrite.
        ///
        /// Because the directive survives, taking a new seat needs no re-order at all: naming
        /// a leader stops this scoring and every member resumes what it was already doing.
        ///
        /// Safety rather than Cohesion: an aircraft with nowhere to form on is a hazard to
        /// itself, and the question outranks any argument about where the slot should be.
        /// </summary>
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

                // An order that goes somewhere definite of its own accord does not need a
                // leader to carry out, and the player gave it before they lost the seat.
                switch (s.Order)
                {
                    case WingOrder.ReturnToBase:
                    case WingOrder.LandHere:
                    case WingOrder.DeliverCargo:
                    case WingOrder.MoveToPoint:
                    case WingOrder.OrbitHere:
                        return 0f;
                    default:
                        return 1f;
                }
            }
        }

        /// <summary>
        /// The leader is on the runway, so every formation slot is on the runway too.
        ///
        /// Replaces the <c>heldOnDeck</c> set, and with it the part that actually hurt: the
        /// old version <i>overwrote the player's directive</i> with an orbit, so the HUD
        /// showed an order nobody had given and the original had to be remembered on the
        /// side. Here the directive is untouched — only the behaviour changes, and it
        /// changes back on its own.
        /// </summary>
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

                // Only a wingman actually trying to hold formation is moved. An explicit
                // order - an attack, a hold somewhere else, an RTB - is the player's and
                // outlives their landing.
                return s.Order == WingOrder.Formation ? 1f : 0f;
            }
        }

        /// <summary>
        /// A hunting wingman that has drifted past its leash.
        ///
        /// Replaces the <c>recalled</c> boolean, and fixes what that boolean cost: the old
        /// recall called <c>SwitchState</c> directly without going through the order system,
        /// so the directive still read <c>Engage</c> while the aircraft flew formation — and
        /// the engagement code, reading that directive, granted it autonomous-combat weapons
        /// authority from the slot with ROE bypassed entirely.
        /// </summary>
        private sealed class LeashRecall : IWingReflex
        {
            public string Id => "wingcommand.leash-recall";
            public WingReflexBand Band => WingReflexBand.Cohesion;
            public string BehaviourId => WingBehaviours.Rejoin;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;

            public float Score(in WingSituation s, bool incumbent)
            {
                if (!s.LeaderPresent || s.LeashRadius <= 0f || s.LeaderDistance < 0f) return 0f;
                if (!WingOrderRules.SendsWingmanHunting(s.Order)) return 0f;

                // Two thresholds, declared rather than tracked: grab control at the leash,
                // give it back only well inside. Asking whether we are the incumbent is what
                // lets a stateless reflex express that.
                float threshold = incumbent
                    ? s.LeashRadius * WingTuning.LeashReleaseFraction
                    : s.LeashRadius;

                if (s.LeaderDistance <= threshold) return 0f;

                // Urgency grows with the overshoot, so a wingman a long way out outranks one
                // that has just crossed the line when both are competing for the same slot.
                float over = (s.LeaderDistance - threshold) / s.LeashRadius;
                return over < 0.02f ? 0.02f : over > 1f ? 1f : over;
            }
        }

        /// <summary>
        /// Fly what the player asked for.
        ///
        /// The floor of the ladder, and the reason resolution is total: it always scores, so
        /// there is always an answer and never a null behaviour. Everything above it is a
        /// temporary reason to do something else.
        /// </summary>
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
