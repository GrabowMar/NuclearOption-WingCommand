namespace WingCommand.PureTests
{
    /// <summary>Synthetic airfields for ground tests. <see cref="Simple"/>: runway 0 along +Z from z = 0 to 2000 at x = 0;
    /// a parallel taxiway at x = −150 split at the apron junctions (z = 500, 700); connectors to the runway entry points
    /// at both ends (x = −40); two hangars at x = −300 facing +X, joined to the taxiway by apron roads.</summary>
    internal static class TestFields
    {
        /// <summary><see cref="Simple"/> with a second way between the apron junction at z = 500 and the south end of the
        /// taxiway: a bypass through (−220, 250).</summary>
        public static AirbaseSample WithBypass()
        {
            AirbaseSample field = Simple();
            var roads = new System.Collections.Generic.List<Vec3[]>(field.Roads)
            {
                new[] { new Vec3(-150f, 0f, 500f), new Vec3(-220f, 0f, 250f), new Vec3(-150f, 0f, 0f) },
            };
            field.Roads = roads.ToArray();
            return field;
        }

        /// <summary><see cref="Simple"/> with a service point beside the taxiway at z = 300, 200 m from any junction.</summary>
        public static AirbaseSample WithServicePoint()
        {
            AirbaseSample field = Simple();
            field.ServicePoints = new[] { new Pose(new Vec3(-200f, 0f, 300f), new Vec3(1f, 0f, 0f)) };
            return field;
        }

        /// <summary><see cref="WithServicePoint"/> with a runway exit on the centreline at z = 1000 (as the game marks
        /// them), joined to the parallel taxiway.</summary>
        public static AirbaseSample WithServicePointAndExit()
        {
            AirbaseSample field = WithServicePoint();
            field.Runways[0].Exits = new[] { new Pose(new Vec3(0f, 0f, 1000f), new Vec3(0f, 0f, 1f)) };
            return field;
        }

        public static AirbaseSample Simple(float runwayWidth = 60f, bool roads = true) => new AirbaseSample
        {
            Name = "test_field",
            Center = new Vec3(-150f, 0f, 1000f),
            Radius = 3000f,
            Runways = new[]
            {
                new RunwaySample
                {
                    Index = 0, Start = new Vec3(0f, 0f, 0f), End = new Vec3(0f, 0f, 2000f), Width = runwayWidth, Length = 2000f,
                    Reversable = true, Takeoff = true, Landing = true,
                    Entries = new[]
                    {
                        new Pose(new Vec3(-40f, 0f, 0f), new Vec3(1f, 0f, 0f)),
                        new Pose(new Vec3(-40f, 0f, 2000f), new Vec3(1f, 0f, 0f)),
                    },
                },
            },
            Roads = roads
                ? new[]
                {
                    new[] { new Vec3(-150f, 0f, 0f), new Vec3(-150f, 0f, 500f) },
                    new[] { new Vec3(-150f, 0f, 500f), new Vec3(-150f, 0f, 700f) },
                    new[] { new Vec3(-150f, 0f, 700f), new Vec3(-150f, 0f, 2000f) },
                    new[] { new Vec3(-150f, 0f, 0f), new Vec3(-40f, 0f, 0f) },
                    new[] { new Vec3(-150f, 0f, 2000f), new Vec3(-40f, 0f, 2000f) },
                    new[] { new Vec3(-260f, 0f, 500f), new Vec3(-150f, 0f, 500f) },
                    new[] { new Vec3(-260f, 0f, 700f), new Vec3(-150f, 0f, 700f) },
                }
                : new Vec3[0][],
            Hangars = new[]
            {
                new HangarSample { Index = 0, Spawn = new Pose(new Vec3(-300f, 0f, 500f), new Vec3(1f, 0f, 0f)), Available = true,
                    Types = new[] { "FS-20 Vortex" } },
                new HangarSample { Index = 1, Spawn = new Pose(new Vec3(-300f, 0f, 700f), new Vec3(1f, 0f, 0f)), Available = true,
                    Types = new[] { "FS-20 Vortex" } },
            },
        };
    }
}
