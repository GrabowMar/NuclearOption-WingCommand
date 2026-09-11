using Xunit;

namespace WingCommand.PureTests
{
    public sealed class HoverApproachPolicyTests
    {
        [Theory]
        [InlineData(0f, 0f, true)]
        [InlineData(5.9f, 29.9f, true)]
        [InlineData(6f, 0f, false)]
        [InlineData(0f, 30f, false)]
        [InlineData(100f, 5f, false)]
        [InlineData(1f, 120f, false)]
        public void DescentAndCargoReleaseRequireBothLowSpeedAndCentring(
            float speed, float horizontalError, bool expected)
        {
            Assert.Equal(expected, HoverApproachPolicy.Settled(speed, horizontalError));
        }
    }
}
