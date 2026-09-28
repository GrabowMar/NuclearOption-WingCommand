using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RolePolicyTests
    {
        private const float Dt = 1f / 60f;

        private static RoleInput Jet(float anchorSpeed, float minSpeed = 72f) =>
            new RoleInput { AnchorSpeed = anchorSpeed, MinSpeed = minSpeed, TopSpeed = 255f };

        private static RoleInput Helo(float anchorSpeed) => new RoleInput { AnchorSpeed = anchorSpeed, MinSpeed = 0f, TopSpeed = 64f };

        private static float RunUntilChange(RolePolicy policy, RoleInput m, float maxSeconds, out RoleReason reason)
        {
            reason = RoleReason.None;
            for (int i = 1; i <= (int)Math.Round(maxSeconds / Dt); i++)
                if (policy.Tick(m, Dt, out reason)) return i * Dt;
            return float.NaN;
        }

        [Fact]
        public void JetBehindASlowAnchorTakesHighCoverAfterThreeSecondsAndComesBackAboveOnePointThreeVmin()
        {
            var policy = new RolePolicy();
            Assert.InRange(RunUntilChange(policy, Jet(80f), 10f, out RoleReason why), 3f, 3.05f);   // below 1.15 x 72
            Assert.Equal(Role.HighCover, policy.Current);
            Assert.Equal(RoleReason.AnchorSlow, why);
            Assert.True(float.IsNaN(RunUntilChange(policy, Jet(90f), 10f, out _)));                // between the thresholds
            Assert.InRange(RunUntilChange(policy, Jet(110f), 10f, out why), 3f, 3.05f);            // above 1.3 x 72
            Assert.Equal(Role.Slot, policy.Current);
            Assert.Equal(RoleReason.AnchorRecovered, why);
        }

        [Fact]
        public void SlowAirframeFollowsAFlyableSlowAnchorOfItsOwnType()
        {
            // In game a CI-22 wing (loaded minimum 46.9 m/s) held overhead 70–85% of the time behind a CI-22 leader
            // climbing at ~60 m/s: the thresholds are ratios of the member's own minimum.
            var policy = new RolePolicy();
            Assert.True(float.IsNaN(RunUntilChange(policy, Jet(58f, 46.9f), 10f, out _)));
            Assert.Equal(Role.Slot, policy.Current);
        }

        [Fact]
        public void HelicopterNeverTakesHighCoverEvenBehindAHover()
        {
            var policy = new RolePolicy();
            Assert.True(float.IsNaN(RunUntilChange(policy, Helo(0f), 20f, out _)));
            Assert.Equal(Role.Slot, policy.Current);
        }

        [Fact]
        public void HelicopterBehindAFastAnchorTrailsAndRejoinsOnceItSlowsDown()
        {
            var policy = new RolePolicy();
            Assert.InRange(RunUntilChange(policy, Helo(200f), 10f, out RoleReason why), 5f, 5.05f);
            Assert.Equal(Role.Trail, policy.Current);
            Assert.Equal(RoleReason.AnchorFast, why);
            Assert.True(float.IsNaN(RunUntilChange(policy, Helo(60f), 10f, out _)));               // above 0.85 x 64
            Assert.InRange(RunUntilChange(policy, Helo(50f), 10f, out why), 5f, 5.05f);
            Assert.Equal(Role.Slot, policy.Current);
            Assert.Equal(RoleReason.AnchorRecovered, why);
        }

        [Fact]
        public void JetNeverTrailsAnAnchorItCanKeepUpWith()
        {
            var policy = new RolePolicy();
            Assert.True(float.IsNaN(RunUntilChange(policy, Jet(250f), 20f, out _)));
            Assert.Equal(Role.Slot, policy.Current);
        }

        [Fact]
        public void AnAnchorFlickeringAcrossTheThresholdNeverChangesTheRole()
        {
            var policy = new RolePolicy();
            for (int i = 0; i < 60 * 60; i++)
                Assert.False(policy.Tick(Jet(i / 60 % 2 == 0 ? 80f : 90f), Dt, out _));            // slow 1 s, fine 1 s
            Assert.Equal(Role.Slot, policy.Current);
        }
    }
}
