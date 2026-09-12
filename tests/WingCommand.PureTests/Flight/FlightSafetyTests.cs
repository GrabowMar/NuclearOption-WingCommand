using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public class FlightSafetyTests
    {
        [Theory]
        [InlineData(0f, false)]
        [InlineData(5f, false)]
        [InlineData(7.9f, false)]
        [InlineData(25f, true)]
        public void TaxiingAircraftAreNotTerrainAborts(float radarAlt, bool expected)
        {
            Assert.Equal(expected, TerrainAbortPolicy.ShouldAbort(
                radarAlt, 600f, WingOrder.Formation, incumbent: false, deliveryPending: false));
        }

        [Theory]
        [InlineData(10f, 5f, false)]
        [InlineData(25f, 15f, false)]
        [InlineData(25f, -2f, true)]
        [InlineData(25f, 0f, true)]
        public void ClimboutAircraftAreNotTerrainAborts(float radarAlt, float verticalSpeed, bool expected)
        {
            Assert.Equal(expected, TerrainAbortPolicy.ShouldAbort(
                radarAlt, 600f, WingOrder.Formation, incumbent: false, deliveryPending: false, verticalSpeed: verticalSpeed));
        }

        [Fact]
        public void TakeoffClimboutDoesNotTriggerTerrainAbortReflex()
        {
            WingAi.Clear();
            WingReflexes.RegisterDefaults();
            try
            {
                var climbout = new WingSituation(order: WingOrder.Formation, missileWarned: false,
                    radarAlt: 25f, leaderDistance: 2000f, leaderPresent: true, airspeed: 85f, secondsInBehaviour: 0.1f)
                    .WithFlightSafety(terrainUrgency: 0f, verticalSpeed: 15f, bankAngle: 0f, minimumAirspeed: 60f);
                var decision = WingArbiter.Resolve(in climbout, "wingcommand.delivery-hold", true, WingAi.Reflexes);
                Assert.Equal(WingBehaviours.Task, decision.BehaviourId);
            }
            finally { WingAi.Clear(); }
        }

        [Fact]
        public void TerrainRecoveryPreemptsMissileHoldButEvasionStillHoldsThroughWarningGaps()
        {
            WingAi.Clear();
            WingReflexes.RegisterDefaults();
            try
            {
                var low = new WingSituation(order: WingOrder.Formation, missileWarned: true,
                    radarAlt: 25f, leaderDistance: 600f, leaderPresent: true, secondsInBehaviour: 0.5f);
                var decision = WingArbiter.Resolve(in low, "wingcommand.missile-break", true, WingAi.Reflexes);
                Assert.Equal(WingBehaviours.TerrainAbort, decision.BehaviourId);

                var high = new WingSituation(order: WingOrder.Formation, missileWarned: false,
                    radarAlt: 500f, leaderDistance: 600f, leaderPresent: true,
                    secondsSinceMissileWarning: 0.2f, secondsInBehaviour: 0.5f);
                decision = WingArbiter.Resolve(in high, "wingcommand.missile-break", true, WingAi.Reflexes);
                Assert.Equal(WingBehaviours.MissileBreak, decision.BehaviourId);

                var recovered = new WingSituation(order: WingOrder.Formation, missileWarned: false,
                    radarAlt: 500f, leaderDistance: 600f, leaderPresent: true,
                    secondsSinceMissileWarning: WingTuning.PanicClearSeconds, secondsInBehaviour: 0.8f);
                decision = WingArbiter.Resolve(in recovered, "wingcommand.missile-break", true, WingAi.Reflexes);
                Assert.Equal(WingBehaviours.Task, decision.BehaviourId);
            }
            finally { WingAi.Clear(); }
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(90f)]
        [InlineData(225f)]
        public void HoldingSlotsTurnTheSameWayOnSeparateCircles(float degrees)
        {
            float x = (float)Math.Cos(degrees * Math.PI / 180) * 2000f;
            float z = (float)Math.Sin(degrees * Math.PI / 180) * 2000f;
            for (int slot = 1; slot <= 6; slot++)
            {
                var aim = OrbitGeometry.AimOffset(x, z, 2000f, slot, 200f);
                Assert.True(x * aim.z - z * aim.x > 0, "Every slot must use the same turn direction.");
                double radialProjection = (x * aim.x + z * aim.z) / 2000d;
                Assert.InRange(radialProjection, 2000 + (slot - 1) * 200 - 0.01,
                                                 2000 + (slot - 1) * 200 + 0.01);
            }
        }
    }
}
