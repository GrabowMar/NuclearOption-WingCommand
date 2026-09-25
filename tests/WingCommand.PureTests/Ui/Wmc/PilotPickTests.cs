using Xunit;

namespace WingCommand.PureTests
{
    public class PilotPickTests
    {
        [Fact]
        public void NoFreePilotGivesNoIndexAndADash()
        {
            Assert.Equal(-1, PilotPick.Index(0, 0));
            Assert.Equal("—", PilotPick.Counter(-1, 0));
        }

        [Fact]
        public void TheSelectionKeepsItsPlaceWhileItIsFree()
        {
            Assert.Equal(2, PilotPick.Index(2, 3));
            Assert.Equal("3/3", PilotPick.Counter(2, 3));
        }

        [Fact]
        public void ASelectionOffTheFreeListShowsTheFirstFreePilot()
        {
            Assert.Equal(0, PilotPick.Index(-1, 3));
            Assert.Equal(0, PilotPick.Index(5, 3));
        }

        [Fact]
        public void SteppingWrapsBothWays()
        {
            Assert.Equal(0, PilotPick.Step(2, 3, 1));
            Assert.Equal(2, PilotPick.Step(0, 3, -1));
            Assert.Equal(1, PilotPick.Step(0, 3, 1));
        }

        [Fact]
        public void SteppingWithOnePilotStaysPut()
        {
            Assert.Equal(0, PilotPick.Step(0, 1, 1));
            Assert.Equal(0, PilotPick.Step(0, 1, -1));
            Assert.Equal(-1, PilotPick.Step(0, 0, 1));
        }

        [Fact]
        public void TheStepChipCountsFreePilotsOrSaysANewPilotIsDrafted()
        {
            Assert.Equal("3 FREE", PilotPick.State(3));
            Assert.Equal("NEW PILOT", PilotPick.State(0));
        }

        [Fact]
        public void TheCardSaysWhoFliesOrThatANewPilotIsDraftedAtLaunch()
        {
            Assert.Equal("HATCH · O. Bae", PilotPick.NameLine("HATCH", "O. Bae"));
            Assert.Equal("HATCH", PilotPick.NameLine("HATCH", null));
            Assert.Equal("NEW PILOT", PilotPick.NameLine(null, null));
            Assert.Equal("ROOKIE · XP 70", PilotPick.RankLine("Rookie", 70));
            Assert.Equal("READY", PilotPick.Status(true));
            Assert.Equal("DRAFTED AT LAUNCH", PilotPick.Status(false));
        }

        [Fact]
        public void TheNameLineCapsTheCallsignAtFourteen() =>
            Assert.Equal("ABCDEFGHIJKLMN", PilotPick.NameLine("ABCDEFGHIJKLMNOPQR", null));

        [Fact]
        public void EveryLineFitsItsBudget()
        {
            string name = PilotPick.NameLine("ABCDEFGHIJKLMN", "Maximilian Alexander Wolfeschlegelstein");
            Assert.True(name.Length <= PilotPick.LineChars, name);
            Assert.StartsWith("ABCDEFGHIJKLMN · Maximilian", name);
            Assert.DoesNotContain("…", name);
            Assert.True(PilotPick.RankLine("Wing Commander", 99999).Length <= PilotPick.LineChars);
        }
    }
}
