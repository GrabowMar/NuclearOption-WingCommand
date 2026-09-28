using Xunit;

namespace WingCommand.PureTests
{
    public class CallWordsTests
    {
        [Fact]
        public void OneLaunchNamesTheTypeTheFieldAndThePilot()
        {
            Assert.Equal("VT-7 launching from Maris Airport · HATCH", CallWords.Launched(1, "VT-7", "Maris Airport", "HATCH", null));
            Assert.Equal("2 × VT-7 launching from Maris Airport", CallWords.Launched(2, "VT-7", "Maris Airport", "HATCH", null));
        }

        [Fact]
        public void ALaunchCutShortSaysWhyTheRestDidNot() =>
            Assert.Equal("VT-7 launching from Maris Airport · HATCH (then: none of that type in the faction's stock)",
                CallWords.Launched(1, "VT-7", "Maris Airport", "HATCH", "none of that type in the faction's stock"));

        [Fact]
        public void NoLaunchIsARefusalWithTheLedgersReasonOrTheField()
        {
            Assert.Equal("Cannot call VT-7: needs 87 CR, the allocation holds 20 CR", CallWords.Refused("VT-7", "needs 87 CR, the allocation holds 20 CR"));
            Assert.Equal("Maris Airport could not launch VT-7", CallWords.Refused("VT-7", null, "Maris Airport"));
        }
    }
}
