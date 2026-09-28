using System;

namespace WingCommand
{
    /// <summary>Points to frame on the game's map (spec bezel v2 §6 FIT): a running box over world X/Z.</summary>
    internal struct MapBox
    {
        private float minX, maxX, minZ, maxZ;

        public bool Any { get; private set; }

        public void Add(float x, float z)
        {
            if (float.IsNaN(x) || float.IsNaN(z)) return;
            if (!Any)
            {
                minX = maxX = x;
                minZ = maxZ = z;
                Any = true;
                return;
            }
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        public float CenterX => (minX + maxX) * 0.5f;
        public float CenterZ => (minZ + maxZ) * 0.5f;

        /// <summary>The longer side, in metres.</summary>
        public float Span => Math.Max(maxX - minX, maxZ - minZ);
    }

    /// <summary>FIT and CENTER on the game's maximized map (spec bezel v2 §6): the zoom that shows a span with a margin, never
    /// under 8 km, inside the game's 1–40; and the view offset that puts a point at the view's centre. The game draws the map at
    /// −(stationary + offset) in map units (world × mapDisplayFactor), so an offset that is not zero also stops it following.</summary>
    internal static class MapFit
    {
        public const float Margin = 0.15f, MinSpan = 8000f, MinZoom = 1f, MaxZoom = 40f;

        public static float Zoom(float spanM, float visiblePx, float pxPerMetreAtZoom1)
        {
            if (float.IsNaN(spanM) || visiblePx <= 0f || pxPerMetreAtZoom1 <= 0f || float.IsNaN(pxPerMetreAtZoom1)) return MinZoom;
            float span = Math.Max(spanM, MinSpan) * (1f + Margin);
            float z = visiblePx / (span * pxPerMetreAtZoom1);
            return z < MinZoom ? MinZoom : z > MaxZoom ? MaxZoom : z;
        }

        /// <summary>The map's view offset that centres world point (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public static (float X, float Y) Offset(float x, float z, float displayFactor, float stationaryX, float stationaryY) =>
            (x * displayFactor - stationaryX, z * displayFactor - stationaryY);
    }
}
