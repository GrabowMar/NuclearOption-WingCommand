using Xunit;

namespace WingCommand.PureTests
{
    public class AirStartTests
    {
        private static readonly Vec3 Leader = new Vec3(1000f, 3000f, 5000f);
        private static readonly Vec3 North = new Vec3(0f, 0f, 200f);

        [Fact]
        public void RotaryAirStartIsOneKilometreBehindAndFiftyMetresAboveOnTheSlotSide()
        {
            var leader = new Vec3(0f, 800f, 0f);
            Vec3 p = AirStart.RotaryPosition(leader, new Vec3(0f, 0f, 60f), -1f, 0, 0f);
            Assert.Equal(-1000f, p.Z, 1);
            Assert.Equal(850f, p.Y, 1);
            Assert.True(p.X < 0f, "left slot spawns left");
        }

        [Fact]
        public void RotaryAirStartKeepsItsTerrainClearance()
        {
            Vec3 p = AirStart.RotaryPosition(new Vec3(0f, 120f, 0f), new Vec3(0f, 0f, 30f), 1f, 0, 100f);
            Assert.Equal(100f + AirStart.RotaryTerrainClearanceM, p.Y, 1);
        }

        [Fact]
        public void RotaryAirStartFliesLevelAtTheLeadersSpeedUpToItsCruise()
        {
            Assert.Equal(new Vec3(0f, 0f, 64f), AirStart.RotaryVelocity(new Vec3(0f, 20f, 200f), 64f));
            Assert.Equal(new Vec3(30f, 0f, 0f), AirStart.RotaryVelocity(new Vec3(30f, -5f, 0f), 64f));
            Assert.Equal(Vec3.Zero, AirStart.RotaryVelocity(Vec3.Zero, 64f));
        }

        [Fact]
        public void TwoKilometresBehindAndBelowOnTheSlotSide()
        {
            Vec3 p = AirStart.Position(Leader, North, 80f, 0, 0f);
            Assert.Equal(5000f - 2000f, p.Z, 1);
            Assert.Equal(3000f - 150f, p.Y, 1);
            Assert.True(p.X > Leader.X + 100f);
            Assert.True(AirStart.Position(Leader, North, -80f, 0, 0f).X < Leader.X - 100f);
        }

        [Fact]
        public void LaterMembersOnTheSameSideSpreadFurtherOut()
        {
            Vec3 a = AirStart.Position(Leader, North, 80f, 0, 0f);
            Vec3 b = AirStart.Position(Leader, North, 160f, 1, 0f);
            Assert.True(b.X - a.X >= AirStart.LateralStepM - 0.01f);
        }

        [Fact]
        public void SlowLeaderIsMatchedAtASafeAirStartSpeedAlongItsPath()
        {
            // In game CI-22s were air-started at the leader's ~50 m/s climb speed, just above the stall.
            var climbing = new Vec3(0f, 5f, 50f);
            Vec3 v = AirStart.Velocity(climbing, 46.9f);
            Assert.Equal(AirStart.MinSpeedFactor * 46.9f, v.Length, 2);
            Assert.Equal(0f, (v.Normalized - climbing.Normalized).Length, 3);
        }

        [Fact]
        public void FastLeaderVelocityIsKept() =>
            Assert.Equal(North, AirStart.Velocity(North, 46.9f));

        [Fact]
        public void StationaryLeaderStartsForwardAtTheFloor() =>
            Assert.Equal(Vec3.Forward * (AirStart.MinSpeedFactor * 60f), AirStart.Velocity(Vec3.Zero, 60f));

        [Fact]
        public void RaisedThreeHundredMetresAboveTheTerrain() =>
            Assert.Equal(2900f + AirStart.TerrainClearanceM, AirStart.Position(Leader, North, 0f, 0, 2900f).Y, 1);

        [Fact]
        public void EverySlotOfEveryShapeGetsItsOwnSpawnPoint()
        {
            // Successive calls spawn later slots: each slot must land clear of every earlier one, even when they
            // share a side.
            var errors = new System.Collections.Generic.List<string>();
            var all = FormationCatalog.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "formations.json")), errors);
            foreach (FormationDefinition def in all)
            {
                var points = new Vec3[FormationCatalog.MaxSlots];
                for (int slot = 0; slot < points.Length; slot++)
                {
                    points[slot] = AirStart.ForSlot(def, slot, Leader, North, 0f);
                    for (int earlier = 0; earlier < slot; earlier++)
                        Assert.True((points[slot] - points[earlier]).Length >= AirStart.LateralStepM - 0.01f,
                            $"{def.Id}: slots {earlier} and {slot} spawn {(points[slot] - points[earlier]).Length:0} m apart");
                }
            }
        }

        [Fact]
        public void SpawnFacesTheLeadersFlightPath()
        {
            Vec3 climbing = new Vec3(0f, 52f, 193f);
            Assert.True((AirStart.Direction(climbing) - climbing.Normalized).Length < 1e-5f);
            Assert.Equal(Vec3.Forward, AirStart.Direction(Vec3.Zero));
        }

        [Fact]
        public void HeadingFollowsTheLeaderOrNorthWhenHovering()
        {
            Assert.Equal(new Vec3(1f, 0f, 0f), AirStart.Heading(new Vec3(150f, 20f, 0f)));
            Assert.Equal(Vec3.Forward, AirStart.Heading(Vec3.Zero));
        }
    }
}
