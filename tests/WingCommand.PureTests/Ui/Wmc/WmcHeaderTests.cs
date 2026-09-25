using Xunit;

namespace WingCommand.PureTests
{
    public class WmcHeaderTests
    {
        [Fact]
        public void TitleCountsTheWingAndWhoIsUp()
        {
            Assert.Equal("WING 3/3 · 3 AIRBORNE", WmcHeader.Title(3, 3, 3));
            Assert.Equal("WING 1/3 · NONE AIRBORNE", WmcHeader.Title(1, 3, 0));
            Assert.Equal("NO WING", WmcHeader.Title(0, 3, 0));
        }

        [Fact]
        public void ChipsSayTheirStateInWords()
        {
            Assert.Equal("LINK 3/3", WmcHeader.Link(3, 0, false, false, out string s1)); Assert.Equal("live", s1);
            Assert.Equal("LINK 3/4", WmcHeader.Link(3, 1, false, false, out string s2)); Assert.Equal("warn", s2);
            Assert.Equal("LINK LOST", WmcHeader.Link(0, 0, true, true, out string s3)); Assert.Equal("danger", s3);
            Assert.Equal("PROFILE ESCORT", WmcHeader.Profile("ESCORT", false, out string p1)); Assert.Equal("info", p1);
            Assert.Equal("PROFILE MIXED", WmcHeader.Profile("ESCORT", true, out string p2)); Assert.Equal("warn", p2);
            Assert.Equal("HANGAR 1/3", WmcHeader.Hangar(1, 3, false, out string r1)); Assert.Equal("info", r1);
            Assert.Equal("HANGAR 0/3", WmcHeader.Hangar(0, 3, false, out string r0)); Assert.Equal("inert", r0);
            Assert.Equal("HANGAR —", WmcHeader.Hangar(0, 3, true, out string r2)); Assert.Equal("inert", r2);
            Assert.Equal("ARMED ATTACK", WmcHeader.Mode(MapMode.Attack, false, out string m1)); Assert.Equal("warn", m1);
            Assert.Equal("HOST", WmcHeader.Mode(MapMode.Off, false, out string m2)); Assert.Equal("live", m2);
            Assert.Equal("CLIENT", WmcHeader.Mode(MapMode.Off, true, out string m3)); Assert.Equal("info", m3);
        }

        [Fact]
        public void EveryChipTextFitsSixteenCharacters()
        {
            foreach (MapMode m in System.Enum.GetValues(typeof(MapMode)))
                Assert.True(WmcHeader.Mode(m, false, out _).Length <= WmcHeader.ChipChars, m.ToString());
            Assert.True(WmcHeader.Profile("CUSTOM", false, out _).Length <= WmcHeader.ChipChars);
        }

        [Fact]
        public void MetricKeysChangePerTab()
        {
            Assert.Equal("FUEL MIN", WmcHeader.Keys(0)[0][0]);
            Assert.Equal("THREAT", WmcHeader.Keys(0)[2][0]);
            Assert.Equal("FUNDS", WmcHeader.Keys(1)[0][0]);
            // User 2026-09-25: the airframe store is the HANGAR (RESERVE stays a doctrine word).
            Assert.Equal("HANGAR", WmcHeader.Keys(1)[1][0]);
            Assert.Equal("STOCK", WmcHeader.Keys(1)[2][0]);
            Assert.Equal("STATIONS", WmcHeader.Keys(2)[0][0]);
            Assert.Equal("LOST", WmcHeader.Keys(3)[2][0]);
        }
    }
}
