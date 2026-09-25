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
        // range, min, max, target alt, min alt, max alt, off-boresight, limit, own speed, min own speed
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, 200f, 0f, true)]
        [InlineData(11000f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, 200f, 0f, false)]   // beyond max range
        [InlineData(300f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, 200f, 0f, false)]     // inside min range
        [InlineData(5000f, 500f, 10000f, 40f, 100f, 20000f, 10f, 30f, 200f, 0f, false)]    // below the distance-scaled floor (50 m)
        [InlineData(5000f, 500f, 10000f, 60f, 100f, 20000f, 10f, 30f, 200f, 0f, true)]
        [InlineData(5000f, 500f, 10000f, 25000f, 0f, 20000f, 10f, 30f, 200f, 0f, false)]   // above max altitude
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 45f, 30f, 200f, 0f, false)]    // off-boresight beyond the limit
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 30f, 30f, 200f, 0f, false)]    // the game's limit is strict
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 0f, 0f, 200f, 0f, false)]      // review M5d-2 I2: 0 means never
        [InlineData(5000f, 500f, 10000f, 3000f, 0f, 20000f, 10f, 30f, 100f, 150f, false)]  // launcher below its min speed
        public void TheLaunchEnvelopeFromTheSlot(float range, float min, float max, float alt, float minAlt, float maxAlt,
            float off, float limit, float speed, float minSpeed, bool inside) =>
            Assert.Equal(inside, StandingFire.InEnvelope(range, min, max, alt, minAlt, maxAlt, off, limit, speed, minSpeed));

        [Theory]
        // members committed, missiles in flight, attacks the target needs
        [InlineData(0, 0, 1f, false)]
        [InlineData(1, 0, 1f, true)]    // review M5d-2 I1: another member's shot this tick
        [InlineData(0, 1, 1f, true)]    // a missile already flying at it
        [InlineData(1, 0, 2f, false)]
        [InlineData(1, 1, 2f, true)]
        [InlineData(1, 0, 0.3f, true)]  // any target needs at least one
        public void ASaturatedTargetIsLeftAlone(int committed, int inFlight, float needed, bool saturated) =>
            Assert.Equal(saturated, StandingFire.Saturated(committed, inFlight, needed));

        [Fact]
        public void QuickDrawShortensTheInterval()
        {
            var c = new FireCadence();
            Assert.True(c.Due(0.5f, 0.6f));
            c.Fired();
            for (int i = 0; i < 4; i++) Assert.False(c.Due(0.5f, 0.6f));   // 2.0 s
            Assert.True(c.Due(0.5f, 0.6f));                               // 2.5 s ≥ 0.6 × 4 s
        }

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
