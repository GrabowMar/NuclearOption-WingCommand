using Xunit;

namespace WingCommand.PureTests
{
    public sealed class RadarDefenceGeometryTests
    {
        [Theory]
        [InlineData(1000f, 3000f)]
        [InlineData(-600f, 100f)]
        public void NotchIsPerpendicularToTheGuidanceSource(float x, float z)
        {
            var notch = RadarDefenceGeometry.Notch(x, z, 0f, 1f, 100f, 0f, true, 0);
            Assert.InRange(x * notch.x + z * notch.z, -0.001f, 0.001f);
            Assert.InRange(notch.x * notch.x + notch.z * notch.z, 0.9999f, 1.0001f);
        }

        [Fact]
        public void HeadOnThreatChoosesTheNotchTowardsTheWing()
        {
            var notch = RadarDefenceGeometry.Notch(0f, 5000f, 0f, 1f, 1000f, 0f, true, 0);
            Assert.Equal(1f, notch.x);
            var held = RadarDefenceGeometry.Notch(0f, 5000f, 0f, 1f, -1000f, 0f, true, notch.side);
            Assert.Equal(notch.side, held.side);
        }

        [Fact]
        public void EstablishedNotchAndUrgentThreatAvoidReversingTowardLeader()
        {
            var established = RadarDefenceGeometry.Notch(0f, 5000f, -1f, 0f, 1000f, 0f, true, 0);
            Assert.Equal(-1f, established.x);
            var urgent = RadarDefenceGeometry.Notch(0f, 5000f, -0.1f, 0.99f, 1000f, 0f, false, 0);
            Assert.Equal(-1f, urgent.x);
        }

        [Fact]
        public void CoincidentSourcePreservesHeading()
        {
            var notch = RadarDefenceGeometry.Notch(0f, 0f, 0f, 1f, 100f, 0f, true, 0);
            Assert.Equal(0f, notch.x);
            Assert.Equal(1f, notch.z);
        }
    }
}
