using System;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The five behaviours the mod ships, exercised through the real registry rather than
    /// through fakes. These are the cases that used to be spread across four boolean guards
    /// in two files and could only be checked by flying a mission.
    /// </summary>
    [Collection("WingAi")]
    public class WingReflexTests : IDisposable
    {
        private const float Leash = WingTuning.LeashRadius;

        public WingReflexTests()
        {
            WingAi.Clear();
            WingReflexes.RegisterDefaults();
        }

        public void Dispose() => WingAi.Clear();

        private static string Behaviour(WingSituation situation, string active = null,
                                        bool smart = true) =>
            WingArbiter.Resolve(in situation, active, smart, WingAi.Reflexes).BehaviourId;

        private static string ReflexOf(WingSituation situation, string active = null) =>
            WingArbiter.Resolve(in situation, active, true, WingAi.Reflexes).ReflexId;

        // ------------------------------------------------------------------- the default

        [Fact]
        public void AnUntroubledWingmanFliesItsOrder()
        {
            Assert.Equal(WingBehaviours.Task, Behaviour(new WingSituation()));
        }

        [Fact]
        public void EveryOrderStillResolvesToSomething()
        {
            foreach (WingOrder order in Enum.GetValues(typeof(WingOrder)))
            {
                string behaviour = Behaviour(new WingSituation(order: order));
                Assert.False(string.IsNullOrEmpty(behaviour));
            }
        }

        // ------------------------------------------------------------------ missile break

        [Fact]
        public void AMissileWarningTakesTheControls()
        {
            Assert.Equal(WingBehaviours.MissileBreak,
                         Behaviour(new WingSituation(missileWarned: true)));
        }

        [Fact]
        public void AMissileWarningOutranksEverySlowerConcern()
        {
            // On the deck, past the leash, and being shot at. Only one of those matters.
            WingSituation s = new WingSituation(
                order: WingOrder.Attack,
                missileWarned: true,
                leaderOnDeck: true,
                leaderDistance: Leash * 3f,
                leashRadius: Leash);

            Assert.Equal(WingBehaviours.MissileBreak, Behaviour(s));
        }

        [Fact]
        public void PerformanceModeStillBreaksForMissiles()
        {
            Assert.Equal(WingBehaviours.MissileBreak,
                         Behaviour(new WingSituation(missileWarned: true), smart: false));
        }

        [Fact]
        public void NoBreakOnTheDeckWhereTheBreakIsTheMoreDangerousOption()
        {
            WingSituation s = new WingSituation(missileWarned: true, radarAlt: 2f);
            Assert.Equal(WingBehaviours.Task, Behaviour(s));
        }

        [Fact]
        public void TheBreakHoldsThroughAGapInTheWarning()
        {
            // The missile is briefly lost. Handing the controls back mid-turn is how a
            // wingman gets killed by a shot it had already beaten.
            WingSituation blink = new WingSituation(
                missileWarned: false,
                secondsSinceMissileWarning: WingTuning.PanicClearSeconds * 0.5f);

            Assert.Equal(WingBehaviours.MissileBreak,
                         Behaviour(blink, active: "wingcommand.missile-break"));
        }

        [Fact]
        public void TheBreakReleasesOnceTheWarningHasStayedClear()
        {
            WingSituation clear = new WingSituation(
                missileWarned: false,
                secondsSinceMissileWarning: WingTuning.PanicClearSeconds + 0.1f,
                secondsInBehaviour: WingTuning.PanicMinimumSeconds + 0.1f);

            Assert.Equal(WingBehaviours.Task,
                         Behaviour(clear, active: "wingcommand.missile-break"));
        }

        [Fact]
        public void TheBreakKeepsControlForItsMinimumEvenWithNothingLeftToRunFrom()
        {
            WingSituation clear = new WingSituation(
                missileWarned: false,
                secondsSinceMissileWarning: 99f,
                secondsInBehaviour: WingTuning.PanicMinimumSeconds * 0.5f);

            Assert.Equal(WingBehaviours.MissileBreak,
                         Behaviour(clear, active: "wingcommand.missile-break"));
        }

        // ---------------------------------------------------------------------- delivery

        [Fact]
        public void ADeliveryUnderTheStockLaunchAiIsNotFlownByUsAtAll()
        {
            Assert.Equal(WingBehaviours.Held, Behaviour(new WingSituation(deliveryPending: true)));
        }

        [Fact]
        public void ADeliveryIsNotCommandeeredEvenToDodge()
        {
            // Both are Survival reflexes. A parked aircraft made to break would leave the
            // apron, so the lockout has to win - and it does so by score, not by luck.
            WingSituation s = new WingSituation(deliveryPending: true, missileWarned: true);
            Assert.Equal(WingBehaviours.Held, Behaviour(s));
        }

        // -------------------------------------------------------------- leader on deck

        [Fact]
        public void TheWingHoldsOverheadWhileTheLeaderIsOnTheRunway()
        {
            WingSituation s = new WingSituation(order: WingOrder.Formation, leaderOnDeck: true);
            Assert.Equal(WingBehaviours.DeckHold, Behaviour(s));
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.OrbitHere)]
        [InlineData(WingOrder.ReturnToBase)]
        [InlineData(WingOrder.MoveToPoint)]
        public void AnExplicitOrderOutlivesTheLeadersLanding(WingOrder order)
        {
            WingSituation s = new WingSituation(order: order, leaderOnDeck: true,
                                                targetAlive: true);
            Assert.Equal(WingBehaviours.Task, Behaviour(s));
        }

        // ------------------------------------------------------------------ leader gone

        [Fact]
        public void WithNoLeaderTheWingHoldsOverheadRatherThanFlyingASlotOnNobody()
        {
            WingSituation s = new WingSituation(leaderPresent: false);
            Assert.Equal("wingcommand.leader-lost", ReflexOf(s));
            Assert.Equal(WingBehaviours.DeckHold, Behaviour(s));
        }

        [Fact]
        public void TheDeckHoldDefersToTheLeaderLostReflexWhenThereIsNoLeaderAtAll()
        {
            // Both want the same behaviour; the point is which reason is recorded, and that
            // "on the runway" cannot be the answer when there is no runway and no leader.
            WingSituation s = new WingSituation(leaderOnDeck: true, leaderPresent: false);
            Assert.Equal("wingcommand.leader-lost", ReflexOf(s));
        }

        [Theory]
        [InlineData(WingOrder.ReturnToBase)]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.MoveToPoint)]
        [InlineData(WingOrder.DeliverCargo)]
        [InlineData(WingOrder.OrbitHere)]
        public void AnOrderThatNeedsNoLeaderIsFlownWithoutOne(WingOrder order)
        {
            // The player gave these before they lost the seat, and none of them is defined
            // relative to a leader. Holding overhead instead would strand a wingman that was
            // already on its way home.
            WingSituation s = new WingSituation(order: order, leaderPresent: false);
            Assert.Equal(WingBehaviours.Task, Behaviour(s));
        }

        [Fact]
        public void AMissileStillOutranksHavingNoLeader()
        {
            WingSituation s = new WingSituation(leaderPresent: false, missileWarned: true);
            Assert.Equal(WingBehaviours.MissileBreak, Behaviour(s));
        }

        // -------------------------------------------------------------------- the leash

        [Fact]
        public void AHuntingWingmanPastTheLeashIsRecalled()
        {
            WingSituation s = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * 1.5f, leashRadius: Leash);

            Assert.Equal(WingBehaviours.Rejoin, Behaviour(s));
        }

        [Fact]
        public void InsideTheLeashItKeepsHunting()
        {
            WingSituation s = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * 0.9f, leashRadius: Leash);

            Assert.Equal(WingBehaviours.Task, Behaviour(s));
        }

        [Fact]
        public void ARecalledWingmanIsNotTurnedLooseTheInstantItCrossesBackIn()
        {
            // The whole point of the two thresholds. At 0.9 of the leash a wingman on its way
            // home is inside the boundary but nowhere near the wing; releasing here is what
            // made it flip between hunting and rejoining every frame.
            WingSituation s = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * 0.9f, leashRadius: Leash);

            Assert.Equal(WingBehaviours.Rejoin, Behaviour(s, active: "wingcommand.leash-recall"));
        }

        [Fact]
        public void ARecalledWingmanIsTurnedLooseOnceItIsGenuinelyBack()
        {
            WingSituation s = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * (WingTuning.LeashReleaseFraction - 0.05f),
                leashRadius: Leash);

            Assert.Equal(WingBehaviours.Task, Behaviour(s, active: "wingcommand.leash-recall"));
        }

        [Fact]
        public void OrdersFlownFromTheSlotAreNeverLeashed()
        {
            // Splash 'Em and Jam carry a target but never leave formation, so a leash on
            // them could only ever fire on a formation that had drifted - not on a chase.
            foreach (WingOrder order in new[] { WingOrder.FireForEffect, WingOrder.JamTarget })
            {
                WingSituation s = new WingSituation(
                    order: order, targetAlive: true,
                    leaderDistance: Leash * 5f, leashRadius: Leash);

                Assert.Equal(WingBehaviours.Task, Behaviour(s));
            }
        }

        [Fact]
        public void ARecallGetsMoreUrgentTheFurtherOutItIs()
        {
            WingSituation near = new WingSituation(
                order: WingOrder.Attack, leaderDistance: Leash * 1.1f, leashRadius: Leash);
            WingSituation far = new WingSituation(
                order: WingOrder.Attack, leaderDistance: Leash * 1.9f, leashRadius: Leash);

            float nearScore = WingArbiter.Resolve(in near, null, true, WingAi.Reflexes).Score;
            float farScore = WingArbiter.Resolve(in far, null, true, WingAi.Reflexes).Score;

            Assert.True(farScore > nearScore);
        }

        // ---------------------------------------------------------------- the whole ladder

        [Fact]
        public void TheLadderRunsInTheOrderItIsDocumentedIn()
        {
            // One situation that satisfies every reflex at once, peeled back a layer at a
            // time. This is the precedence table, asserted.
            WingSituation all = new WingSituation(
                order: WingOrder.Formation,
                deliveryPending: true,
                missileWarned: true,
                leaderOnDeck: true,
                leaderDistance: Leash * 2f,
                leashRadius: Leash);

            Assert.Equal("wingcommand.delivery-hold", ReflexOf(all));

            WingSituation noDelivery = new WingSituation(
                order: WingOrder.Formation, missileWarned: true, leaderOnDeck: true,
                leaderDistance: Leash * 2f, leashRadius: Leash);
            Assert.Equal("wingcommand.missile-break", ReflexOf(noDelivery));

            WingSituation noMissile = new WingSituation(
                order: WingOrder.Formation, leaderOnDeck: true,
                leaderDistance: Leash * 2f, leashRadius: Leash);
            Assert.Equal("wingcommand.deck-hold", ReflexOf(noMissile));

            WingSituation flying = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * 2f, leashRadius: Leash);
            Assert.Equal("wingcommand.leash-recall", ReflexOf(flying));

            WingSituation calm = new WingSituation(order: WingOrder.Attack, targetAlive: true);
            Assert.Equal("wingcommand.standing-task", ReflexOf(calm));
        }

        [Fact]
        public void APluginCanReplaceOneOfOursWithoutTouchingTheRest()
        {
            WingAi.Register(new NeverRecall());

            WingSituation s = new WingSituation(
                order: WingOrder.Attack, targetAlive: true,
                leaderDistance: Leash * 5f, leashRadius: Leash);

            Assert.Equal(WingBehaviours.Task, Behaviour(s));

            // ...and the bands above it still work.
            Assert.Equal(WingBehaviours.MissileBreak,
                         Behaviour(new WingSituation(missileWarned: true)));
        }

        /// <summary>A third-party override of the mod's own leash, registered under its id.</summary>
        private sealed class NeverRecall : IWingReflex
        {
            public string Id => "wingcommand.leash-recall";
            public WingReflexBand Band => WingReflexBand.Cohesion;
            public string BehaviourId => WingBehaviours.Rejoin;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;
            public float Score(in WingSituation s, bool incumbent) => 0f;
        }
    }
}
