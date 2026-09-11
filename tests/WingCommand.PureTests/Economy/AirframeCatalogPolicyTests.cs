using Xunit;

namespace WingCommand.PureTests
{
    public class AirframeCatalogPolicyTests
    {
        [Theory]
        [InlineData("???")]
        [InlineData("??")]
        [InlineData("?")]
        public void QuestionMarkNamesAreHidden(string name)
        {
            Assert.True(AirframeCatalogPolicy.IsHiddenFromPanels(name));
            Assert.True(AirframeCatalogPolicy.IsHiddenFromPanels("KR-67 Ifrit", name, "ifrit"));
            Assert.True(AirframeCatalogPolicy.IsHiddenFromPanels("KR-67 Ifrit", "KR-67", name));
        }

        [Theory]
        [InlineData("UFO")]
        [InlineData("ufo")]
        [InlineData("Ufo")]
        public void UfoDevKeyIsHidden(string jsonKey)
        {
            Assert.True(AirframeCatalogPolicy.IsHiddenFromPanels("???", "???", jsonKey));
            Assert.True(AirframeCatalogPolicy.IsHiddenFromPanels("Mystery", "MYST", jsonKey));
        }

        [Theory]
        [InlineData("KR-67 Ifrit", "KR-67", "ifrit")]
        [InlineData("CI-22 Cricket", "CI-22", "cricket")]
        [InlineData("CargoPlane1", "CARGO", "CargoPlane1")]
        [InlineData(null, null, null)]
        [InlineData("", "", "")]
        public void OrdinaryAirframesStayVisible(string unitName, string code, string jsonKey)
        {
            Assert.False(AirframeCatalogPolicy.IsHiddenFromPanels(unitName, code, jsonKey));
        }
    }
}
