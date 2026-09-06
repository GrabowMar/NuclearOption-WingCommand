using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingMemberBrainTests : IDisposable
    {
        public WingMemberBrainTests() { WingAi.Clear(); WingReflexes.RegisterDefaults(); }
        public void Dispose() { WingAi.Clear(); WingAi.FaultReporter = null; }

        [Fact]
        public void DepartureHandsOffOnceAndTelemetryUpdatesDoNotRestartCapture()
        {
            var brain = new WingMemberBrain();
            Assert.Equal(WingBehaviours.Held, Step(brain, 0f, pending: true).Resolution.BehaviourId);
            var launch = Step(brain, 10f, distance: 4000f);
            Assert.Equal(WingBehaviours.Task, launch.Resolution.BehaviourId);
            Assert.True(launch.NeedsControlUpdate);
            float capture = brain.Flight.CaptureGain;
            for (int second = 11; second <= 40; second++)
                Assert.False(Step(brain, second, distance: 100f).NeedsControlUpdate);
            Assert.True(brain.Flight.CaptureGain < capture);
            Assert.Equal(30f, brain.SecondsInBehaviour(40f));
        }

        [Fact]
        public void NewOrderDuringDefenceIsPreservedWithoutRestartingTheDefensiveController()
        {
            var brain = new WingMemberBrain();
            Step(brain, 0f);
            Assert.True(Step(brain, 10f, warned: true).NeedsControlUpdate);
            WingDecision retained = Step(brain, 10.1f, WingOrder.ReturnToBase, revision: 1, warned: true);
            Assert.Equal(WingBehaviours.MissileBreak, retained.Resolution.BehaviourId);
            Assert.False(retained.NeedsControlUpdate);
            WingDecision resumed = Step(brain, 14f, WingOrder.ReturnToBase, revision: 1);
            Assert.True(resumed.LeavesMissileBreak);
            Assert.True(resumed.NeedsControlUpdate);
            Assert.Equal(1, resumed.OrderRevision);
            Assert.Equal(WingBehaviours.Task, resumed.Resolution.BehaviourId);
        }

        [Fact]
        public void LostControlAndTaskCompletionBypassPerformanceCadence()
        {
            var brain = new WingMemberBrain();
            Assert.True(brain.BeginUpdate(0f, false, false, false, 1f));
            Assert.False(brain.BeginUpdate(0.1f, false, false, false, 1f));
            Assert.True(brain.BeginUpdate(0.2f, false, true, false, 1f));
            brain.RequestEvaluation();
            Assert.True(brain.BeginUpdate(0.3f, false, false, false, 1f));
            Step(brain, 2f);
            Assert.True(Step(brain, 2.1f, lost: true).NeedsControlUpdate);
            Assert.False(Step(brain, 2.2f).NeedsControlUpdate);
        }

        [Fact]
        public void SpeculativeEvaluationDoesNotResetHoldClockOrReplaceCommittedState()
        {
            var brain = new WingMemberBrain();
            Step(brain, 0f);
            Step(brain, 10f, warned: true);
            var telemetry = Sample(brain, 14f, WingOrder.Maneuver);
            var draft = brain.Evaluate(in telemetry, 1, false, true);
            Assert.True(draft.LeavesMissileBreak);
            Assert.True(brain.Defensive);
            Assert.Equal(4f, brain.SecondsInBehaviour(14f));
            // Lifecycle retires an interrupted maneuver, then resamples Formation.
            WingDecision actual = Step(brain, 14f, WingOrder.Formation, revision: 2);
            Assert.Equal(2, actual.OrderRevision);
            Assert.False(brain.Defensive);
        }

        [Fact]
        public void WingmenShareProvidersButNeverShareWarningOrHoldTimers()
        {
            var first = new WingMemberBrain();
            var second = new WingMemberBrain();
            Step(first, 0f); Step(second, 0f);
            Step(first, 10f, warned: true);
            Assert.False(Step(second, 11f).NeedsControlUpdate);
            Assert.False(second.Defensive);
            Assert.Equal(999f, second.SecondsSinceWarning(11f));
            Assert.Equal(1f, first.SecondsSinceWarning(11f));
            Assert.Equal(11f, second.SecondsInBehaviour(11f));
        }

        [Fact]
        public void NewRouteReleasesRecallAndChangesOnlyTheTaskRevision()
        {
            var brain = new WingMemberBrain();
            Step(brain, 0f, WingOrder.Attack, distance: 9000f);
            Assert.Equal(WingBehaviours.Rejoin, brain.Current.BehaviourId);
            var route = Step(brain, 0.1f, WingOrder.MoveToPoint, revision: 1, distance: 9000f);
            Assert.Equal(WingBehaviours.Task, route.Resolution.BehaviourId);
            Assert.True(route.NeedsControlUpdate);
            Assert.False(Step(brain, 0.2f, WingOrder.MoveToPoint, revision: 1).NeedsControlUpdate);
            Assert.True(Step(brain, 0.3f, WingOrder.MoveToPoint, revision: 2).NeedsControlUpdate);
        }

        [Fact]
        public void UnavailableExtensionCannotHideFallbackOwnershipOrIgnoreLaterOrders()
        {
            WingAi.FaultReporter = (_, _) => { };
            WingAi.Register(new MissingBehaviour());
            var brain = new WingMemberBrain();
            Step(brain, 0f);
            Assert.Equal("test.missing", brain.Current.BehaviourId);
            brain.FallBackToTask(0f);
            Assert.Equal(WingBehaviours.Task, brain.Current.BehaviourId);
            var route = Step(brain, 0.1f, WingOrder.MoveToPoint, revision: 1);
            Assert.True(route.NeedsControlUpdate);
            Assert.Equal(WingBehaviours.Task, route.Resolution.BehaviourId);
            Assert.Equal(1, route.OrderRevision);
        }

        [Fact]
        public void UpdatedUrgencyIsVisibleWithoutRestartingAnUnchangedController()
        {
            WingAi.Register(new DistanceReflex());
            var brain = new WingMemberBrain();
            Step(brain, 0f, distance: 100f);
            Assert.Equal(1f, brain.Current.Score);
            WingDecision next = Step(brain, 3f, distance: 25f);
            Assert.False(next.NeedsControlUpdate);
            Assert.Equal(0.25f, brain.Current.Score);
            Assert.Equal(3f, brain.SecondsInBehaviour(3f));
        }

        [Fact]
        public void RestoredFactoryRevivesItsReflexWithoutLosingTheNewStandingOrder()
        {
            WingAi.Register(new MissingBehaviour());
            var brain = new WingMemberBrain();
            Step(brain, 0f);
            brain.FallBackToTask(0f);
            Step(brain, 1f, WingOrder.MoveToPoint, revision: 1);
            WingAi.RestoreBehaviour("test.missing");
            WingDecision restored = Step(brain, 2f, WingOrder.MoveToPoint, revision: 1);
            Assert.Equal("test.missing", restored.Resolution.BehaviourId);
            Assert.True(restored.NeedsControlUpdate);
            Assert.Equal(1, restored.OrderRevision);
        }

        [Fact]
        public void FailedFactoryWithAnAvailableSuccessorRetriesWithoutSuppression()
        {
            WingAi.Register(new MissingBehaviour());
            var brain = new WingMemberBrain();
            Step(brain, 0f);
            brain.FallBackToTask(0f, rejectUnavailable: false);
            WingDecision retry = Step(brain, 0.1f);
            Assert.Equal("test.missing", retry.Resolution.BehaviourId);
            Assert.True(retry.NeedsControlUpdate);
        }

        private sealed class DistanceReflex : IWingReflex
        {
            public string Id => "test.distance";
            public WingReflexBand Band => WingReflexBand.Cohesion;
            public string BehaviourId => WingBehaviours.Rejoin;
            public float MinimumSeconds => 0f;
            public bool RequiresSmartMode => false;
            public float Score(in WingSituation situation, bool incumbent) => situation.LeaderDistance / 100f;
        }

        private sealed class MissingBehaviour : IWingReflex
        {
            public string Id => "test.missing-reflex";
            public WingReflexBand Band => WingReflexBand.Safety;
            public string BehaviourId => "test.missing";
            public float MinimumSeconds => 10f;
            public bool RequiresSmartMode => false;
            public float Score(in WingSituation situation, bool incumbent) => 1f;
        }

        private static WingDecision Step(WingMemberBrain brain, float now,
            WingOrder order = WingOrder.Formation, int revision = 0, bool warned = false,
            bool pending = false, float distance = 0f, bool lost = false)
        {
            brain.BeginUpdate(now, warned, lost, true, 0f);
            var telemetry = Sample(brain, now, order, warned, pending, distance);
            WingDecision decision = brain.Evaluate(in telemetry, revision, lost, true);
            brain.Commit(in decision, now);
            return decision;
        }

        private static WingFlightSituation Sample(WingMemberBrain brain, float now,
            WingOrder order, bool warned = false, bool pending = false, float distance = 0f)
        {
            var situation = new WingSituation(order: order, deliveryPending: pending,
                missileWarned: warned, secondsSinceMissileWarning: brain.SecondsSinceWarning(now),
                secondsInBehaviour: brain.SecondsInBehaviour(now), leaderDistance: distance, leashRadius: 5000f);
            return new WingFlightSituation(in situation, distance, 300f, 0f, 100f, 0.5f);
        }
    }
}
