using Xunit;

namespace WingCommand.Tests
{
    public class LaunchSafetyTests
    {
        [Theory]
        [InlineData(80f, 90f, 0f, false, true)]
        [InlineData(79f, 90f, 0f, false, false)]
        [InlineData(80f, 75f, 0f, false, false)]
        [InlineData(80f, 90f, -3f, false, false)]
        [InlineData(200f, 90f, 0f, true, false)]
        public void HandoffRequiresClearanceSpeedStableClimbAndNativeRelease(
            float altitude, float speed, float sink, bool launching, bool expected)
        {
            Assert.Equal(expected, LaunchSafety.ReadyForHandoff(altitude, speed, 70f, sink, false, launching));
        }

        [Fact]
        public void HelicopterHandoffDoesNotRequireJetSpeed()
        {
            Assert.True(LaunchSafety.ReadyForHandoff(80f, 5f, 70f, 0f, true, false));
        }

        [Fact]
        public void ClearanceUsesBothAirframesAndNeverShrinksBelowApronMargin()
        {
            Assert.Equal(30f, LaunchSafety.Clearance(14f, 14f));
            Assert.Equal(60f, LaunchSafety.Clearance(20f, 80f));
            Assert.Equal(LaunchSafety.Clearance(20f, 80f), LaunchSafety.Clearance(80f, 20f));
        }

        [Fact]
        public void FreshLaunchCannotFollowSteepLeaderBank()
        {
            Assert.InRange(LaunchSafety.RejoinBankLimit(80f, 200f, 70f), 8f, 25f);
            Assert.InRange(LaunchSafety.RejoinBankLimit(150f, 90f, 70f), 8f, 40f);
            Assert.InRange(LaunchSafety.RejoinBankLimit(1000f, 75f, 70f), 8f, 9f);
            Assert.InRange(LaunchSafety.RejoinBankLimit(1000f, 300f, 70f), 70f, 88f);
        }

        [Fact]
        public void DistantPlayerNoLongerRequires450MetreClimb()
        {
            Assert.False(ClimbOutPolicy.ShouldClimbOut(150f, 20000f, WingOrder.Formation,
                true, false, true, 90f, 70f));
            Assert.True(ClimbOutPolicy.ShouldClimbOut(150f, 20000f, WingOrder.Formation,
                true, false, true, 75f, 70f));
            Assert.True(ClimbOutPolicy.ShouldAccelerateLevel(80f, 75f, 70f));
        }
    }
}
