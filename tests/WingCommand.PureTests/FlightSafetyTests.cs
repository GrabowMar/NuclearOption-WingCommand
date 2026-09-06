using System;
using Xunit;

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public class FlightSafetyTests
    {
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
                    secondsSinceMissileWarning: 10f, secondsInBehaviour: 0.5f);
                decision = WingArbiter.Resolve(in high, "wingcommand.missile-break", true, WingAi.Reflexes);
                Assert.Equal(WingBehaviours.MissileBreak, decision.BehaviourId);

                var recovered = new WingSituation(order: WingOrder.Formation, missileWarned: false,
                    radarAlt: 500f, leaderDistance: 600f, leaderPresent: true,
                    secondsSinceMissileWarning: 10f, secondsInBehaviour: 3f);
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
