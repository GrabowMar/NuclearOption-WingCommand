using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class FormationScenarioTests
    {
        private const float Dt = SimWing.Dt;

        [Fact]
        public void FourShipJoinsFromFourKilometresAsternInTurnWithoutConflicts()
        {
            // J1: leader 4 km ahead; wingmen 60 m/s slower, 150 m low, spread by slot side; afterburner allowed.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 4000f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-150f, 1850f, 0f), new Vec3(150f, 1850f, 0f), new Vec3(300f, 1850f, -150f) }, 140f, 0f);
            var captured = new[] { float.NaN, float.NaN, float.NaN };
            var inside = new float[3];
            float maxAhead = float.NegativeInfinity, minSeparation = float.MaxValue;
            for (int i = 0; i < 200 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                {
                    Vec3 e = wing.Plants[k].Position - wing.Wing.Frame.Slots[k].Ref.Pos;
                    maxAhead = Math.Max(maxAhead, e.Z);
                    inside[k] = e.Length < 0.25f * FormationCatalog.Standard ? inside[k] + Dt : 0f;
                    if (float.IsNaN(captured[k]) && inside[k] >= 5f) captured[k] = wing.Time;
                }
            }
            for (int k = 0; k < 3; k++)
                Assert.True(captured[k] < 120f + 15f * k, $"member {k + 1} captured at {captured[k]:0} s");
            Assert.True(maxAhead < FormationCatalog.Standard, $"passed {maxAhead:0} m ahead of a slot");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void OppositeHeadingJoinConvergesWithoutConflicts()
        {
            // J2: the wing starts 6 km ahead of the leader, flying toward it.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-200f, 2000f, 6000f), new Vec3(200f, 2000f, 6000f), new Vec3(400f, 2000f, 6200f) }, 200f, 180f);
            var captured = new[] { float.NaN, float.NaN, float.NaN };
            var inside = new float[3];
            float minSeparation = float.MaxValue;
            for (int i = 0; i < 300 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                {
                    inside[k] = wing.SlotError(k) < 0.25f * FormationCatalog.Standard ? inside[k] + Dt : 0f;
                    if (float.IsNaN(captured[k]) && inside[k] >= 5f) captured[k] = wing.Time;
                }
            }
            for (int k = 0; k < 3; k++) Assert.False(float.IsNaN(captured[k]), $"member {k + 1} never captured");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }
    }
}
