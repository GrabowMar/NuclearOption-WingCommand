using Xunit;

namespace WingCommand.PureTests
{
    public class MapFitTests
    {
        [Fact]
        public void TheZoomFramesTheSpanWithAMargin()
        {
            // 0.01 px a metre at zoom 1, 600 px to show 20 km with 15 % margin: 600 / (23000 * 0.01) = 2.6.
            Assert.Equal(2.61f, MapFit.Zoom(20000f, 600f, 0.01f), 2);
        }

        [Fact]
        public void ATightWingStillShowsEightKilometres()
        {
            Assert.Equal(MapFit.Zoom(8000f, 600f, 0.01f), MapFit.Zoom(500f, 600f, 0.01f), 3);
        }

        [Fact]
        public void TheZoomStaysInsideTheGamesRange()
        {
            Assert.Equal(1f, MapFit.Zoom(900000f, 600f, 0.01f));
            Assert.Equal(40f, MapFit.Zoom(8000f, 600f, 0.00001f));
            Assert.Equal(1f, MapFit.Zoom(8000f, 0f, 0.01f));
            Assert.Equal(1f, MapFit.Zoom(float.NaN, 600f, 0.01f));
        }

        [Fact]
        public void TheBoxSpansItsPointsAndCentresOnThem()
        {
            var box = new MapBox();
            box.Add(0f, 0f);
            box.Add(4000f, -2000f);
            box.Add(1000f, 6000f);
            Assert.True(box.Any);
            Assert.Equal(8000f, box.Span, 1);
            Assert.Equal(2000f, box.CenterX, 1);
            Assert.Equal(2000f, box.CenterZ, 1);
            Assert.False(new MapBox().Any);
        }

        [Fact]
        public void TheOffsetPutsThePointAtTheViewCentre()
        {
            // The game draws the map at -(stationary + offset): a point p (map units) is centred when offset = p - stationary.
            var (ox, oy) = MapFit.Offset(100f, -50f, 0.5f, 30f, 20f);
            Assert.Equal(20f, ox, 3);
            Assert.Equal(-45f, oy, 3);
        }
    }
}
