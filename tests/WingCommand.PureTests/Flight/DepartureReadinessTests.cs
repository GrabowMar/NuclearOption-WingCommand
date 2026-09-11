using Xunit;

namespace WingCommand.PureTests
{
    public sealed class DepartureReadinessTests
    {
        [Theory]
        [InlineData(35f, false)]
        [InlineData(40f, false)]
        [InlineData(59.9f, false)]
        [InlineData(60.1f, true)]
        public void VagrantNativeCompletionWaitsForForwardFlight(float airspeed, bool expected)
        {
            float minimum = FormationGuidance.MinimumAirspeed(180f, 95f);
            Assert.Equal(expected, LaunchSafety.CanHandOff(true, false, false,
                105f, airspeed, 70f, minimum));
        }

        [Fact]
        public void ClimbOrSidewaysSpeedDoesNotReleaseEarlyTakeoff()
        {
            // 100 m/s total motion with only 40 m/s along the nose remains below launch speed.
            Assert.False(LaunchSafety.CanHandOff(false, true, false, 40f, 40f, 70f, 60f));
            Assert.True(LaunchSafety.CanHandOff(false, true, false, 40f, 90f, 70f, 60f));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void RotaryHoverDepartureKeepsNativeCompletion(bool complete, bool expected)
        {
            Assert.Equal(expected, LaunchSafety.CanHandOff(complete, !complete, true,
                25f, 0f, 70f, 60f));
        }
    }
}
