using Xunit;

namespace WingCommand.PureTests
{
    public class WmcTabsTests
    {
        [Fact]
        public void SevenTabsFlyingFirstThenLogistics()
        {
            Assert.Equal(new[] { "TACTICAL", "FORM", "PLAN", "INSPECT", "SUPPLY", "LOADOUT", "SQUADRON" }, WmcTabs.Labels);
            Assert.Equal(0, WmcTabs.Tactical);
            Assert.Equal(3, WmcTabs.Inspect);
            Assert.Equal(6, WmcTabs.Squadron);
        }

        [Fact]
        public void EveryLabelFitsItsTabAtElevenPixelsBold()
        {
            // 480 / 8 tabs (SETUP arrives as the 8th) = 60 px less a 6 + 6 px margin; ~6 px a character at 11 px bold.
            foreach (string label in WmcTabs.Labels)
            {
                Assert.True(label.Length <= 8, label);
                Assert.True(label.Length * 6f <= 60f - 12f, label);
            }
        }

        [Theory]
        [InlineData("TACTICAL", 0)]
        [InlineData("form", 1)]
        [InlineData("Plan", 2)]
        [InlineData("SQUADRON", 6)]
        [InlineData("WING", 6)]
        [InlineData("3", -1)]
        [InlineData("nope", -1)]
        [InlineData("", -1)]
        [InlineData("9", -1)]
        public void AScenarioNamesATabByItsLabelNeverItsNumber(string name, int tab) => Assert.Equal(tab, WmcTabs.Index(name));

        [Theory]
        [InlineData("tac.orders.cell3", 0)]
        [InlineData("form.shape0", 1)]
        [InlineData("plan.route.send", 2)]
        [InlineData("plan.ap.nav", 2)]
        [InlineData("insp.center", 3)]
        [InlineData("sup.requisition", 4)]
        [InlineData("lo.new", 5)]
        [InlineData("wing.recruit", 6)]
        [InlineData("sq.save", 6)]
        [InlineData("hdr.room", -1)]
        [InlineData(null, -1)]
        public void AControlIdShowsItsOwnTab(string id, int tab) => Assert.Equal(tab, WmcTabs.Of(id));

        [Fact]
        public void TheGroupRuleSitsBetweenInspectAndSupply()
        {
            Assert.Equal(274.3f, WmcTabs.GroupRuleX(480f, 7), 1);
            Assert.Equal(240f, WmcTabs.GroupRuleX(480f, 8), 1);
        }
    }
}
