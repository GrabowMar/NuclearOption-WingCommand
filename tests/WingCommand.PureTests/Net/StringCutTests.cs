using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Review M6a I1: the writer's string cut — one pass, no exception on a lone surrogate.</summary>
    public class StringCutTests
    {
        private static string RoundTrip(string s)
        {
            var buffer = new byte[128];
            var w = new ByteWriter(buffer);
            w.String(s);
            Assert.False(w.Overflow);
            Assert.True(w.Length <= 1 + Protocol.MaxString);
            return new ByteReader(buffer, 0, w.Length).String();
        }

        [Theory]
        [InlineData("€")]   // euro sign: 3 bytes
        [InlineData("中")]   // a CJK character: 3 bytes
        public void ThreeByteCharactersAreCutAtABoundary(string c)
        {
            string s = string.Concat(System.Linq.Enumerable.Repeat(c, 40));
            string back = RoundTrip(s);
            Assert.Equal(21, back.Length);
            Assert.StartsWith(back, s);
        }

        [Fact]
        public void AnEmojiStraddlingTheLimitIsLeftOutWhole()
        {
            // 62 ASCII bytes, then a 4-byte pair that would end at byte 66.
            string s = new string('a', 62) + char.ConvertFromUtf32(0x1F680);
            Assert.Equal(new string('a', 62), RoundTrip(s));
        }

        [Fact]
        public void AnEmojiThatFitsIsKeptWhole()
        {
            string s = new string('a', 60) + char.ConvertFromUtf32(0x1F680);
            Assert.Equal(s, RoundTrip(s));
        }

        [Fact]
        public void ALoneSurrogateIsWrittenAsAReplacementNotAnException()
        {
            string s = "a" + (char)0xD800 + "b";
            Assert.Equal("a" + (char)0xFFFD + "b", RoundTrip(s));
        }
    }
}
