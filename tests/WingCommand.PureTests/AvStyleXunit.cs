using NOAvionics.Tests;
using Xunit;

namespace WingCommand
{
    public sealed class AvStyleXunit
    {
        [Fact]
        public void SharedAvionicsStylesheetHolds()
        {
            AvStyleTests.Run((ok, message) => Assert.True(ok, message));
        }
    }
}
