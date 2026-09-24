using Xunit;

namespace WingCommand.PureTests
{
    public class StandingFireTests
    {
        [Theory]
        [InlineData((int)TargetPolicy.Hold, (int)StandingMode.None, (int)DoctrineAllow.None)]
        [InlineData((int)TargetPolicy.Cover, (int)StandingMode.Cover, (int)DoctrineAllow.None)]
        [InlineData((int)TargetPolicy.Air, (int)StandingMode.Opportunity, (int)DoctrineAllow.AirOnly)]
        [InlineData((int)TargetPolicy.Ground, (int)StandingMode.Opportunity, (int)DoctrineAllow.GroundOnly)]
        [InlineData((int)TargetPolicy.Both, (int)StandingMode.Opportunity, (int)DoctrineAllow.AirAndGround)]
        public void TheDoctrinesTargetPolicyDecidesTheMode(int targets, int mode, int allow)
        {
            Assert.Equal((StandingMode)mode, StandingFire.Decide((TargetPolicy)targets, out DoctrineAllow a));
            Assert.Equal((DoctrineAllow)allow, a);
        }

        [Theory]
        [InlineData((int)DoctrineAllow.AirOnly, true, true)]
        [InlineData((int)DoctrineAllow.AirOnly, false, false)]
        [InlineData((int)DoctrineAllow.GroundOnly, false, true)]
        [InlineData((int)DoctrineAllow.GroundOnly, true, false)]
        [InlineData((int)DoctrineAllow.AirAndGround, true, true)]
        [InlineData((int)DoctrineAllow.AirAndGround, false, true)]
        [InlineData((int)DoctrineAllow.None, true, false)]
        public void TheClassFilter(int allow, bool air, bool allowed) =>
            Assert.Equal(allowed, StandingFire.Allows((DoctrineAllow)allow, air));

        [Theory]
        // range, min, max, target alt, min alt, max alt, off-boresight, limit
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, true)]
        [InlineData(11000f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, false)]   // beyond max range
        [InlineData(300f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, false)]     // inside min range
        [InlineData(5000f, 500f, 10000f, 40f, 100f, 20000f, 10f, 30f, false)]    // below the distance-scaled floor (50 m)
        [InlineData(5000f, 500f, 10000f, 60f, 100f, 20000f, 10f, 30f, true)]
        [InlineData(5000f, 500f, 10000f, 25000f, 0f, 20000f, 10f, 30f, false)]   // above max altitude
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 45f, 30f, false)]    // off-boresight beyond the limit
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 170f, 0f, true)]     // no limit
        public void TheLaunchEnvelopeFromTheSlot(float range, float min, float max, float alt, float minAlt, float maxAlt,
            float off, float limit, bool inside) =>
            Assert.Equal(inside, StandingFire.InEnvelope(range, min, max, alt, minAlt, maxAlt, off, limit));

        [Fact]
        public void TheCadenceChecksTwiceASecondAndFiresAtMostEveryFourSeconds()
        {
            var c = new FireCadence();
            Assert.False(c.Due(0.4f));
            Assert.True(c.Due(0.1f));
            c.Fired();
            for (int i = 0; i < 7; i++) Assert.False(c.Due(0.5f));   // 3.5 s
            Assert.True(c.Due(0.5f));                               // 4.0 s
        }
    }
}
