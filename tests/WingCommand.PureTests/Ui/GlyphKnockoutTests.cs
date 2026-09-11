using Xunit;

namespace WingCommand.PureTests
{
    public class GlyphKnockoutTests
    {
        [Fact]
        public void TransparentCornersDoNotNeedKnockout()
        {
            float[] rgba = Solid(4, 4, 1f, 1f, 1f, 0f);
            PaintRect(rgba, 4, 1, 1, 2, 2, 1f, 1f, 1f, 1f);
            Assert.False(GlyphKnockout.NeedsKnockout(rgba, 4, 4));
        }

        [Fact]
        public void OpaqueCornersNeedKnockout()
        {
            float[] rgba = Solid(4, 4, 0f, 0f, 0f, 1f);
            Assert.True(GlyphKnockout.NeedsKnockout(rgba, 4, 4));
        }

        [Fact]
        public void WhiteGlyphOnBlackFieldLosesTheField()
        {
            float[] rgba = Solid(6, 6, 0f, 0f, 0f, 1f);
            PaintRect(rgba, 6, 2, 2, 2, 2, 1f, 1f, 1f, 1f);

            GlyphKnockout.Apply(rgba, 6, 6);

            AssertTransparent(rgba, 6, 0, 0);
            AssertTransparent(rgba, 6, 5, 5);
            AssertOpaqueWhite(rgba, 6, 2, 2);
            AssertOpaqueWhite(rgba, 6, 3, 3);
        }

        [Fact]
        public void DarkGlyphOnWhiteFieldLosesTheField()
        {
            float[] rgba = Solid(6, 6, 1f, 1f, 1f, 1f);
            PaintRect(rgba, 6, 2, 2, 2, 2, 0f, 0f, 0f, 1f);

            GlyphKnockout.Apply(rgba, 6, 6);

            AssertTransparent(rgba, 6, 0, 0);
            AssertTransparent(rgba, 6, 5, 0);
            Assert.True(Alpha(rgba, 6, 2, 2) > 0.5f);
            Assert.True(Alpha(rgba, 6, 3, 3) > 0.5f);
        }

        [Fact]
        public void AlreadyTransparentBufferIsUnchanged()
        {
            float[] rgba = Solid(3, 3, 1f, 1f, 1f, 0f);
            PaintRect(rgba, 3, 1, 1, 1, 1, 1f, 1f, 1f, 1f);
            float[] before = (float[])rgba.Clone();

            GlyphKnockout.Apply(rgba, 3, 3);

            Assert.Equal(before, rgba);
        }

        private static float[] Solid(int width, int height, float r, float g, float b, float a)
        {
            var rgba = new float[width * height * 4];
            PaintRect(rgba, width, 0, 0, width, height, r, g, b, a);
            return rgba;
        }

        private static void PaintRect(float[] rgba, int width, int x, int y, int w, int h,
                                      float r, float g, float b, float a)
        {
            for (int py = y; py < y + h; py++)
            {
                for (int px = x; px < x + w; px++)
                {
                    int o = (py * width + px) * 4;
                    rgba[o] = r;
                    rgba[o + 1] = g;
                    rgba[o + 2] = b;
                    rgba[o + 3] = a;
                }
            }
        }

        private static float Alpha(float[] rgba, int width, int x, int y) =>
            rgba[(y * width + x) * 4 + 3];

        private static void AssertTransparent(float[] rgba, int width, int x, int y) =>
            Assert.True(Alpha(rgba, width, x, y) < 0.05f);

        private static void AssertOpaqueWhite(float[] rgba, int width, int x, int y)
        {
            int o = (y * width + x) * 4;
            Assert.True(rgba[o + 3] > 0.5f);
            Assert.Equal(1f, rgba[o]);
            Assert.Equal(1f, rgba[o + 1]);
            Assert.Equal(1f, rgba[o + 2]);
        }
    }
}
