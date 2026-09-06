using Xunit;

namespace WingCommand.PureTests
{
    public class LaunchSafetyTests
    {
        [Theory]
        [InlineData(false, false, 8f, 90f, false)] // Taxi still owns a fast aircraft.
        [InlineData(true, false, 1f, 90f, false)] // Rotation is not runway clearance.
        [InlineData(true, false, 8f, 60f, false)] // Below flying-speed margin.
        [InlineData(true, false, 8f, 90f, true)]  // Rejoin before native 75 m gate.
        [InlineData(true, true, 4f, 0f, false)]
        [InlineData(true, true, 5f, 0f, true)]    // Helicopters need no forward speed.
        public void EarlyHandoffRequiresSafeLiftoff(bool takeoffState, bool rotary,
            float altitude, float speed, bool expected)
        {
            Assert.Equal(expected, LaunchSafety.CanHandOff(false, takeoffState,
                rotary, altitude, speed, 70f));
        }

        [Fact]
        public void NativeCompletionStillAllowsHandoff()
        {
            Assert.True(LaunchSafety.CanHandOff(true, false, false, 80f, 70f, 70f));
        }
    }
}
