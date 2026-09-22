using NOAvionics.Tests;
using Xunit;

namespace WingCommand.PureTests.Ui
{
    public sealed class PanelStyleTests
    {
        [Fact]
        public void SharedPaletteKeepsContrastAndStateHierarchy() =>
            AvionicsTokenTests.Run((condition, message) => Assert.True(condition, message));

        [Fact]
        public void StylesPreserveThemeReferencesAndSurviveBadInput() =>
            AvStyleTests.Run((condition, message) => Assert.True(condition, message));
    }
}
