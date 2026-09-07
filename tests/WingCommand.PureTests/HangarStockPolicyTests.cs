using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class HangarStockPolicyTests
    {
        [Theory]
        [InlineData("HPAD", "helipad")]
        [InlineData("REV", "revetment")]
        [InlineData("HGR-M", "hangar")]
        [InlineData("HGR-H", "shelter")]
        [InlineData("SHP", "ship")]
        [InlineData("X", "X")]
        [InlineData("", "pad")]
        public void PadCodesMatchTheNativeAirbaseInfoPanel(string code, string label)
        {
            Assert.Equal(label, HangarStockPolicy.PadLabel(code));
        }

        [Fact]
        public void JsonKeyMatchBeatsAMissingUnitName()
        {
            Assert.True(HangarStockPolicy.SameAirframe("vtol_trainer", "", "vtol_trainer", "VT-7 Vagrant"));
        }

        [Fact]
        public void UnitNameMatchWorksWhenJsonKeysDiffer()
        {
            Assert.True(HangarStockPolicy.SameAirframe("a", "VT-7 Vagrant", "b", "VT-7 Vagrant"));
            Assert.False(HangarStockPolicy.SameAirframe("a", "VT-7 Vagrant", "b", "KR-67 Ifrit"));
        }

        [Fact]
        public void FieldStockJoinsEachPadType()
        {
            string text = HangarStockPolicy.FormatFieldStock(new List<string>
            {
                HangarStockPolicy.FormatPadStock("shelter", new[] { "VT-7", "KR-67", "FS-20" }),
                HangarStockPolicy.FormatPadStock("helipad", new[] { "UH-90", "SAH-46" }),
            });
            Assert.Equal("shelter: VT-7, KR-67, FS-20 | helipad: UH-90, SAH-46", text);
        }

        [Fact]
        public void LaunchSiteSummaryNamesTheFieldsThatListTheAirframe()
        {
            Assert.Equal("Launch: island14 (shelter), NE (hangar, helipad)",
                HangarStockPolicy.FormatLaunchSites(new[]
                {
                    "island14 (shelter)",
                    "NE (hangar, helipad)",
                }));
        }

        [Fact]
        public void EmptyLaunchSitesStayAnHonestFailure()
        {
            Assert.Equal("No hangar or helipad lists this airframe",
                HangarStockPolicy.FormatLaunchSites(null));
            Assert.Equal("No hangar or helipad lists this airframe",
                HangarStockPolicy.FormatLaunchSites(new List<string>()));
        }

        [Fact]
        public void LongListsAreCapped()
        {
            Assert.Equal("A, B +2", HangarStockPolicy.JoinLimited(new[] { "A", "B", "C", "D" }, 2));
        }
    }
}
