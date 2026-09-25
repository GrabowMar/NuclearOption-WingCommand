using Xunit;

namespace WingCommand.PureTests
{
    public class TemplateNamesTests
    {
        [Fact]
        public void ANameIsTrimmedUpperCasedAndCutToSixteenAtAWord()
        {
            Assert.Equal("CAP HEAVY TWO", TemplateNames.Clean("  cap\theavy   two  "));
            Assert.Equal("A VERY LONG", TemplateNames.Clean("a very long template name"));
            Assert.True(TemplateNames.Clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ").Length <= TemplateNames.MaxChars);
        }

        [Fact]
        public void ABlankNameKeepsTheOldOne()
        {
            Assert.Null(TemplateNames.Clean("   "));
            Assert.Null(TemplateNames.Clean(null));
            Assert.Null(TemplateNames.Clean("\u0001\u0002"));
        }

        [Fact]
        public void DefaultNamesTakeTheFirstFreeNumber()
        {
            Assert.Equal("TEMPLATE 1", TemplateNames.NextDefault(new string[0]));
            Assert.Equal("TEMPLATE 3", TemplateNames.NextDefault(new[] { "TEMPLATE 1", "template 2" }));
        }

        [Fact]
        public void ACopyTakesTheNextFreeNumberAndStillFits()
        {
            Assert.Equal("CAP 2", TemplateNames.Unique("CAP", new[] { "cap" }));
            Assert.Equal("CAP 3", TemplateNames.Unique("CAP", new[] { "CAP", "CAP 2" }));
            string u = TemplateNames.Unique("ABCDEFGHIJKLMNOP", new[] { "ABCDEFGHIJKLMNOP" });
            Assert.Equal("ABCDEFGHIJKLMN 2", u);
            Assert.True(u.Length <= TemplateNames.MaxChars);
        }

        [Fact]
        public void AFreeNameIsKeptAndNamesCompareWithoutCase()
        {
            Assert.Equal("CAP", TemplateNames.Unique("CAP", new[] { "STRIKE" }));
            Assert.True(TemplateNames.Taken("cap two", new[] { "CAP TWO" }));
            Assert.False(TemplateNames.Taken("CAP", new[] { "CAP TWO" }));
        }

        [Fact]
        public void AnAirframeHoldsFiveTemplatesSoSupplyFitNeverPages() =>
            // AUTO + YOUR LOADOUT + 5 = the toolkit popup's 7 rows (review of R5 research C5).
            Assert.Equal(7, 2 + TemplateNames.PerAirframe);
    }
}
