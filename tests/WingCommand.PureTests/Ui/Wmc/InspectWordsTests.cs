using Xunit;

namespace WingCommand.PureTests
{
    public class InspectWordsTests
    {
        [Fact]
        public void TheTitleNamesSeatCallsignAndType()
        {
            Assert.Equal("#3 IBIS · FS-20", InspectWords.Title(1, "IBIS", "FS-20"));
            Assert.Equal("#3 · FS-20", InspectWords.Title(1, null, "FS-20"));
            Assert.Equal("#3", InspectWords.Title(1, "", null));
        }

        [Fact]
        public void ThePilotLineSaysRankExperienceAndKills()
        {
            Assert.Equal("IBIS · ROOKIE · XP 40 · 1 KILL", InspectWords.Pilot("IBIS", "ROOKIE", 40, 1, null));
            Assert.Equal("IBIS · ACE · XP 800 · 12 KILLS · WINGMAN, VETERAN", InspectWords.Pilot("IBIS", "ACE", 800, 12, "WINGMAN, VETERAN"));
            Assert.Equal("NO PILOT RECORD", InspectWords.Pilot(null, null, 0, 0, null));
        }

        [Fact]
        public void NothingToInspectSaysHowToPickOne()
        {
            Assert.Contains("INSPECT", InspectWords.Empty(false));
            Assert.Contains("host", InspectWords.Empty(true));
        }

        [Fact]
        public void AMemberChipIsItsSeat()
        {
            Assert.Equal("#2", InspectWords.Chip(0));
            Assert.Equal("#8", InspectWords.Chip(6));
        }
    }
}
