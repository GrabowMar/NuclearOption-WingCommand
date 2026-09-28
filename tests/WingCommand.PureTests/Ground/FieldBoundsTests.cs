using Xunit;

namespace WingCommand.PureTests
{
    public class FieldBoundsTests
    {
        private static AirbaseSample Field()
        {
            return new AirbaseSample
            {
                Name = "airbase_island5",
                Center = new Vec3(42400f, 0f, 200f),
                Runways = new[] { new RunwaySample { Start = new Vec3(41800f, 0f, 0f), End = new Vec3(43000f, 0f, 400f) } },
                Roads = new[] { new[] { new Vec3(42300f, 0f, 100f), new Vec3(42500f, 0f, 300f) } },
                Hangars = new[]
                {
                    new HangarSample { Index = 0, Spawn = new Pose(new Vec3(42410f, 0f, 190f), new Vec3(0f, 0f, 1f)) },
                    // In-game 2026-09-28: IBIS spawned here, ~45 km from Opal Airport, and taxied toward it for the rest of the session.
                    new HangarSample { Index = 1, Spawn = new Pose(new Vec3(74703f, 0f, -27849f), new Vec3(0f, 0f, 1f)) },
                    new HangarSample { Index = 2, Spawn = new Pose(new Vec3(42370f, 0f, 150f), new Vec3(0f, 0f, 1f)) },
                },
                ServicePoints = new[] { new Pose(new Vec3(42600f, 0f, 250f), new Vec3(1f, 0f, 0f)), new Pose(new Vec3(-5000f, 0f, 9000f), new Vec3(1f, 0f, 0f)) },
                Pads = new[] { new Pose(new Vec3(90000f, 0f, 0f), new Vec3(1f, 0f, 0f)) },
            };
        }

        [Fact]
        public void AHangarBesideTheRunwayBelongsToTheField() =>
            Assert.True(FieldBounds.Near(Field(), new Vec3(42410f, 0f, 190f)));

        [Fact]
        public void AHangarFortyFiveKilometresAwayIsDroppedAndNamed()
        {
            AirbaseSample f = Field();
            string dropped = FieldBounds.Prune(f);
            Assert.Equal(2, f.Hangars.Length);
            Assert.Equal(0, f.Hangars[0].Index);
            Assert.Equal(2, f.Hangars[1].Index);
            Assert.Contains("hangar 1", dropped);
        }

        [Fact]
        public void FarServicePointsAndPadsGoToo()
        {
            AirbaseSample f = Field();
            FieldBounds.Prune(f);
            Assert.Single(f.ServicePoints);
            Assert.Empty(f.Pads);
        }

        [Fact]
        public void ANearFieldDropsNothing()
        {
            AirbaseSample f = Field();
            f.Hangars = new[] { f.Hangars[0] };
            f.ServicePoints = new[] { f.ServicePoints[0] };
            f.Pads = new Pose[0];
            Assert.Null(FieldBounds.Prune(f));
        }

        [Fact]
        public void AFieldWithNoRunwayOrRoadsMeasuresFromItsCentre()
        {
            var f = new AirbaseSample { Center = new Vec3(1000f, 0f, 1000f) };
            Assert.True(FieldBounds.Near(f, new Vec3(1000f + FieldBounds.Margin - 10f, 0f, 1000f)));
            Assert.False(FieldBounds.Near(f, new Vec3(1000f + FieldBounds.Margin + 10f, 0f, 1000f)));
        }

        [Fact]
        public void AnAirbaseHangarIndexFindsItsSampleOrNone()
        {
            AirbaseSample f = Field();
            FieldBounds.Prune(f);
            Assert.Equal(1, FieldBounds.SampleOf(f, 2));
            Assert.Equal(-1, FieldBounds.SampleOf(f, 1));
        }
    }
}
