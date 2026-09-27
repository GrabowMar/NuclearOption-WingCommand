using Xunit;

namespace WingCommand.PureTests
{
    public class BezelLayoutTests
    {
        [Fact]
        public void BodyIsThePanelLessTheChrome()
        {
            // Spec bezel v2 §3: no metric row, so the chrome is 176 and the body grows by 84 px.
            Assert.Equal(720f, BezelLayout.Body(896f));
            Assert.Equal(420f, BezelLayout.Body(596f));
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

        // ---- LOADOUT

        [Fact]
        public void TheLiveryPinSitsOnTheBodyFloorAtBothDocks()
        {
            Assert.Equal(600f, BezelLayout.LoadoutView(636f));
            Assert.Equal(300f, BezelLayout.LoadoutView(336f));
            Assert.Equal(6f + 30f, BezelLayout.LiveryPin);
        }

        [Fact]
        public void ATallDockShowsSixHardpointRowsAndThePagerWithoutScrolling()
        {
            Assert.Equal(6, BezelLayout.HardpointRows(636f));
            Assert.True(BezelLayout.LoadoutContent(6, true) <= BezelLayout.LoadoutView(636f));
        }

        [Fact]
        public void AShortDockShowsFourRowsAndScrolls()
        {
            Assert.Equal(4, BezelLayout.HardpointRows(336f));
            Assert.True(BezelLayout.LoadoutContent(4, false) > BezelLayout.LoadoutView(336f));
        }

        [Fact]
        public void TheHardpointsStartBelowTheCardTilesAndTemplateBar() =>
            Assert.Equal(84f + 10f + 22f + 4f + 94f + 10f + 26f + 10f, BezelLayout.HardpointsTop);

        [Fact]
        public void TheHardpointColumnsTileTheContentWidthWithoutOverlap()
        {
            Assert.True(BezelLayout.ColStation + BezelLayout.ColStationW <= BezelLayout.ColStore);
            Assert.True(BezelLayout.ColStore + BezelLayout.ColStoreW <= BezelLayout.ColMass);
            Assert.True(BezelLayout.ColMass + BezelLayout.ColMassW <= BezelLayout.ColVerb);
            Assert.Equal(BezelLayout.Content, BezelLayout.ColVerb + BezelLayout.ColVerbW);
        }

        [Fact]
        public void TheTemplateBarFillsTheContentWidthWithDeleteSetApart() =>
            Assert.Equal(BezelLayout.Content, BezelLayout.TemplatePick + 4f + BezelLayout.TemplateBtn + 4f + BezelLayout.TemplateBtn
                + BezelLayout.DeleteGap + BezelLayout.TemplateBtn);

        [Fact]
        public void APopupIsSevenRowsAtMostAsTheToolkitDrawsIt()
        {
            Assert.Equal(3 * 32f + 8f, BezelLayout.PopupHeight(3));
            Assert.Equal(7 * 32f + 8f, BezelLayout.PopupHeight(12));
            Assert.Equal(32f + 8f, BezelLayout.PopupHeight(0));
        }

        [Fact]
        public void AStorePopupOpensBelowItsRowWhenThereIsRoom()
        {
            Assert.Equal(142f, BezelLayout.PopupPlace(100f, 42f, 136f, 636f, 0f, out float scroll));
            Assert.Equal(0f, scroll);
        }

        [Fact]
        public void AStorePopupOpensAboveItsRowNearTheFloor()
        {
            Assert.Equal(168f, BezelLayout.PopupPlace(400f, 42f, 232f, 636f, 0f, out float scroll));
            Assert.Equal(0f, scroll);
        }

        [Fact]
        public void AMiddleRowOnAShortDockScrollsIntoRoomFirst()
        {
            float top = BezelLayout.PopupPlace(130f, 42f, 232f, 336f, 200f, out float scroll);
            Assert.Equal(68f, scroll);
            Assert.Equal(104f, top);
            Assert.True(top + 232f <= 336f);
        }

        [Fact]
        public void APopupNeverCoversItsRowAtEitherDock()
        {
            foreach (float body in new[] { 336f, 636f })
                for (float row = 0f; row + 42f <= body; row += 10f)
                {
                    float top = BezelLayout.PopupPlace(row, 42f, 232f, body, 1000f, out float scroll);
                    float r = row - scroll;
                    Assert.True(top >= r + 42f || top + 232f <= r, $"body {body} row {row}");
                    Assert.True(top >= 0f && top + 232f <= body, $"body {body} row {row} top {top}");
                }
        }

        [Theory]
        [InlineData(336f)]
        [InlineData(390f)]
        [InlineData(440f)]
        [InlineData(506f)]
        public void AHardpointPopupNeverCoversItsRowWithThePagesRealScroll(float body)
        {
            // Review R5: the page can scroll only as far as its content allows, forward (MaxOffset - Offset) and back (Offset).
            float view = BezelLayout.LoadoutView(body);
            int rows = BezelLayout.HardpointRows(body);
            foreach (bool paged in new[] { false, true })
            {
                float max = System.Math.Max(0f, BezelLayout.LoadoutContent(rows, paged) - view);
                for (int entries = 5; entries <= 7; entries++)
                {
                    float popupH = BezelLayout.PopupHeight(entries);
                    for (float offset = 0f; offset <= max; offset += 1f)
                        for (int i = 0; i < rows; i++)
                        {
                            float row = BezelLayout.HardpointsTop + BezelLayout.HardpointHead + i * BezelLayout.HpPitch - offset;
                            if (row < 0f || row + BezelLayout.HpRowH > view) continue;
                            float top = BezelLayout.PopupPlace(row, BezelLayout.HpRowH, popupH, body, max - offset, out float scroll, offset);
                            Assert.InRange(offset + scroll, 0f, max);
                            float r = row - scroll;
                            Assert.True(top >= r + BezelLayout.HpRowH || top + popupH <= r, $"body {body} paged {paged} n {entries} off {offset} row {i}");
                            Assert.True(top >= 0f && top + popupH <= body, $"body {body} off {offset} row {i} top {top}");
                        }
                }
            }
        }

        // ---- WING

        [Fact]
        public void TheAssignmentBarSitsOnTheBodyFloorAtBothDocks()
        {
            Assert.Equal(574f, BezelLayout.WingView(636f));
            Assert.Equal(274f, BezelLayout.WingView(336f));
            Assert.Equal(6f + 56f, BezelLayout.WingPin);
        }

        [Fact]
        public void ATallDockShowsEightPilotsAndTheWholePageWithoutScrolling()
        {
            Assert.Equal(8, BezelLayout.PilotRows(636f));
            Assert.True(BezelLayout.WingContent(8) <= BezelLayout.WingView(636f));
        }

        [Fact]
        public void PilotRowsStayBetweenFourAndEight()
        {
            for (float body = 336f; body <= 636f; body += 10f)
            {
                int rows = BezelLayout.PilotRows(body);
                Assert.InRange(rows, 4, 8);
            }
            Assert.Equal(4, BezelLayout.PilotRows(336f));
        }

        [Fact]
        public void TwoPerkCardsAndTheirGapFillTheContentWidth() =>
            Assert.Equal(BezelLayout.Content, 2f * BezelLayout.PerkCardW(BezelLayout.Content) + BezelLayout.PerkGap, 3);

        [Fact]
        public void ThreeTilesAndTheirGapsFillTheContentWidth()
        {
            float w = BezelLayout.TileWidth(BezelLayout.Content);
            Assert.Equal(458f, 3f * w + 2f * BezelLayout.TileGap, 3);
            Assert.True(w >= 140f);
        }
    }
}
