using Xunit;

namespace WingCommand.PureTests
{
    public class SarRulesTests
    {
        [Fact]
        public void ALocalSearchCostsHalfTheLostAirframesValue() => Assert.Equal(44f, SarRules.LocalCost(87f, false));

        [Fact]
        public void TheRescueBountyIsHalfTheAirframesValue() => Assert.Equal(44f, SarRules.Bounty(87f));

        [Fact]
        public void AnAirframeOfUnknownValueUsesTheFloor()
        {
            Assert.Equal(5f, SarRules.LocalCost(0f, false));
            Assert.Equal(5f, SarRules.Bounty(-3f));
        }

        [Fact]
        public void TheSandboxSearchesForFree() => Assert.Equal(0f, SarRules.LocalCost(87f, true));
    }
}
