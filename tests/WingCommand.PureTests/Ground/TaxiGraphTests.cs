using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WingCommand.PureTests
{
    public class TaxiGraphTests
    {
        private static bool Near(Vec3 a, Vec3 b, float tolerance = 1f) => (a - b).Length <= tolerance;

        [Fact]
        public void RoadEndsWithinTheMergeRadiusBecomeOneNode()
        {
            TaxiGraph g = TaxiGraph.Build(TestFields.Simple());
            int atCorner = Enumerable.Range(0, g.NodeCount).Count(n => Near(g.NodePos(n), new Vec3(-150f, 0f, 0f)));
            Assert.Equal(1, atCorner);
        }

        [Fact]
        public void HangarExitsHoldShortsAndThresholdsAreConnectedNodes()
        {
            TaxiGraph g = TaxiGraph.Build(TestFields.Simple());
            int exit = g.HangarExit(0);
            Assert.Equal(NodeKind.HangarExit, g.Kind(exit));
            Assert.True(Near(g.NodePos(exit), new Vec3(-260f, 0f, 500f), 1f), $"exit at {g.NodePos(exit)}");
            Assert.NotEmpty(g.EdgesOf(exit));
            int hold = g.HoldShort(0, false);
            Assert.Equal(NodeKind.HoldShort, g.Kind(hold));
            Assert.True(Near(g.NodePos(hold), new Vec3(-40f, 0f, 0f)), $"hold-short at {g.NodePos(hold)} (the runway entry point)");
            int threshold = g.Threshold(0, false);
            Assert.True(Near(g.NodePos(threshold), new Vec3(0f, 0f, 0f)));
            Assert.Contains(g.EdgesOf(hold), e => g.EdgeFrom(e) == threshold || g.EdgeTo(e) == threshold);
            Assert.True(Near(g.NodePos(g.Threshold(0, true)), new Vec3(0f, 0f, 2000f)), "reversable: the far end is a threshold too");
        }

        [Fact]
        public void TheRouteFromAHangarToTheHoldShortFollowsTheTaxiway()
        {
            TaxiGraph g = TaxiGraph.Build(TestFields.Simple());
            var nodes = new List<int>();
            var edges = new List<int>();
            Assert.True(TaxiRouter.Route(g, g.HangarExit(0), g.HoldShort(0, false), null, nodes, edges));
            Vec3[] path = g.PathPoints(nodes, edges);
            Assert.True(Near(path[0], new Vec3(-260f, 0f, 500f)));
            Assert.True(Near(path[path.Length - 1], new Vec3(-40f, 0f, 0f)));
            Assert.Contains(path, p => Near(p, new Vec3(-150f, 0f, 500f)));
            Assert.Contains(path, p => Near(p, new Vec3(-150f, 0f, 0f)));
            float length = 0f;
            for (int i = 1; i < path.Length; i++) length += (path[i] - path[i - 1]).Length;
            Assert.Equal(110f + 500f + 110f, length, 0);
        }

        [Fact]
        public void AFieldWithoutRoadsStillReachesTheHoldShort()
        {
            TaxiGraph g = TaxiGraph.Build(TestFields.Simple(roads: false));
            var nodes = new List<int>();
            var edges = new List<int>();
            Assert.True(TaxiRouter.Route(g, g.HangarExit(1), g.HoldShort(0, true), null, nodes, edges));
        }

        [Fact]
        public void AnExpensiveEdgeIsAvoidedWhenAnotherWayExists()
        {
            var ring = new AirbaseSample
            {
                Name = "ring",
                Roads = new[]
                {
                    new[] { new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f) },
                    new[] { new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 100f) },
                    new[] { new Vec3(100f, 0f, 100f), new Vec3(0f, 0f, 100f) },
                    new[] { new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 0f) },
                },
            };
            TaxiGraph g = TaxiGraph.Build(ring);
            int a = g.NearestNode(new Vec3(0f, 0f, 0f)), c = g.NearestNode(new Vec3(100f, 0f, 100f));
            int ab = Enumerable.Range(0, g.EdgeCount).Single(e =>
                Near(g.NodePos(g.EdgeFrom(e)), new Vec3(0f, 0f, 0f)) && Near(g.NodePos(g.EdgeTo(e)), new Vec3(100f, 0f, 0f)));
            var nodes = new List<int>();
            var edges = new List<int>();
            Assert.True(TaxiRouter.Route(g, a, c, e => e == ab ? 1000f : 0f, nodes, edges));
            Assert.DoesNotContain(ab, edges);
            Assert.Contains(nodes, n => Near(g.NodePos(n), new Vec3(0f, 0f, 100f)));
        }

        [Fact]
        public void EveryRealFreeFlightFieldRoutesFromItsServicePointsToAHoldShort()
        {
            // The Free Flight dump (nomodkit, 2026-09-24): 8 unowned airbases with taxi roads, no hangars.
            string json = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "airbases", "free-flight.json"));
            List<AirbaseSample> fields = AirbaseSample.FromDumpJson(json);
            Assert.Equal(8, fields.Count);
            foreach (AirbaseSample field in fields)
            {
                TaxiGraph g = TaxiGraph.Build(field);
                int hold = g.HoldShort(0, false) >= 0 ? g.HoldShort(0, false) : g.HoldShort(0, true);
                Assert.True(hold >= 0, $"{field.Name}: no hold-short");
                int start = g.NearestNode(field.ServicePoints[0].Pos);
                var nodes = new List<int>();
                var edges = new List<int>();
                Assert.True(TaxiRouter.Route(g, start, hold, null, nodes, edges), $"{field.Name}: no route from the service point");
            }
        }

        [Fact]
        public void NoRouteWhenTheTargetIsUnreachable()
        {
            var split = new AirbaseSample
            {
                Name = "split",
                Roads = new[]
                {
                    new[] { new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f) },
                    new[] { new Vec3(500f, 0f, 0f), new Vec3(600f, 0f, 0f) },
                },
            };
            TaxiGraph g = TaxiGraph.Build(split);
            Assert.False(TaxiRouter.Route(g, g.NearestNode(Vec3.Zero), g.NearestNode(new Vec3(600f, 0f, 0f)), null,
                new List<int>(), new List<int>()));
        }
    }
}
