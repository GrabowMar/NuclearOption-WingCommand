using NOAvionics.Tests;
using Xunit;

namespace WingCommand
{
    public sealed class AvGridXunit
    {
        [Fact]
        public void SharedAvionicsGridGeometryHolds()
        {
            AvGridTests.Run((ok, message) => Assert.True(ok, message));
        }
    }
}
