using Xunit;

namespace WingCommand.PureTests
{
    public class LaunchSafetyTests
    {
        [Theory]
        [InlineData(false, false, 8f, 90f, false)] // Speed alone cannot take ownership from taxi.
        [InlineData(true, false, 1f, 90f, false)] // Rotation alone does not clear the runway.
        [InlineData(true, false, 8f, 60f, false)] // Reject speed below the launch margin.
        [InlineData(true, false, 8f, 90f, false)] // Keep protected climb even at flying speed.
        [InlineData(true, false, 74.9f, 90f, false)]
        [InlineData(true, false, 75f, 90f, true)]
        [InlineData(true, true, 4f, 0f, false)]
        public void EarlyHandoffRequiresSafeLiftoff(bool takeoffState, bool rotary,
            float altitude, float speed, bool expected)
        {
            Assert.Equal(expected, LaunchSafety.CanHandOff(false, takeoffState,
                rotary, altitude, speed, 70f));
        }

        [Theory]
        [InlineData(5f)]
        [InlineData(20f)]
        [InlineData(100f)]
        public void RotaryTakeoffAlwaysWaitsForNativeCompletion(float altitude)
        {
            Assert.False(LaunchSafety.CanHandOff(false, true, true, altitude, 100f, 70f));
        }

        [Fact]
        public void NativeCompletionStillAllowsHandoff()
        {
            Assert.False(LaunchSafety.CanHandOff(true, false, false, 8f, 90f, 70f));
            Assert.True(LaunchSafety.CanHandOff(true, false, false, 80f, 70f, 70f));
            Assert.True(LaunchSafety.CanHandOff(true, false, true, 1f, 0f, 0f));
        }
    }
}
