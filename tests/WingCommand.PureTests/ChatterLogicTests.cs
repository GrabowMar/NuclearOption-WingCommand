using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class ChatterLogicTests
    {
        [Theory]
        [InlineData("John Smith", "Maverick", "John \"MAVERICK\" SMITH")]
        [InlineData("Cher", "Ace", "\"ACE\" CHER")]
        [InlineData(null, null, "\"NO CALLSIGN\" UNKNOWN")]
        public void Identity_FormatsGivenNameCallsignSurname(string name, string callsign, string expected)
        {
            Assert.Equal(expected, ChatterDialogue.Identity(name, callsign));
        }

        [Fact]
        public void Ambient_WrapsIntoRange()
        {
            ChatterExchange first = ChatterDialogue.Ambient(seed: 0);
            ChatterExchange wrapped = ChatterDialogue.Ambient(seed: ChatterDialogue.AmbientCount);

            Assert.Equal(first.Opening, wrapped.Opening);
        }

        [Fact]
        public void EveryOrderHasARadioAcknowledgementLine()
        {
            foreach (WingOrder order in System.Enum.GetValues(typeof(WingOrder)))
            {
                string line = ChatterDialogue.Acknowledge(
                    ChatterPersona.Professional, order.ToString(), seed: 0);
                Assert.False(string.IsNullOrWhiteSpace(line));
            }
        }

        [Fact]
        public void RefitHasItsOwnRadioAcknowledgementLine()
        {
            string single = ChatterDialogue.Acknowledge(
                ChatterPersona.Professional, "REFIT", seed: 0);
            string group = ChatterDialogue.GroupAcknowledge(
                ChatterPersona.Professional, "REFIT", "Two", seed: 0);

            Assert.False(string.IsNullOrWhiteSpace(single));
            Assert.False(string.IsNullOrWhiteSpace(group));
        }
    }
}
