using Xunit;

namespace WingCommand.PureTests
{
    public class MapViewTests
    {
        private static MapView View()
        {
            var v = new MapView();
            v.Resize(1000f, 800f);
            v.SetMapHalf(100000f);
            v.FitMap();
            return v;
        }

        [Fact]
        public void FitMapShowsTheWholeTheatre()
        {
            MapView v = View();
            v.Project(-100000f, 100000f, out float x0, out float y0);
            v.Project(100000f, -100000f, out float x1, out float y1);
            Assert.InRange(x0, -500.01f, 0f);
            Assert.InRange(x1, 0f, 500.01f);
            Assert.InRange(y0, 0f, 400.01f);
            Assert.InRange(y1, -400.01f, 0f);
        }

        [Fact]
        public void ProjectAndUnprojectRoundTrip()
        {
            MapView v = View();
            v.Project(12345f, -6789f, out float px, out float py);
            v.Unproject(px, py, out float x, out float z);
            Assert.Equal(12345f, x, 1);
            Assert.Equal(-6789f, z, 1);
        }

        [Fact]
        public void ZoomAboutTheCursorKeepsThePoint()
        {
            // Review focus 3: the world point under the cursor stays under it.
            MapView v = View();
            v.Unproject(200f, -150f, out float wx, out float wz);
            v.ZoomAt(200f, -150f, 4f);
            v.Project(wx, wz, out float px, out float py);
            Assert.Equal(200f, px, 1);
            Assert.Equal(-150f, py, 1);
            Assert.True(v.MetresPerPixel < 250f);
        }

        [Fact]
        public void PanAndZoomClamp()
        {
            MapView v = View();
            for (int i = 0; i < 40; i++) v.ZoomAt(0f, 0f, 4f);
            Assert.Equal(MapView.MinMetresPerPixel, v.MetresPerPixel, 3);
            for (int i = 0; i < 40; i++) v.ZoomAt(0f, 0f, 0.25f);
            float widest = v.MetresPerPixel;
            v.ZoomAt(0f, 0f, 0.5f);
            Assert.Equal(widest, v.MetresPerPixel, 3);
            v.PanBy(1e7f, -1e7f);
            Assert.InRange(v.CentreX, -100000f, 100000f);
            Assert.InRange(v.CentreZ, -100000f, 100000f);
        }

        [Fact]
        public void FitFramesThePoints()
        {
            MapView v = View();
            float[] xs = { 1000f, 5000f, 3000f }, zs = { 2000f, 2500f, 9000f };
            v.Fit(xs, zs, 3, 2000f, 40f);
            for (int i = 0; i < 3; i++)
            {
                v.Project(xs[i], zs[i], out float px, out float py);
                // The tallest span lands exactly on the padded edge: half a pixel for rounding.
                Assert.InRange(px, -460.5f, 460.5f);
                Assert.InRange(py, -360.5f, 360.5f);
            }
            v.Fit(xs, zs, 1, 2000f, 40f);                 // one point: at least the minimum span
            Assert.True(v.MetresPerPixel * 800f >= 2000f);
        }

        [Fact]
        public void DegenerateInputsStayFinite()
        {
            // Review focus 2: no map, no size, NaN: nothing divides by zero.
            var v = new MapView();
            Assert.False(v.Valid);
            v.Project(10f, 10f, out float px, out float py);
            Assert.False(float.IsNaN(px) || float.IsNaN(py));
            v.Resize(0f, float.NaN);
            v.SetMapHalf(float.NaN);
            v.FitMap();
            v.ZoomAt(float.NaN, 0f, 2f);
            v.PanBy(float.NaN, 1f);
            v.Unproject(5f, 5f, out float x, out float z);
            Assert.False(float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(v.MetresPerPixel));
            v.Fit(new float[0], new float[0], 0, 1000f, 10f);
            Assert.False(float.IsNaN(v.CentreX));
        }
    }
}
