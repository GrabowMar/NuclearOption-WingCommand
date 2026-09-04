using Xunit;

namespace WingCommand
{
    public sealed class VanillaMfdPanelCatalogTests
    {
        [Theory]
        [InlineData("BDF", 1)]
        [InlineData("map", 2)]
        [InlineData(" HUD ", 3)]
        [InlineData("PALA", 4)]
        [InlineData("TGT", 5)]
        [InlineData("MIS", 6)]
        public void RecognisesEveryCanonicalStockPanel(string shortName, int expected)
        {
            Assert.Equal((VanillaMfdPanelId)expected, VanillaMfdPanelCatalog.FromShortName(shortName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("WMC")]
        [InlineData("OPS")]
        [InlineData("RAD")]
        [InlineData("future-panel")]
        public void LeavesModAndUnknownPanelsAlone(string shortName)
        {
            Assert.Equal(VanillaMfdPanelId.Unknown, VanillaMfdPanelCatalog.FromShortName(shortName));
        }

        [Theory]
        [InlineData("BDF", "BDF")]
        [InlineData("MAP", "MAP")]
        [InlineData("HUD", "HUD")]
        [InlineData("PALA", "PALA")]
        [InlineData("TGT", "TGT")]
        [InlineData("MIS", "MIS")]
        public void KeepsEveryOwnedHeaderStable(string shortName, string label)
        {
            Assert.Equal(label, VanillaMfdPanelCatalog.Label(
                VanillaMfdPanelCatalog.FromShortName(shortName)));
        }
    }
}
