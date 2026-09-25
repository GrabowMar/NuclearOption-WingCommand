using Xunit;

namespace WingCommand.PureTests
{
    public class PagesTests
    {
        [Fact]
        public void AnEmptyListHasOnePage()
        {
            Assert.Equal(1, Pages.Count(0, 6));
            Assert.Equal(0, Pages.Clamp(3, 0, 6));
            Assert.Equal("1 / 1", Pages.Label(0, 1));
        }

        [Fact]
        public void APageBeyondTheLastIsPulledBack()
        {
            Assert.Equal(1, Pages.Clamp(5, 7, 6));
            Assert.Equal(0, Pages.Clamp(-2, 7, 6));
        }

        [Fact]
        public void TheItemsOfAPageStartAtItsFirstSlot()
        {
            Assert.Equal(0, Pages.First(0, 6));
            Assert.Equal(12, Pages.First(2, 6));
            Assert.Equal(2, Pages.Of(13, 6));
            Assert.Equal(0, Pages.Of(-1, 6));
        }

        [Fact]
        public void SixAirframesFillOnePageAndASeventhOpensTheSecond()
        {
            Assert.Equal(1, Pages.Count(6, 6));
            Assert.Equal(2, Pages.Count(7, 6));
            Assert.Equal("2 / 2", Pages.Label(1, 2));
        }
    }
}
