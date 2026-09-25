using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>The field's traffic state in one line, for the in-game ground trace and field dump.</summary>
    public class FieldStateTests
    {
        [Fact]
        public void TheStateNamesTheRunwayFlagsQueueClaimsBlockedEdgesAndObstacles()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            field.Departures.Expect(3, 2);
            field.Departures.Enqueue(3, 0f);
            field.Departures.NativeLandingPending = true;
            int node = field.Graph.HangarExit(0);
            field.Reservations.TryAdvance(3, TaxiPriority.Departing, new[] { node }, new int[0], 0, 0);
            field.Reservations.Block(0, true);
            field.Obstacles.Add(new Vec3(-120f, 0f, 400f));
            string state = field.Describe();
            Assert.Contains("native=True", state);
            Assert.Contains("queue=[3]", state);
            Assert.Contains($"n{node}:3", state);
            Assert.Contains("blocked=[e0]", state);
            Assert.Contains("(-120,400)", state);
        }
    }
}
