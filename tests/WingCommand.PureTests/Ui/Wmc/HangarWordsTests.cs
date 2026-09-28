using Xunit;

namespace WingCommand.PureTests
{
    public class HangarWordsTests
    {
        [Fact]
        public void TheLabelReadsStoredOverCapacityAndADashOffline()
        {
            Assert.Equal("HANGAR 1/3", HangarWords.Label(1, 3, false));
            Assert.Equal("HANGAR —", HangarWords.Label(1, 3, true));
            Assert.Equal("1/3", HangarWords.Value(1, 3, false));
            Assert.Equal("—", HangarWords.Value(0, 3, true));
        }

        [Fact]
        public void TheMeterFillsAndWarnsWhenFull()
        {
            Assert.Equal(0f, HangarWords.Level(0, 3));
            Assert.Equal(1f, HangarWords.Level(3, 3));
            Assert.Equal(0f, HangarWords.Level(1, 0));
            Assert.True(HangarWords.Full(3, 3));
            Assert.False(HangarWords.Full(2, 3));
        }

        [Fact]
        public void ReturnAsksBeforeItGivesTheAirframeBack()
        {
            Assert.Equal("RETURN", HangarWords.ReturnLabel(false));
            Assert.Equal("RETURN?", HangarWords.ReturnLabel(true));
            Assert.Equal("Return FS-20 Vortex to the faction's stock? Press RETURN again", HangarWords.Ask("FS-20 Vortex"));
        }

        [Fact]
        public void TheToastsNameTheAirframeAndTheCount()
        {
            Assert.Equal("FS-20 Vortex stored in the HANGAR (2/3)", HangarWords.Stored("FS-20 Vortex", 2, 3));
            Assert.Equal("FS-20 Vortex returned to the faction (1/3)", HangarWords.Returned("FS-20 Vortex", 1, 3));
        }

        [Fact]
        public void TheCaptionListsStoredTypesWithinTwentyTwoCharacters()
        {
            Assert.Equal("HOST ONLY", HangarWords.Caption(new string[0], false, true));
            Assert.Equal("NO FACTION", HangarWords.Caption(new string[0], true, false));
            Assert.Equal("NONE STORED", HangarWords.Caption(new string[0], true, true));
            Assert.Equal("VT-7 · A-19", HangarWords.Caption(new[] { "VT-7", "A-19" }, true, true));
            Assert.Equal("FS-20 ×2 · A-19", HangarWords.Caption(new[] { "FS-20", "A-19", "FS-20" }, true, true));
            string c = HangarWords.Caption(new[] { "SAH-46", "T/A-30", "UH-90" }, true, true);
            Assert.True(c.Length <= HangarWords.CaptionChars, c);
            Assert.EndsWith("+1", c);
        }
    }
}
