using System;

namespace NOAvionics
{
    /// <summary>
    /// Geometry parameters for the MFD 3-column layout grid.
    /// </summary>
    public struct AvGridSpec
    {
        /// <summary>Grid snap module in pixels (default 8px).</summary>
        public float Module;

        /// <summary>Clearance from the outer canvas edges.</summary>
        public float Margin;

        /// <summary>Gap between adjacent columns.</summary>
        public float Gutter;

        /// <summary>Top reserve for mission clock and kill feed.</summary>
        public float TopReserve;

        /// <summary>Bottom reserve for spawn / Select Aircraft strip.</summary>
        public float BottomReserve;

        /// <summary>Inset holding map back from filling its column outright.</summary>
        public float MapInset;

        public static AvGridSpec Default => new AvGridSpec
        {
            Module = 8f,
            Margin = 8f,
            Gutter = 8f,
            TopReserve = 0f,
            BottomReserve = 0f,
            MapInset = 0f,
        };
    }

    /// <summary>
    /// Resolved regions for the three MFD columns and overall content box.
    /// All coordinates are in canvas-centred space (+Y up, origin at canvas centre).
    /// </summary>
    public struct AvRegions
    {
        public AvRect Panel;
        public AvRect Map;
        public AvRect Rail;
        public AvRect Content;
    }

    /// <summary>
    /// Geometry authority for the MFD layout. All returned edges are guaranteed
    /// multiples of the module, ensuring zero sub-pixel drift and no overlapping regions.
    /// </summary>
    public static class AvGrid
    {
        public static float Snap(float v, float module)
        {
            if (module <= 0f) return v;
            return (float)(Math.Round((double)v / module, MidpointRounding.AwayFromZero) * module);
        }

        public static AvRect SnapRect(AvRect r, float module)
        {
            if (module <= 0f) return r;
            float left = Snap(r.X, module);
            float right = Snap(r.Right, module);
            float top = Snap(r.Y, module);
            float bottom = Snap(r.Bottom, module);

            float width = Math.Max(0f, right - left);
            float height = Math.Max(0f, top - bottom);
            return new AvRect(left, top, width, height);
        }

        /// <summary>
        /// Resolve the 3-column layout against a canvas of size (canvasW x canvasH).
        /// </summary>
        public static AvRegions Resolve(float canvasW, float canvasH, float panelWidth, float railWidth, AvGridSpec spec)
        {
            float mod = spec.Module > 0f ? spec.Module : 8f;
            float halfW = canvasW * 0.5f;
            float halfH = canvasH * 0.5f;

            float leftBound = Snap(-halfW + spec.Margin, mod);
            float rightBound = Snap(halfW - spec.Margin, mod);
            float topBound = Snap(halfH - spec.Margin - spec.TopReserve, mod);
            float bottomBound = Snap(-halfH + spec.Margin + spec.BottomReserve, mod);

            float totalW = Math.Max(0f, rightBound - leftBound);
            float totalH = Math.Max(0f, topBound - bottomBound);
            var content = new AvRect(leftBound, topBound, totalW, totalH);

            // Panel column pinned to left
            float snappedPanelW = Snap(panelWidth, mod);
            float panelRight = Math.Min(rightBound, leftBound + snappedPanelW);
            panelRight = Snap(panelRight, mod);
            float actualPanelW = Math.Max(0f, panelRight - leftBound);
            var panel = new AvRect(leftBound, topBound, actualPanelW, totalH);

            // Rail column pinned to right
            float snappedRailW = Snap(railWidth, mod);
            float railLeft = Math.Max(panelRight, rightBound - snappedRailW);
            railLeft = Snap(railLeft, mod);
            float actualRailW = Math.Max(0f, rightBound - railLeft);
            var rail = new AvRect(railLeft, topBound, actualRailW, totalH);

            // Map column between panel and rail with gutters
            float gutter = Snap(spec.Gutter, mod);
            float mapLeft = Snap(panelRight + gutter, mod);
            float mapRight = Snap(railLeft - gutter, mod);

            float mapW = Math.Max(0f, mapRight - mapLeft);
            var map = new AvRect(mapLeft, topBound, mapW, totalH);

            // Optional map inset
            if (spec.MapInset > 0f)
            {
                float inset = Snap(spec.MapInset, mod);
                float inLeft = Snap(mapLeft + inset, mod);
                float inRight = Snap(mapRight - inset, mod);
                float inTop = Snap(topBound - inset, mod);
                float inBottom = Snap(bottomBound + inset, mod);

                float inW = Math.Max(0f, inRight - inLeft);
                float inH = Math.Max(0f, inTop - inBottom);
                map = new AvRect(inLeft, inTop, inW, inH);
            }

            return new AvRegions
            {
                Panel = SnapRect(panel, mod),
                Map = SnapRect(map, mod),
                Rail = SnapRect(rail, mod),
                Content = SnapRect(content, mod),
            };
        }
    }
}
