using Xunit;

namespace WingCommand.PureTests
{
    public sealed class MissileDefencePolicyTests
    {
        [Theory]
        [InlineData(20000f, 500f, false)]
        [InlineData(6000f, 500f, true)]
        [InlineData(6001f, 500f, false)]
        [InlineData(1000f, -50f, true)]
        [InlineData(1001f, -50f, false)]
        [InlineData(20000f, 0f, false)]
        public void SaturationOnlyYieldsToCriticalMissiles(float distance, float closing, bool expected)
        {
            Assert.Equal(expected, MissileDefencePolicy.InterruptsSaturation(distance, closing));
        }

        [Theory]
        [InlineData(WingRoe.Hold, "SARH", true, 10f, false, 100f, true)]
        [InlineData(WingRoe.Hold, "SARH", true, 10f, true, 5000f, false)]
        [InlineData(WingRoe.Hold, "SARH", true, 10f, true, 5001f, true)]
        [InlineData(WingRoe.Hold, "SARH", true, 10f, true, -1f, true)]
        [InlineData(WingRoe.Hold, "SARH", true, 3f, false, 100f, false)]
        [InlineData(WingRoe.Hold, "SARH", false, 10f, false, 100f, false)]
        [InlineData(WingRoe.Hold, "IR", true, 10f, false, 100f, false)]
        [InlineData(WingRoe.Hold, "ARH", true, 10f, false, 100f, false)]
        [InlineData(WingRoe.Tight, "SARH", true, 10f, false, 100f, false)]
        [InlineData(WingRoe.Free, "SARH", true, 10f, false, 100f, false)]
        public void HoldPrefersSarhInterceptionUnlessCoveredOrUrgent(WingRoe roe, string seeker,
            bool armed, float impact, bool cover, float distance, bool expected)
        {
            Assert.Equal(expected, MissileDefencePolicy.PreferInterception(
                roe, seeker, armed, impact, cover, distance, 5000f));
        }
    }
}
