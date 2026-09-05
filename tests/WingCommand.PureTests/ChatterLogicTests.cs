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
    }
}
