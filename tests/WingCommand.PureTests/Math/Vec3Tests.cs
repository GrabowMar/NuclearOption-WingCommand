using Xunit;

namespace WingCommand.PureTests
{
    public class Vec3Tests
    {
        [Fact]
        public void CrossFollowsUnityLeftHandedAxes()
        {
            // Unity: x right, y up, z forward. Cross(up, forward) points right.
            Vec3 right = Vec3.Cross(Vec3.Up, Vec3.Forward);
            Assert.Equal(1f, right.X, 5);
            Assert.Equal(0f, right.Y, 5);
            Assert.Equal(0f, right.Z, 5);
        }

        [Fact]
        public void NormalizedOfZeroIsZeroNotNaN()
        {
            Vec3 n = Vec3.Zero.Normalized;
            Assert.Equal(0f, n.Length);
        }

        [Fact]
        public void HeadingIsClockwiseFromNorthInDegrees()
        {
            Assert.Equal(0f, Vec3.HeadingDeg(new Vec3(0f, 0f, 10f)), 4);
            Assert.Equal(90f, Vec3.HeadingDeg(new Vec3(10f, 0f, 0f)), 4);
            Assert.Equal(180f, Vec3.HeadingDeg(new Vec3(0f, 0f, -10f)), 4);
            Assert.Equal(270f, Vec3.HeadingDeg(new Vec3(-10f, 5f, 0f)), 4);
        }

        [Fact]
        public void FromHeadingRoundTripsThroughHeading()
        {
            Vec3 v = Vec3.FromHeading(135f, 2f);
            Assert.Equal(135f, Vec3.HeadingDeg(v), 3);
            Assert.Equal(2f, v.Length, 4);
            Assert.Equal(0f, v.Y);
        }
    }

    public class IsaTests
    {
        [Theory]
        [InlineData(0f, 1.225f)]
        [InlineData(1000f, 1.1117f)]
        [InlineData(5000f, 0.7364f)]
        [InlineData(11000f, 0.3639f)]
        public void DensityMatchesStandardAtmosphereTable(float altitude, float expected)
        {
            Assert.Equal(expected, Isa.Density(altitude), 3);
        }

        [Fact]
        public void DensityAboveTropopauseDecaysAndNeverGoesNegative()
        {
            float d15 = Isa.Density(15000f);
            Assert.InRange(d15, 0.19f, 0.2f);
            Assert.True(Isa.Density(40000f) > 0f);
        }

        [Fact]
        public void DynamicPressureIsHalfRhoVSquared()
        {
            Assert.Equal(0.5f * 1.225f * 200f * 200f, Isa.DynamicPressure(0f, 200f), 1);
        }
    }
}
