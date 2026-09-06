using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingArbitrationTests : IDisposable
    {
        public WingArbitrationTests() { WingAi.Clear(); WingAi.FaultReporter = null; }
        public void Dispose() { WingAi.Clear(); WingAi.FaultReporter = null; }

        [Fact]
        public void RankingIsIndependentOfInputOrderAndScoresNeverBeatSafetyBands()
        {
            var task = new Reflex("a.task", WingReflexBand.Task, 1f);
            var z = new Reflex("z.safety", WingReflexBand.Safety, 0.2f);
            var a = new Reflex("a.safety", WingReflexBand.Safety, 0.2f);
            var s = new WingSituation();
            Assert.Equal(a.Id, WingArbiter.Resolve(in s, null, true, new IWingReflex[] { task, z, a }).ReflexId);
            Assert.Equal(a.Id, WingArbiter.Resolve(in s, null, true, new IWingReflex[] { a, task, z }).ReflexId);
        }

        [Fact]
        public void ANewRouteOrReturnOrderImmediatelyReleasesACohesionMinimumHold()
        {
            WingReflexes.RegisterDefaults();
            foreach (WingOrder order in new[] { WingOrder.ReturnToBase, WingOrder.MoveToPoint, WingOrder.OrbitHere })
            {
                var s = new WingSituation(order: order, leaderDistance: 9000f, leashRadius: 5000f,
                    secondsInBehaviour: 0.1f);
                Assert.Equal(WingBehaviours.Task, WingArbiter.Resolve(in s,
                    "wingcommand.leash-recall", true, WingAi.Reflexes).BehaviourId);
                Assert.Equal(order, s.Order);
            }
        }

        [Fact]
        public void NativeDepartureOwnershipInterruptsAnAirborneMissileHold()
        {
            WingReflexes.RegisterDefaults();
            var s = new WingSituation(deliveryPending: true, missileWarned: true, secondsInBehaviour: 0.1f);
            Assert.Equal(WingBehaviours.Held, WingArbiter.Resolve(in s,
                "wingcommand.missile-break", true, WingAi.Reflexes).BehaviourId);
        }

        [Theory]
        [InlineData(WingOrder.Formation)]
        [InlineData(WingOrder.JamTarget)]
        public void SlotOrdersHoldOverheadWhileTheLeaderIsOnDeckAndResumeTheirPayload(WingOrder order)
        {
            WingReflexes.RegisterDefaults();
            var grounded = new WingSituation(order: order, targetAlive: true, leaderOnDeck: true);
            var result = WingArbiter.Resolve(in grounded, null, true, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.DeckHold, result.BehaviourId);
            Assert.Equal(order, grounded.Order);
            Assert.True(grounded.TargetAlive);
            var airborne = new WingSituation(order: order, targetAlive: true, leaderOnDeck: false);
            Assert.Equal(WingBehaviours.Task,
                WingArbiter.Resolve(in airborne, result.ReflexId, true, WingAi.Reflexes).BehaviourId);
        }

        [Theory]
        [InlineData(WingOrder.ReturnToBase)]
        [InlineData(WingOrder.MoveToPoint)]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.OrbitHere)]
        public void LandingTheLeaderDoesNotInterruptIndependentTasks(WingOrder order)
        {
            WingReflexes.RegisterDefaults();
            var s = new WingSituation(order: order, targetAlive: true, leaderOnDeck: true);
            Assert.Equal(WingBehaviours.Task,
                WingArbiter.Resolve(in s, null, true, WingAi.Reflexes).BehaviourId);
        }

        [Fact]
        public void ExtensionsCanDeclareAnImmediateEmergencyWithoutBuiltInBehaviourNames()
        {
            var active = new Reflex("test.active", WingReflexBand.Survival, 0.5f) { MinimumSeconds = 5f };
            var emergency = new Reflex("test.emergency", WingReflexBand.Survival, 0.9f) { InterruptsMinimumHold = true };
            var s = new WingSituation(secondsInBehaviour: 0.1f);
            Assert.Equal(emergency.Id, WingArbiter.Resolve(in s, active.Id, true,
                new IWingReflex[] { active, emergency }).ReflexId);
        }

        [Fact]
        public void FaultedIncumbentCannotKeepControlsThroughItsMinimumHold()
        {
            var active = new Reflex("test.fault", WingReflexBand.Survival, 1f)
                { MinimumSeconds = 10f, ThrowScore = true };
            var task = new Reflex("test.task", WingReflexBand.Task, 1f);
            var s = new WingSituation(secondsInBehaviour: 0.1f);
            Assert.Equal(task.Id, WingArbiter.Resolve(in s, active.Id, true,
                new IWingReflex[] { active, task }).ReflexId);
        }

        [Fact]
        public void FaultInLifecycleMetadataDisablesOnlyThatExtension()
        {
            int reports = 0;
            WingAi.FaultReporter = (_, _) => reports++;
            var bad = new Reflex("test.bad", WingReflexBand.Survival, 1f) { ThrowLifecycle = true };
            var task = new Reflex("test.task", WingReflexBand.Task, 1f);
            var s = new WingSituation();
            for (int i = 0; i < 3; i++)
                Assert.Equal(task.Id, WingArbiter.Resolve(in s, null, true,
                    new IWingReflex[] { bad, task }).ReflexId);
            Assert.Equal(1, reports);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonFiniteScoreCannotWinOrStick(float score)
        {
            var bad = new Reflex("test.bad", WingReflexBand.Survival, score) { MinimumSeconds = 10f };
            var task = new Reflex("test.task", WingReflexBand.Task, 1f);
            var s = new WingSituation();
            Assert.Equal(task.Id, WingArbiter.Resolve(in s, bad.Id, true,
                new IWingReflex[] { bad, task }).ReflexId);
        }

        [Fact]
        public void AnIdleCombatOrderRegroupsAndResumesWithoutBeingReplaced()
        {
            WingReflexes.RegisterDefaults();
            var active = new WingSituation(order: WingOrder.Attack, targetAlive: true);
            var quiet = active.WithEngagementIdle(WingTuning.EngageIdleSeconds + 1f);
            var result = WingArbiter.Resolve(in quiet, null, true, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.Rejoin, result.BehaviourId);
            Assert.Equal(WingOrder.Attack, quiet.Order);
            Assert.True(quiet.TargetAlive);
            Assert.Equal(WingBehaviours.Task,
                WingArbiter.Resolve(in active, result.ReflexId, true, WingAi.Reflexes).BehaviourId);
        }

        [Fact]
        public void ReplacingBehaviourUnderTheSameReflexIsARealTransition()
        {
            var old = new WingResolution("old", "plugin.reflex", WingReflexBand.Task, 1f);
            var next = new WingResolution("new", "plugin.reflex", WingReflexBand.Task, 1f);
            Assert.False(old.SameAs(in next));
        }

        private sealed class Reflex : IWingReflex, IWingReflexLifecycle
        {
            public string Id { get; }
            public WingReflexBand Band { get; }
            public string BehaviourId => Id;
            public float MinimumSeconds { get; set; }
            public bool RequiresSmartMode => false;
            public bool ThrowScore;
            public bool ThrowLifecycle;
            private bool interrupts;
            public bool InterruptsMinimumHold
            {
                get => ThrowLifecycle ? throw new InvalidOperationException("metadata") : interrupts;
                set => interrupts = value;
            }
            private readonly float score;
            public Reflex(string id, WingReflexBand band, float score) { Id = id; Band = band; this.score = score; }
            public bool CanHold(in WingSituation s) => true;
            public float Score(in WingSituation s, bool incumbent) => ThrowScore ? throw new Exception("score") : score;
        }
    }
}
