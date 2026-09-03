using NOAvionics.Tests;
using Xunit;

namespace WingCommand
{
    public sealed class AvionicsProtocolXunit
    {
        [Fact]
        public void SharedAvionicsProtocolHolds()
        {
            AvionicsProtocolTests.Run((ok, message) => Assert.True(ok, message));
        }
    }
}
