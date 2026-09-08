using Xunit;

namespace WingCommand.PureTests
{
    public sealed class MissileDefencePolicyTests
    {
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
