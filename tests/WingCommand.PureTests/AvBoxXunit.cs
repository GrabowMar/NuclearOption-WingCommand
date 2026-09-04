using NOAvionics.Tests;
using Xunit;

namespace WingCommand
{
    public sealed class AvBoxXunit
    {
        [Fact]
        public void SharedAvionicsLayoutGeometryHolds()
        {
            AvBoxTests.Run((ok, message) => Assert.True(ok, message));
        }
    }
}
