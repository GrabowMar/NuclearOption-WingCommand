using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>R7: what a saved pilot's text may hold (research squadron-studio §2.1; critic §5 R7 1): the MFD font shows printable
    /// ASCII, so Polish letters are transliterated and everything else is dropped at the source.</summary>
    public class PilotTextTests
    {
        [Theory]
        [InlineData("  hatch  ", "HATCH")]
        [InlineData("ab   c", "AB C")]
        [InlineData("x!y@z", "XYZ")]
        [InlineData("iron-6", "IRON-6")]
        [InlineData("abcdefghijklmnopq", "ABCDEFGHIJKLMN")]
        [InlineData("Łoś", "LOS")]
        [InlineData("żółć", "ZOLC")]
        [InlineData("tab\there", "TAB HERE")]
        [InlineData("   ", "")]
        [InlineData(null, "")]
        public void ACallsignIsTrimmedUppercasedFilteredAndCutToFourteen(string raw, string clean) =>
            Assert.Equal(clean, PilotText.Callsign(raw));

        [Fact]
        public void ACutCallsignNeverEndsInASpaceOrHyphen() => Assert.Equal("ABCDEFGHIJKLM", PilotText.Callsign("abcdefghijklm nop"));

        [Fact]
        public void ANameKeepsCaseCollapsesWhitespaceAndIsCutToTwentyFour()
        {
            Assert.Equal("Ola Bae", PilotText.Name("  Ola \n\t Bae "));
            Assert.Equal("Zbigniew Lukaszewicz", PilotText.Name("Zbigniew Łukaszewicz"));
            Assert.Equal(24, PilotText.Name(new string('a', 40)).Length);
            Assert.Equal("", PilotText.Name("\u0001\u0002"));
        }

        [Fact]
        public void ATagIsUppercasedAndCutToTwentyFour()
        {
            Assert.Equal("HATCH TWO", PilotText.Tag(" hatch  two "));
            Assert.Equal(24, PilotText.Tag(new string('b', 30)).Length);
        }

        [Fact]
        public void ABioKeepsLinesDropsControlsAndIsCutToTwoHundredEighty()
        {
            Assert.Equal("Line one\nLine two", PilotText.Bio("Line one\r\nLine two"));
            Assert.Equal("Tab a", PilotText.Bio("Tab\ta"));
            Assert.Equal(280, PilotText.Bio(new string('c', 400)).Length);
            Assert.Equal("Wait...", PilotText.Bio("Wait…"));
            Assert.Equal("Cross", PilotText.Bio("Cross †"));
        }

        [Fact]
        public void EmojiAndOtherScriptsAreDropped()
        {
            Assert.Equal("ACE", PilotText.Callsign("ace\U0001F680"));
            Assert.Equal("Anna", PilotText.Name("Anna 李"));
        }

        [Fact]
        public void NothingCleanedKeepsAGlyphTheFontLacks()
        {
            foreach (string s in new[] { PilotText.Name("Łódź ☺ …"), PilotText.Bio("Ωmega ■ ← †"), PilotText.Callsign("ß-ø") })
                foreach (char c in s)
                    Assert.True(c == '\n' || (c >= ' ' && c <= '~'), s);
        }
    }
}
