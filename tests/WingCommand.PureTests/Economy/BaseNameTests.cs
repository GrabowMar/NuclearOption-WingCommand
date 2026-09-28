using Xunit;

namespace WingCommand.PureTests
{
    public class BaseNameTests
    {
        [Theory]
        [InlineData("South Boscali General Aviation", "South Boscali GA")]
        [InlineData("Vigil Cay Naval Airbase", "Vigil Cay Naval AB")]
        [InlineData("Dustbowl Highway Strip", "Dustbowl Hwy Strip")]
        [InlineData("North Boscali Airbase", "North Boscali AB")]
        [InlineData("Maris Airport", "Maris Airport")]
        [InlineData("K92 Highway Strip", "K92 Highway Strip")]
        public void ALongFieldNameIsShortenedByItsCommonWords(string name, string expected)
        {
            Assert.Equal(expected, BaseName.Short(name));
            Assert.True(BaseName.Short(name).Length <= BaseName.Max);
        }

        [Fact]
        public void ANameStillTooLongIsCutAtAWordAndNeverEllipsised()
        {
            string s = BaseName.Short("Grand Northern Mountain Reserve Strip");
            Assert.True(s.Length <= BaseName.Max);
            Assert.DoesNotContain("…", s);
            Assert.False(s.EndsWith(" "));
            Assert.Equal("Abcdefghijklmnopqrst", BaseName.Short("Abcdefghijklmnopqrstuvwxyz"));
        }

        [Fact]
        public void TheDisplayNameWinsElseTheUniqueNameIsFormatted()
        {
            Assert.Equal("Ashwood Aux", BaseName.Of("Ashwood Aux", "airbase_ashwood", "x"));
            Assert.Equal("Northeast Airbase", BaseName.Of(null, "airbase_NE", "x"));
            Assert.Equal("Island 5 Airfield", BaseName.Of("", null, "airbase_island5(Clone)"));
        }
    }
}
