using Xunit;

namespace WingCommand.PureTests
{
    public class RadioLogTests
    {
        [Fact]
        public void KeepsLinesOldestFirst()
        {
            var log = new RadioLog();
            log.Push(1f, "a");
            log.Push(2f, "b");
            Assert.Equal(2, log.Count);
            Assert.Equal("a", log.TextAt(0));
            Assert.Equal(2f, log.TimeAt(1));
        }

        [Fact]
        public void OverwritesTheOldestWhenFullAndClears()
        {
            var log = new RadioLog();
            for (int i = 0; i < RadioLog.Capacity + 3; i++) log.Push(i, "l" + i);
            Assert.Equal(RadioLog.Capacity, log.Count);
            Assert.Equal("l3", log.TextAt(0));
            Assert.Equal("l" + (RadioLog.Capacity + 2), log.TextAt(RadioLog.Capacity - 1));
            log.Clear();
            Assert.Equal(0, log.Count);
        }
    }
}
