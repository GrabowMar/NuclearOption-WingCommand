using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>R7: the SQUADRON notch's three columns (research squadron-studio §2.5) at the design floor (body 1278×650) and a real
    /// 16:9 window (1838×898).</summary>
    public class SquadronLayoutTests
    {
        [Theory]
        [InlineData(1278f)]
        [InlineData(1838f)]
        [InlineData(1500f)]
        public void ColumnsFillTheBodyWithoutOverlap(float w)
        {
            float l = SquadronLayout.ListW(w), c = SquadronLayout.StudioW(w), r = SquadronLayout.RecordW(w);
            Assert.Equal(SquadronLayout.Pad, SquadronLayout.ListX);
            Assert.Equal(SquadronLayout.ListX + l + SquadronLayout.Gap, SquadronLayout.StudioX(w));
            Assert.Equal(SquadronLayout.StudioX(w) + c + SquadronLayout.Gap, SquadronLayout.RecordX(w));
            Assert.Equal(w - SquadronLayout.Pad, SquadronLayout.RecordX(w) + r, 3);
        }

        [Fact]
        public void TheColumnsHaveTheResearchWidths()
        {
            Assert.Equal((280f, 630f, 320f), (SquadronLayout.ListW(1278f), SquadronLayout.StudioW(1278f), SquadronLayout.RecordW(1278f)));
            Assert.Equal((340f, 1010f, 440f), (SquadronLayout.ListW(1838f), SquadronLayout.StudioW(1838f), SquadronLayout.RecordW(1838f)));
        }

        [Fact]
        public void TheListShowsElevenRowsAt650AndSixteenAt898()
        {
            Assert.Equal(11, SquadronLayout.ListRows(650f));
            Assert.Equal(16, SquadronLayout.ListRows(898f));
            Assert.Equal(SquadronLayout.MinRows, SquadronLayout.ListRows(100f));
        }

        [Theory]
        [InlineData(650f)]
        [InlineData(898f)]
        [InlineData(760f)]
        public void TheLastRowEndsAboveTheListFooter(float h)
        {
            int n = SquadronLayout.ListRows(h);
            float lastBottom = SquadronLayout.RowsTop + (n - 1) * SquadronLayout.RowPitch + SquadronLayout.RowH;
            Assert.True(lastBottom <= h - SquadronLayout.Pad - SquadronLayout.ListFooter, $"{lastBottom} at {h}");
        }

        [Fact]
        public void ThePortraitIsNativeOnTallBodiesAndTwoThirdsOnShortOnes()
        {
            Assert.Equal((128f, 192f), (SquadronLayout.PortraitW(898f), SquadronLayout.PortraitH(898f)));
            Assert.Equal((96f, 144f), (SquadronLayout.PortraitW(650f), SquadronLayout.PortraitH(650f)));
        }

        [Fact]
        public void TheStudioSubColumnsSplitItsWidth()
        {
            Assert.Equal(304f, SquadronLayout.LookW(630f, 650f));
            Assert.Equal(314f, SquadronLayout.IdentityW(630f, 650f));
            Assert.Equal(396f, SquadronLayout.LookW(1010f, 898f));
            Assert.Equal(602f, SquadronLayout.IdentityW(1010f, 898f));
        }

        [Theory]
        [InlineData(650f)]
        [InlineData(898f)]
        public void TheStudioBottomStaysAboveTheBodyFloor(float h)
        {
            float bio = SquadronLayout.BioH(h);
            Assert.InRange(bio, 96f, 180f);
            Assert.True(SquadronLayout.StudioBottom(h) <= h - SquadronLayout.Pad, $"{SquadronLayout.StudioBottom(h)} at {h}");
        }
    }
}
