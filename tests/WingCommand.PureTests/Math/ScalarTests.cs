using Xunit;

namespace WingCommand.PureTests
{
    public class ScalarTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(190f, -170f)]
        [InlineData(-180f, 180f)]
        [InlineData(540f, 180f)]
        [InlineData(-190f, 170f)]
        public void Wrap180MapsIntoHalfOpenRange(float input, float expected)
        {
            Assert.Equal(expected, Scalar.Wrap180(input), 3);
        }

        [Fact]
        public void SmoothStepIsZeroOneAndHalfAtTheMidpoint()
        {
            Assert.Equal(0f, Scalar.SmoothStep(60f, 180f, 10f));
            Assert.Equal(1f, Scalar.SmoothStep(60f, 180f, 500f));
            Assert.Equal(0.5f, Scalar.SmoothStep(60f, 180f, 120f), 4);
        }

        [Fact]
        public void ClampAndLerpBehaveAsExpected()
        {
            Assert.Equal(1f, Scalar.Clamp(3f, -1f, 1f));
            Assert.Equal(0f, Scalar.Clamp01(-2f));
            Assert.Equal(7.5f, Scalar.Lerp(5f, 10f, 0.5f));
            Assert.False(Scalar.IsFinite(float.NaN));
        }
    }
}
