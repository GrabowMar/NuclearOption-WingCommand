using Xunit;

namespace WingCommand.PureTests
{
    public class WmcTabsTests
    {
        [Fact]
        public void FiveTabsFlyingFirstThenLogistics()
        {
            // The user (2026-09-28): fewer tabs — PLAN goes into FORM, renamed BEHAVIOUR; INSPECT goes into SQUADRON.
            Assert.Equal(new[] { "TACTICAL", "BEHAVIOUR", "SUPPLY", "LOADOUT", "SQUADRON" }, WmcTabs.Labels);
            Assert.Equal(0, WmcTabs.Tactical);
            Assert.Equal(1, WmcTabs.Behaviour);
            Assert.Equal(2, WmcTabs.Supply);
            Assert.Equal(4, WmcTabs.Squadron);
        }

        [Fact]
        public void EveryLabelFitsItsTabAtElevenPixelsBold()
        {
            // 480 / 5 tabs = 96 px less a 6 + 6 px margin; ~6.5 px a character at 11 px bold.
            foreach (string label in WmcTabs.Labels)
                Assert.True(label.Length * 6.5f <= 480f / WmcTabs.Labels.Length - 12f, label);
        }

        [Theory]
        [InlineData("TACTICAL", 0)]
        [InlineData("behaviour", 1)]
        [InlineData("FORM", 1)]
        [InlineData("Plan", 1)]
        [InlineData("INSPECT", 4)]
        [InlineData("SQUADRON", 4)]
        [InlineData("WING", 4)]
        [InlineData("SUPPLY", 2)]
        [InlineData("3", -1)]
        [InlineData("nope", -1)]
        [InlineData("", -1)]
        public void AScenarioNamesATabByItsLabelNeverItsNumber(string name, int tab) => Assert.Equal(tab, WmcTabs.Index(name));

        [Theory]
        [InlineData("tac.orders.cell3", 0)]
        [InlineData("form.shape0", 1)]
        [InlineData("opt.targets.air", 1)]
        [InlineData("plan.route.send", 1)]
        [InlineData("plan.ap.nav", 1)]
        [InlineData("insp.center", 4)]
        [InlineData("sup.requisition", 2)]
        [InlineData("lo.new", 3)]
        [InlineData("wing.recruit", 4)]
        [InlineData("sq.save", 4)]
        [InlineData("hdr.fit", -1)]
        [InlineData(null, -1)]
        public void AControlIdShowsItsOwnTab(string id, int tab) => Assert.Equal(tab, WmcTabs.Of(id));

        [Fact]
        public void TheGroupRuleSitsBetweenBehaviourAndSupply() => Assert.Equal(192f, WmcTabs.GroupRuleX(480f, 5), 1);
    }
}
