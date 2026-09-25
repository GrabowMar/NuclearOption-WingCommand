using System;

namespace WingCommand
{
    /// <summary>The room map's projection (spec WMC program §6 TACTICAL): world x/z (metres) to view pixels with the origin at
    /// the view's centre and +y north; zoom about a point, pan, fit, all clamped to the theatre. Degenerate sizes keep a
    /// finite, harmless view.</summary>
    internal sealed class MapView
    {
        public const float MinMetresPerPixel = 5f;
        public float CentreX { get; private set; }
        public float CentreZ { get; private set; }
        public float MetresPerPixel { get; private set; } = 100f;
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float MapHalf { get; private set; } = 50000f;

        public bool Valid => Width > 0f && Height > 0f && MapHalf > 0f;

        public void Resize(float width, float height)
        {
            Width = Finite(width) && width > 0f ? width : 0f;
            Height = Finite(height) && height > 0f ? height : 0f;
            Clamp();
        }

        public void SetMapHalf(float half)
        {
            if (Finite(half) && half > 0f) MapHalf = half;
            Clamp();
        }

        /// <summary>The widest view: the whole theatre fits.</summary>
        private float MaxMetresPerPixel => Valid ? Math.Max(MinMetresPerPixel, 2f * MapHalf / Math.Min(Width, Height)) : 100f;

        public void FitMap()
        {
            CentreX = CentreZ = 0f;
            MetresPerPixel = MaxMetresPerPixel;
            Clamp();
        }

        public void Fit(float[] xs, float[] zs, int count, float minSpanMetres, float padPixels)
        {
            if (count <= 0 || xs == null || zs == null || !Valid) return;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (!Finite(xs[i]) || !Finite(zs[i])) continue;
                x0 = Math.Min(x0, xs[i]);
                x1 = Math.Max(x1, xs[i]);
                z0 = Math.Min(z0, zs[i]);
                z1 = Math.Max(z1, zs[i]);
            }
            if (x0 > x1) return;
            CentreX = (x0 + x1) * 0.5f;
            CentreZ = (z0 + z1) * 0.5f;
            float spanX = Math.Max(x1 - x0, minSpanMetres), spanZ = Math.Max(z1 - z0, minSpanMetres);
            float w = Math.Max(1f, Width - 2f * padPixels), h = Math.Max(1f, Height - 2f * padPixels);
            MetresPerPixel = Math.Max(spanX / w, spanZ / h);
            Clamp();
        }

        public void Project(float x, float z, out float px, out float py)
        {
            px = (x - CentreX) / MetresPerPixel;
            py = (z - CentreZ) / MetresPerPixel;
        }

        public void Unproject(float px, float py, out float x, out float z)
        {
            x = CentreX + (Finite(px) ? px : 0f) * MetresPerPixel;
            z = CentreZ + (Finite(py) ? py : 0f) * MetresPerPixel;
        }

        /// <summary>Zoom by <paramref name="factor"/> (> 1 closer) keeping the world point under (px, py) there.</summary>
        public void ZoomAt(float px, float py, float factor)
        {
            if (!Finite(px) || !Finite(py) || !Finite(factor) || factor <= 0f) return;
            Unproject(px, py, out float wx, out float wz);
            MetresPerPixel = Math.Min(MaxMetresPerPixel, Math.Max(MinMetresPerPixel, MetresPerPixel / factor));
            CentreX = wx - px * MetresPerPixel;
            CentreZ = wz - py * MetresPerPixel;
            Clamp();
        }

        /// <summary>The map follows a drag of (dx, dy) pixels.</summary>
        public void PanBy(float dxPixels, float dyPixels)
        {
            if (!Finite(dxPixels) || !Finite(dyPixels)) return;
            CentreX -= dxPixels * MetresPerPixel;
            CentreZ -= dyPixels * MetresPerPixel;
            Clamp();
        }

        private void Clamp()
        {
            if (!Finite(MetresPerPixel) || MetresPerPixel <= 0f) MetresPerPixel = MaxMetresPerPixel;
            MetresPerPixel = Math.Min(MaxMetresPerPixel, Math.Max(MinMetresPerPixel, MetresPerPixel));
            if (!Finite(CentreX)) CentreX = 0f;
            if (!Finite(CentreZ)) CentreZ = 0f;
            CentreX = Math.Max(-MapHalf, Math.Min(MapHalf, CentreX));
            CentreZ = Math.Max(-MapHalf, Math.Min(MapHalf, CentreZ));
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
