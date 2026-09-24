using Xunit;

namespace WingCommand.PureTests
{
    public class WmcStyleTests
    {
        [Theory]
        [InlineData("FIGHT", "danger")]
        [InlineData("DEFEND", "danger")]
        [InlineData("RTB", "armed")]
        [InlineData("BEHIND", "armed")]
        [InlineData("JOIN", "armed")]
        [InlineData("SLOT", "ready")]
        [InlineData("HOLD", "ready")]
        [InlineData("TRAIL", "ready")]
        [InlineData("GROUND", "locked")]
        [InlineData("LANDED", "locked")]
        [InlineData("SOMETHING", "info")]
        [InlineData(null, "info")]
        public void RailFollowsTheStateWord(string state, string rail) => Assert.Equal(rail, WmcStyle.Rail(state));

        [Theory]
        [InlineData(0.9f, "ok", "")]
        [InlineData(0.34f, "warn", "LOW")]
        [InlineData(0.1f, "bad", "CRIT")]
        [InlineData(float.NaN, "", "")]
        public void LevelsCarryAWordAsWellAsAColour(float fraction, string level, string word)
        {
            Assert.Equal(level, WmcStyle.Level(fraction));
            Assert.Equal(word, WmcStyle.LevelWord(fraction));
        }
    }
}
