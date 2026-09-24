using Xunit;

namespace WingCommand.PureTests
{
    public class MapGestureTests
    {
        [Fact]
        public void AReleaseNearThePressIsAClick()
        {
            var g = new MapGesture();
            g.NotePointerDown(100f, 100f);
            Assert.True(g.ReleasedAsClick(104f, 103f));
        }

        [Fact]
        public void ADragIsNotAClick()
        {
            // Review focus 2: a right-drag never places an order.
            var g = new MapGesture();
            g.NotePointerDown(100f, 100f);
            Assert.False(g.ReleasedAsClick(100f + MapGesture.ClickSlopPixels + 1f, 100f));
        }

        [Fact]
        public void AReleaseWithoutAPressIsNotAClick()
        {
            var g = new MapGesture();
            Assert.False(g.ReleasedAsClick(0f, 0f));
            g.NotePointerDown(5f, 5f);
            Assert.True(g.ReleasedAsClick(5f, 5f));
            Assert.False(g.ReleasedAsClick(5f, 5f));   // consumed
        }
    }
}
