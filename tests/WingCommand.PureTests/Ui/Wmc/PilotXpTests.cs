using Xunit;

namespace WingCommand.PureTests
{
    public class PilotXpTests
    {
        [Fact]
        public void HalfwayThroughRookieFillsHalfTheFirstFifth() => Assert.Equal(0.1f, PilotXp.Fill(60), 3);

        [Fact]
        public void EachRankThresholdStartsItsOwnFifth()
        {
            Assert.Equal(0.2f, PilotXp.Fill(120), 3);
            Assert.Equal(0.4f, PilotXp.Fill(360), 3);
            Assert.Equal(0.6f, PilotXp.Fill(720), 3);
            Assert.Equal(0.2f, PilotXp.Tick(1), 3);
        }

        [Fact]
        public void ALegendFillsTheWholeBarAndNothingReadsBelowEmpty()
        {
            Assert.Equal(1f, PilotXp.Fill(5000), 3);
            Assert.Equal(0f, PilotXp.Fill(-20), 3);
        }

        [Fact]
        public void TheRankLineNamesTheNextRankOrTheTop()
        {
            Assert.Equal("ROOKIE · XP 70 / 120 · NEXT WINGMAN", PilotXp.RankLine(70));
            Assert.Equal("LEGEND · XP 1,340 · TOP RANK", PilotXp.RankLine(1340));
            Assert.Equal("WINGMAN", PilotPerks.RankName(WingRank.Wingman));
        }
    }
}
