using Xunit;

namespace WingCommand.PureTests
{
    public class SupplyWordsTests
    {
        [Fact]
        public void AnAirframeNameDropsItsCodePrefix()
        {
            Assert.Equal("Vortex", SupplyWords.Name("FS-20 Vortex", "FS-20"));
            Assert.Equal("Compass", SupplyWords.Name("T/A-30 Compass", "T/A-30"));
            Assert.Equal("Tarantula", SupplyWords.Name("Tarantula", "VL-49"));
            Assert.Equal("FS-20", SupplyWords.Name("FS-20", "FS-20"));
            Assert.Equal("FS-20", SupplyWords.Code("FS-20", "FS-20 Vortex"));
            Assert.Equal("FS-20", SupplyWords.Code(null, "FS-20 Vortex"));
        }

        [Fact]
        public void TileWordsFitTheTile()
        {
            Assert.True(SupplyWords.Code("ABCDEFGHIJKLMNOP", "x").Length <= SupplyWords.CodeChars);
            string name = SupplyWords.Name("FS-20 Vortex Extended Range Heavy Strike", "FS-20");
            Assert.True(name.Length <= SupplyWords.NameChars, name);
            Assert.DoesNotContain("…", name);
        }

        [Fact]
        public void ACutEndsAtAWordAndNeverAddsAnEllipsis()
        {
            Assert.Equal("South Boscali", WmcText.Cut("South Boscali General", 16));
            Assert.Equal("ABCDE", WmcText.Cut("ABCDEFGH", 5));
            Assert.Equal("short", WmcText.Cut("short", 16));
            Assert.Equal("", WmcText.Cut(null, 5));
        }

        [Fact]
        public void StepChipsSayWhatEachStepHolds()
        {
            Assert.Equal("10 LISTED", SupplyWords.AirframeChip(10));
            Assert.Equal("NONE LISTED", SupplyWords.AirframeChip(0));
            Assert.Equal("FUEL 75%", SupplyWords.Fuel(75));
            Assert.Equal("2 BASES ON", SupplyWords.BaseChip(2, 7));
            Assert.Equal("1 BASE ON", SupplyWords.BaseChip(1, 7));
            Assert.Equal("ALL OFF", SupplyWords.BaseChip(0, 7));
            Assert.Equal("NO FIELD", SupplyWords.BaseChip(0, 0));
            foreach (string chip in new[] { SupplyWords.AirframeChip(9999), SupplyWords.Fuel(100), SupplyWords.BaseChip(99, 99), PilotPick.State(99) })
                Assert.True(chip.Length <= SupplyWords.ChipChars, chip);
        }

        [Fact]
        public void TheFitIsAutoYoursOrATemplateByName()
        {
            Assert.Equal("AUTO", SupplyWords.Fit(null, null));
            Assert.Equal("YOUR LOADOUT", SupplyWords.Fit(CallSpec.YourLoadout, null));
            Assert.Equal("CAP HEAVY", SupplyWords.Fit("t1", "Cap Heavy"));
            Assert.True(SupplyWords.Fit("t1", "A very long template name here").Length <= SupplyWords.FitChars);
            Assert.Equal("FIT · AUTO ›", SupplyWords.FitButton("AUTO"));
            Assert.Contains("the game", SupplyWords.FitDetail(null, false));
            Assert.Contains("LOADOUT", SupplyWords.FitDetail(null, false));
            Assert.DoesNotContain("arrive", SupplyWords.FitDetail(null, true));
        }

        [Fact]
        public void RequisitionNamesTheCostOrFree()
        {
            Assert.Equal("REQUISITION · 87 CR", SupplyWords.Requisition(87f));
            Assert.Equal("REQUISITION · FREE", SupplyWords.Requisition(0f));
        }

        [Fact]
        public void MetricCaptionsFitTheirTile()
        {
            Assert.Equal("NEXT CALL 87 CR", SupplyWords.FundsCaption(false, false, true, 87f));
            Assert.Equal("NEXT CALL 3M CR", SupplyWords.FundsCaption(false, false, true, 3_000_000f));
            Assert.Equal("SANDBOX · FREE", SupplyWords.FundsCaption(false, true, true, 87f));
            Assert.Equal("YOUR ALLOCATION", SupplyWords.FundsCaption(true, false, true, 87f));
            Assert.Equal("PICK AN AIRFRAME", SupplyWords.FundsCaption(false, false, false, 0f));
            Assert.Equal("FS-20 IN STOCK", SupplyWords.StockCaption("FS-20", 4, true));
            Assert.Equal("FS-20 NONE LEFT", SupplyWords.StockCaption("FS-20", 0, true));
            Assert.Equal("PICK AN AIRFRAME", SupplyWords.StockCaption(null, 0, false));
            Assert.True(SupplyWords.FundsCaption(false, false, true, 999_999f).Length <= SupplyWords.CaptionChars);
            Assert.True(SupplyWords.StockCaption("ABCDEFGHIJKLMNOP", 0, true).Length <= SupplyWords.CaptionChars);
        }

        [Fact]
        public void TheHintTellsTheHostWhatToDoAndAClientWhoDoesIt()
        {
            Assert.Contains("REQUISITION", SupplyWords.Hint(false, 0));
            Assert.Equal("2 inbound · watch LINK", SupplyWords.Hint(false, 2));
            Assert.Contains("host", SupplyWords.Hint(true, 2));
        }

        [Fact]
        public void TheDispatchDetailFitsTheCardAtTheFloorSize()
        {
            string line = ShopRules.DispatchLine("ABCDEFGHIJKLMN", SupplyWords.Fit("t", "ABCDEFGHIJKLMNOPQRS"), 100,
                BaseName.Short("South Boscali General Aviation Field"));
            Assert.True(line.Length <= SupplyWords.DispatchChars, line);
        }
    }
}
