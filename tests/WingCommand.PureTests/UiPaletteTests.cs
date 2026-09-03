using NOAvionics.Tests;
using Xunit;

namespace WingCommand
{
    public sealed class UiPaletteTests
    {
        [Fact]
        public void SharedAvionicsTokenContrastHolds()
        {
            AvionicsTokenTests.Run((ok, message) => Assert.True(ok, message));
        }
    }
}
