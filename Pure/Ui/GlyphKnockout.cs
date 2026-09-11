namespace WingCommand
{
    /// <summary>Removes opaque glyph backgrounds for silhouette tinting without a visible
    /// square.</summary>
    internal static class GlyphKnockout
    {
        public const float OpaqueCornerAlpha = 0.12f;
        private const float HardDistance = 0.16f;
        private const float SoftDistance = 0.32f;
        private const float DarkBackgroundLuminance = 0.35f;

        public static bool NeedsKnockout(float[] rgba, int width, int height)
        {
            if (!Valid(rgba, width, height)) return false;
            CornerAlpha(rgba, width, height, out float a00, out float a10, out float a01, out float a11);
            return a00 > OpaqueCornerAlpha || a10 > OpaqueCornerAlpha
                || a01 > OpaqueCornerAlpha || a11 > OpaqueCornerAlpha;
        }

        /// <summary>Flood transparent alpha from connected edge colour, then map remaining coverage to
        /// white. Leave already-transparent edges unchanged.</summary>
        public static void Apply(float[] rgba, int width, int height)
        {
            if (!NeedsKnockout(rgba, width, height)) return;

            SampleEdgeBackground(rgba, width, height,
                out float br, out float bg, out float bb, out float bLum);
            Flood(rgba, width, height, br, bg, bb);
            SoftenAndFlatten(rgba, width, height, br, bg, bb, bLum);
        }

        private static bool Valid(float[] rgba, int width, int height) =>
            rgba != null && width > 0 && height > 0 && rgba.Length >= width * height * 4;

        private static void CornerAlpha(float[] rgba, int width, int height,
                                        out float a00, out float a10, out float a01, out float a11)
        {
            a00 = rgba[3];
            a10 = rgba[(width - 1) * 4 + 3];
            a01 = rgba[(height - 1) * width * 4 + 3];
            a11 = rgba[(((height - 1) * width) + (width - 1)) * 4 + 3];
        }

        private static void SampleEdgeBackground(float[] rgba, int width, int height,
                                                 out float r, out float g, out float b, out float lum)
        {
            float sr = 0f, sg = 0f, sb = 0f;
            int samples = 0;
            int yLast = height - 1;
            int xLast = width - 1;
            int yInner = height > 1 ? height - 2 : 0;
            int xInner = width > 1 ? width - 2 : 0;

            for (int x = 0; x < width; x++)
            {
                Acc(rgba, width, x, 0, ref sr, ref sg, ref sb, ref samples);
                Acc(rgba, width, x, yLast, ref sr, ref sg, ref sb, ref samples);
                if (height > 2)
                {
                    Acc(rgba, width, x, 1, ref sr, ref sg, ref sb, ref samples);
                    Acc(rgba, width, x, yInner, ref sr, ref sg, ref sb, ref samples);
                }
            }

            for (int y = 1; y < yLast; y++)
            {
                Acc(rgba, width, 0, y, ref sr, ref sg, ref sb, ref samples);
                Acc(rgba, width, xLast, y, ref sr, ref sg, ref sb, ref samples);
                if (width > 2)
                {
                    Acc(rgba, width, 1, y, ref sr, ref sg, ref sb, ref samples);
                    Acc(rgba, width, xInner, y, ref sr, ref sg, ref sb, ref samples);
                }
            }

            if (samples == 0)
            {
                r = g = b = lum = 0f;
                return;
            }

            float inv = 1f / samples;
            r = sr * inv;
            g = sg * inv;
            b = sb * inv;
            lum = Luminance(r, g, b);
        }

        private static void Acc(float[] rgba, int width, int x, int y,
                                ref float sr, ref float sg, ref float sb, ref int samples)
        {
            int o = (y * width + x) * 4;
            if (rgba[o + 3] < OpaqueCornerAlpha) return;
            sr += rgba[o];
            sg += rgba[o + 1];
            sb += rgba[o + 2];
            samples++;
        }

        private static void Flood(float[] rgba, int width, int height, float br, float bg, float bb)
        {
            int n = width * height;
            var visited = new bool[n];
            var stack = new int[n];
            int sp = 0;

            void TryPush(int x, int y)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
                int i = y * width + x;
                if (visited[i]) return;
                int o = i * 4;
                if (rgba[o + 3] <= 0.001f)
                {
                    visited[i] = true;
                    return;
                }
                if (DistanceSq(rgba[o], rgba[o + 1], rgba[o + 2], br, bg, bb) > HardDistance * HardDistance)
                    return;
                visited[i] = true;
                stack[sp++] = i;
            }

            for (int x = 0; x < width; x++)
            {
                TryPush(x, 0);
                TryPush(x, height - 1);
            }
            for (int y = 1; y < height - 1; y++)
            {
                TryPush(0, y);
                TryPush(width - 1, y);
            }

            while (sp > 0)
            {
                int i = stack[--sp];
                int x = i % width;
                int y = i / width;
                rgba[i * 4 + 3] = 0f;
                TryPush(x - 1, y);
                TryPush(x + 1, y);
                TryPush(x, y - 1);
                TryPush(x, y + 1);
            }
        }

        private static void SoftenAndFlatten(float[] rgba, int width, int height,
                                             float br, float bg, float bb, float bLum)
        {
            int n = width * height;
            bool darkField = bLum < DarkBackgroundLuminance;
            float hardSq = HardDistance * HardDistance;
            float softSq = SoftDistance * SoftDistance;
            float span = softSq - hardSq;
            if (span < 0.0001f) span = 0.0001f;

            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                float a = rgba[o + 3];
                if (a <= 0.001f) continue;

                float distSq = DistanceSq(rgba[o], rgba[o + 1], rgba[o + 2], br, bg, bb);
                if (distSq < softSq)
                {
                    float t = (distSq - hardSq) / span;
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;
                    a *= t;
                    rgba[o + 3] = a;
                    if (a <= 0.001f) continue;
                }

                float lum = Luminance(rgba[o], rgba[o + 1], rgba[o + 2]);
                rgba[o] = 1f;
                rgba[o + 1] = 1f;
                rgba[o + 2] = 1f;
                if (darkField)
                {
                    if (lum < 0f) lum = 0f;
                    if (lum > 1f) lum = 1f;
                    rgba[o + 3] = a * lum;
                }
            }
        }

        private static float DistanceSq(float r0, float g0, float b0, float r1, float g1, float b1)
        {
            float dr = r0 - r1;
            float dg = g0 - g1;
            float db = b0 - b1;
            return dr * dr + dg * dg + db * db;
        }

        private static float Luminance(float r, float g, float b) =>
            0.2126f * r + 0.7152f * g + 0.0722f * b;
    }
}
