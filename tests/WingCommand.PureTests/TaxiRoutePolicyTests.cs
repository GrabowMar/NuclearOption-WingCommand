using Xunit;

namespace WingCommand.PureTests
{
    public class TaxiRoutePolicyTests
    {
        [Theory]
        [InlineData(5f, 3f)]
        [InlineData(25f, 7.5f)]
        [InlineData(50f, 10f)]
        public void HeavyAirframesGetBoundedCornerRoundingWithoutSkippingTheApproach(float size, float radius)
        {
            Assert.Equal(radius, TaxiRoutePolicy.CornerRadius(size), 4);
            Assert.False(TaxiRoutePolicy.CanAdvance(0f, 20f, 100f, 0f, radius));
            Assert.False(TaxiRoutePolicy.CanAdvance(-30f, radius + 1f, 100f, 0f, radius));
            Assert.True(TaxiRoutePolicy.CanAdvance(0f, radius - 0.1f, 100f, 0f, radius));
            Assert.Equal(3.5f, TaxiRoutePolicy.SpeedLimit(radius * 2f, 90f, 0f, radius));
        }

        [Theory]
        [InlineData(0f, 20f)]
        [InlineData(0f, 60f)]
        [InlineData(-8f, 8f)]
        public void CornerCannotBeSkippedFromTheApproachTaxiway(float x, float z)
        {
            Assert.False(TaxiRoutePolicy.CanAdvance(x, z, 100f, 0f));
        }

        [Fact]
        public void PassedWaypointRequiresJoiningItsOutgoingSegment()
        {
            Assert.True(TaxiRoutePolicy.CanAdvance(-10f, 2f, 100f, 0f));
            Assert.False(TaxiRoutePolicy.CanAdvance(-10f, 12f, 100f, 0f));
            Assert.True(TaxiRoutePolicy.CanAdvance(0f, 2.9f, 100f, 0f));
            Assert.False(TaxiRoutePolicy.CanAdvance(0f, 4f, 0f, 0f));
        }

        [Fact]
        public void HeavyAircraftBrakeBeforeSharpTaxiwayTurns()
        {
            float far = TaxiRoutePolicy.SpeedLimit(100f, 90f, 0f);
            float approach = TaxiRoutePolicy.SpeedLimit(20f, 90f, 0f);
            float corner = TaxiRoutePolicy.SpeedLimit(4f, 90f, 0f);
            Assert.Equal(12f, far);
            Assert.InRange(approach, corner + 1f, far - 1f);
            Assert.Equal(3.5f, corner);
            Assert.Equal(3.5f, TaxiRoutePolicy.SpeedLimit(100f, 0f, 90f));
        }

        [Theory]
        [InlineData(true, false, true, 30f, 20f, 10f, false)] // Runway/obstacle wait.
        [InlineData(false, true, false, 0f, 20f, 10f, false)] // Valid route.
        [InlineData(false, false, true, 10f, 5f, 10f, false)] // Startup.
        [InlineData(false, false, true, 10f, 20f, 1f, false)] // Rebuild cooldown.
        [InlineData(false, false, false, 0f, 20f, 10f, true)] // Missing route.
        [InlineData(false, true, true, 0f, 20f, 10f, true)] // Left the pavement.
        [InlineData(false, true, false, 9f, 20f, 10f, true)] // Native stuck timer.
        public void RouteRecoveryPreservesTrafficWaitsAndDoesNotThrash(bool waiting,
            bool hasRoute, bool offNetwork, float stopped, float elapsed, float cooldown, bool expected)
        {
            Assert.Equal(expected, TaxiRoutePolicy.ShouldRebuild(waiting, hasRoute,
                offNetwork, stopped, elapsed, cooldown));
        }

        [Theory]
        [InlineData(8f)]
        [InlineData(12f)]
        [InlineData(20f)]
        public void RecoveryNeverClearsTheNativeRunwayArrivalWaypoint(float distance)
        {
            Assert.False(TaxiRoutePolicy.ShouldRebuild(false, true, true,
                20f, 120f, 20f, distance));
        }
    }
}
