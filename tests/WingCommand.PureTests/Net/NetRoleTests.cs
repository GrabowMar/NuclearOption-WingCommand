using Xunit;

namespace WingCommand.PureTests
{
    public class NetRoleTests
    {
        [Theory]
        [InlineData(true, true, false)]     // single player and a host: the server runs here
        [InlineData(false, true, true)]     // a client, spawned or not (review M7b-1 I3)
        [InlineData(false, false, false)]   // no session yet
        [InlineData(true, false, false)]    // a dedicated server
        public void ClientOnlyIsTheRoleNotTheAircraft(bool serverActive, bool clientActive, bool expected) =>
            Assert.Equal(expected, NetRole.ClientOnly(serverActive, clientActive));
    }
}
