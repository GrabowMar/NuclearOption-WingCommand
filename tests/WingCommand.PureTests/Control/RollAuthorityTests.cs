using Xunit;

namespace WingCommand.PureTests
{
    public class RollAuthorityTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Fly <paramref name="seconds"/> of a plant whose roll rate follows G·stick through a 0.2 s lag.</summary>
        private static float Fly(RollAuthority r, float trueDps, float stick, float seconds, float rate = 0f)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                r.Update(stick, rate, Dt);
                rate += (trueDps * stick - rate) * (Dt / 0.2f);
            }
            return rate;
        }

        [Fact]
        public void LearnsTheRateAFullStickGivesFromSteadyRolling()
        {
            var r = new RollAuthority();
            r.Reset(286f);
            Fly(r, 95f, 0.5f, 15f);
            Assert.InRange(r.RateDps, 90f, 100f);
        }

        [Fact]
        public void LearnsWhileTheStickKeepsMoving()
        {
            var r = new RollAuthority();
            r.Reset(286f);
            float rate = 0f;
            for (int k = 0; k < 20; k++) rate = Fly(r, 95f, k % 2 == 0 ? 0.6f : -0.6f, 1.5f, rate);
            Assert.InRange(r.RateDps, 80f, 110f);
        }

        [Fact]
        public void IgnoresTicksWithTheStickNearCentre()
        {
            var r = new RollAuthority();
            r.Reset(100f);
            for (int i = 0; i < 600; i++) r.Update(0.05f, 30f, Dt);
            Assert.Equal(100f, r.RateDps);
        }

        [Fact]
        public void IgnoresRatesAgainstTheStick()
        {
            var r = new RollAuthority();
            r.Reset(100f);
            for (int i = 0; i < 600; i++) r.Update(0.5f, -20f, Dt);
            Assert.Equal(100f, r.RateDps);
        }

        [Fact]
        public void StaysBetweenTheFloorAndAMultipleOfTheSeed()
        {
            var high = new RollAuthority();
            high.Reset(100f);
            for (int i = 0; i < 3000; i++) high.Update(1f, 1000f, Dt);
            var low = new RollAuthority();
            low.Reset(100f);
            for (int i = 0; i < 3000; i++) low.Update(1f, 1f, Dt);
            Assert.Equal(RollAuthority.CeilingFactor * 100f, high.RateDps, 3);
            Assert.Equal(RollAuthority.FloorDps, low.RateDps, 3);
        }

        [Fact]
        public void ResetSeedsTheEstimate()
        {
            var r = new RollAuthority();
            r.Reset(120f);
            Fly(r, 60f, 1f, 10f);
            r.Reset(150f);
            Assert.Equal(150f, r.RateDps);
        }
    }
}
