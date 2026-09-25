using Xunit;

namespace WingCommand.PureTests
{
    public class BezelLayoutTests
    {
        [Fact]
        public void BodyIsThePanelLessTheChrome()
        {
            Assert.Equal(636f, BezelLayout.Body(896f));
            Assert.Equal(336f, BezelLayout.Body(596f));
        }

        [Fact]
        public void TheListFitsThreeWingmenInTwoElementsOnATallDock()
        {
            float cap = BezelLayout.ListCap(BezelLayout.Body(896f));
            Assert.True(cap >= 2 * BezelLayout.HeaderPitch + 3 * BezelLayout.RowPitch);
            Assert.Equal(116f, BezelLayout.ListCap(BezelLayout.Body(596f)));
        }

        [Fact]
        public void TheDispatchPinSitsOnTheBodyFloorAtBothDocks()
        {
            Assert.Equal(548f, BezelLayout.SupplyView(636f));
            Assert.Equal(248f, BezelLayout.SupplyView(336f));
            Assert.Equal(636f, BezelLayout.SupplyView(636f) + BezelLayout.SupplyPin);
        }

        [Fact]
        public void TheDispatchPinAddsUpToItsParts()
        {
            // No OVER-LIMIT row (user 2026-09-25: the mode is a setting): gap, card, gap, REQUISITION.
            Assert.Equal(6f + 48f + 4f + 30f, BezelLayout.SupplyPin);
            Assert.Equal(BezelLayout.PinGap + BezelLayout.PinCard + BezelLayout.PinGap2 + BezelLayout.RequisitionH, BezelLayout.SupplyPin);
        }

        [Fact]
        public void AllFourStepsFitATallDockWithNothingInbound()
        {
            Assert.Equal(491f, BezelLayout.SupplyContent(0, false));
            Assert.True(BezelLayout.SupplyContent(0, false) <= BezelLayout.SupplyView(636f));
        }

        [Fact]
        public void ATallDockTakesOneInboundOrAnAdoptRowWithoutScrolling()
        {
            Assert.True(BezelLayout.SupplyContent(1, false) <= BezelLayout.SupplyView(636f));
            Assert.True(BezelLayout.SupplyContent(0, true) <= BezelLayout.SupplyView(636f));
        }

        [Fact]
        public void AShortDockScrollsTheStepsAndKeepsTheDispatchWhole()
        {
            float view = BezelLayout.SupplyView(336f);
            Assert.True(BezelLayout.SupplyContent(0, false) > view);
            Assert.True(view >= BezelLayout.AirframeStep, "a whole step fits the short viewport");
        }

        [Fact]
        public void InboundRowsAndAdoptOnlyLengthenTheScroll()
        {
            Assert.Equal(132f, BezelLayout.SupplyContent(3, true) - BezelLayout.SupplyContent(0, false));
            Assert.Equal(BezelLayout.SupplyContent(BezelLayout.InboundMax, false), BezelLayout.SupplyContent(9, false));
            Assert.Equal(4, BezelLayout.InboundRows(9));
            Assert.Equal(0, BezelLayout.InboundRows(0));
        }

        [Fact]
        public void ThreeTilesAndTheirGapsFillTheContentWidth()
        {
            float w = BezelLayout.TileWidth(BezelLayout.Content);
            Assert.Equal(458f, 3f * w + 2f * BezelLayout.TileGap, 3);
            Assert.True(w >= 140f);
        }
    }
}
