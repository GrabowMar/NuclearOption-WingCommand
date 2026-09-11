using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class FlightRecoveryTests : IDisposable
    {
        public FlightRecoveryTests()
        {
            WingAi.Clear();
            WingReflexes.RegisterDefaults();
        }

        public void Dispose() => WingAi.Clear();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RecordedInvertedDefensiveExitRecoversBeforeLeashRejoin(bool smart)
        {
            var falling = new WingSituation(order: WingOrder.Engage, radarAlt: 104f,
                leaderDistance: 14463f, leashRadius: 5000f, airspeed: 58f,
                secondsSinceMissileWarning: 3f, secondsInBehaviour: 3f)
                .WithFlightSafety(5.93f, -153.2f, -172f, 86.7f, recoveringFromDefence: true);
            var decision = WingArbiter.Resolve(in falling, "wingcommand.missile-break", smart, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.TerrainAbort, decision.BehaviourId);

            // Clearing the terrain probe is not enough while still inverted or below flying speed.
            var clearing = new WingSituation(order: WingOrder.Engage, radarAlt: 400f,
                leaderDistance: 14000f, leashRadius: 5000f, airspeed: 80f, secondsInBehaviour: 2f)
                .WithFlightSafety(0f, 5f, -50f, 86.7f);
            decision = WingArbiter.Resolve(in clearing, "wingcommand.terrain-abort", smart, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.TerrainAbort, decision.BehaviourId);

            var recovered = new WingSituation(order: WingOrder.Engage, radarAlt: 450f,
                leaderDistance: 14000f, leashRadius: 5000f, airspeed: 100f, secondsInBehaviour: 4f)
                .WithFlightSafety(0f, 5f, 15f, 86.7f);
            decision = WingArbiter.Resolve(in recovered, "wingcommand.terrain-abort", smart, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.Rejoin, decision.BehaviourId);
            Assert.Equal(WingOrder.Engage, recovered.Order);
        }

        [Fact]
        public void PredictedImpactPreemptsMissileHoldEvenCloseToTheLeader()
        {
            var falling = new WingSituation(radarAlt: 220f, leaderDistance: 20f,
                missileWarned: true, secondsInBehaviour: 0.1f)
                .WithFlightSafety(0f, -46.8f, 9f, 60f);
            var decision = WingArbiter.Resolve(in falling, "wingcommand.missile-break", false, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.TerrainAbort, decision.BehaviourId);

            var brain = new WingMemberBrain();
            Assert.True(brain.BeginUpdate(0f, false, false, false, 0.625f));
            Assert.True(brain.BeginUpdate(0.1f, false, false,
                TerrainAbortPolicy.ImmediateDanger(220f, -46.8f, 0f), 0.625f));
        }

        [Fact]
        public void ANewMissileCanInterruptNonUrgentRecoveryWithoutWaitingForItsHold()
        {
            var safe = new WingSituation(radarAlt: 1000f, missileWarned: true, secondsInBehaviour: 0.1f)
                .WithFlightSafety(0f, 0f, 20f, 60f);
            var decision = WingArbiter.Resolve(in safe, "wingcommand.terrain-abort", true, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.MissileBreak, decision.BehaviourId);
        }

        [Theory]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.ReturnToBase)]
        [InlineData(WingOrder.DeliverCargo)]
        public void ExplicitLowAltitudeTasksKeepTheirOwnSafetyController(WingOrder order)
        {
            var task = new WingSituation(order: order, radarAlt: 30f)
                .WithFlightSafety(2f, -10f, 0f, 60f, recoveringFromDefence: true);
            Assert.False(TerrainAbortPolicy.ShouldRecover(in task, false));
        }

        [Fact]
        public void RecoveryDoesNotTakeOverNativeDepartureSurfaceUnitsOrIntentionalAerobatics()
        {
            var departure = new WingSituation(deliveryPending: true, radarAlt: 100f)
                .WithFlightSafety(2f, -80f, 170f, 60f, true);
            Assert.False(TerrainAbortPolicy.ShouldRecover(in departure, false));
            var surface = new WingSituation(memberIsSurface: true)
                .WithFlightSafety(2f, -80f, 170f, 60f, true);
            Assert.False(TerrainAbortPolicy.ShouldRecover(in surface, false));
            var maneuver = new WingSituation(order: WingOrder.Maneuver, radarAlt: 2000f)
                .WithFlightSafety(0f, -20f, 170f, 60f);
            Assert.False(TerrainAbortPolicy.ShouldRecover(in maneuver, false));
        }

        [Fact]
        public void ActiveAttackYieldsToImminentTerrainButFlatLowFlightDoesNot()
        {
            var attack = new WingSituation(order: WingOrder.Attack, radarAlt: 220f)
                .WithFlightSafety(1f, -46f, 0f, 60f);
            Assert.True(TerrainAbortPolicy.ShouldRecover(in attack, false));
            var flat = new WingSituation(order: WingOrder.Attack, radarAlt: 40f)
                .WithFlightSafety(0f, 0f, 0f, 60f);
            Assert.False(TerrainAbortPolicy.ShouldRecover(in flat, false));
        }

        [Fact]
        public void FallingBelowApronHeightDoesNotReleaseAnExistingRecovery()
        {
            var falling = new WingSituation(radarAlt: 7f, airspeed: 40f, secondsInBehaviour: 4f)
                .WithFlightSafety(5f, -40f, 80f, 60f);
            Assert.True(TerrainAbortPolicy.ShouldRecover(in falling, true));
            Assert.False(TerrainAbortPolicy.ShouldRecover(in falling, false));
            var decision = WingArbiter.Resolve(in falling, "wingcommand.terrain-abort", false, WingAi.Reflexes);
            Assert.Equal(WingBehaviours.TerrainAbort, decision.BehaviourId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TerrainInterruptionDefersAllClearUntilWarningActuallyClears(bool missileFirst)
        {
            var brain = new WingMemberBrain();
            WingFlightSituation Sample(float now, bool warned, float altitude)
            {
                brain.BeginUpdate(now, warned, false, true, 0f);
                var situation = new WingSituation(missileWarned: warned, radarAlt: altitude,
                    secondsSinceMissileWarning: brain.SecondsSinceWarning(now),
                    secondsInBehaviour: brain.SecondsInBehaviour(now))
                    .WithFlightSafety(altitude < 100f ? 2f : 0f, -10f, 0f, 60f);
                return new WingFlightSituation(in situation, 0f, 100f, 0f, 100f, 0f);
            }

            var telemetry = Sample(0f, true, missileFirst ? 1000f : 40f);
            var first = brain.Evaluate(in telemetry, 0, false, true);
            brain.Commit(in first, 0f);
            telemetry = Sample(0.1f, true, 40f);
            var interrupted = brain.Evaluate(in telemetry, 0, false, true);
            Assert.Equal(WingBehaviours.TerrainAbort, interrupted.Resolution.BehaviourId);
            Assert.Equal(missileFirst, interrupted.LeavesMissileBreak);
            Assert.False(interrupted.DefenceCleared);
            brain.Commit(in interrupted, 0.1f);

            telemetry = Sample(0.3f, false, 40f);
            Assert.False(brain.Evaluate(in telemetry, 0, false, true).DefenceCleared);
            telemetry = Sample(3f, false, 40f);
            var clear = brain.Evaluate(in telemetry, 0, false, true);
            Assert.True(clear.DefenceCleared);
            Assert.Equal(WingBehaviours.TerrainAbort, clear.Resolution.BehaviourId);
            // Speculation does not consume the event; committing it prevents duplicate credit/chatter.
            Assert.True(brain.Evaluate(in telemetry, 0, false, true).DefenceCleared);
            brain.Commit(in clear, 3f);
            Assert.False(brain.Evaluate(in telemetry, 0, false, true).DefenceCleared);
        }
    }
}
