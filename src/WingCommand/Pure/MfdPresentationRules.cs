using System;

namespace WingCommand
{
    internal static class MfdPresentationRules
    {
        internal readonly struct Placement
        {
            public readonly float X, Top, Scale;
            public Placement(float x, float top, float scale)
            {
                X = x;
                Top = top;
                Scale = scale;
            }
        }

        public static Placement FitBesideBezel(float width, float height, bool left,
            float viewportLeft, float viewportRight, float viewportBottom, float viewportTop,
            float bezelLeft, float bezelRight, float gap, float? centerY = null)
        {
            float min = left ? viewportLeft : Math.Max(viewportLeft, bezelRight + gap);
            float max = left ? Math.Min(viewportRight, bezelLeft - gap) : viewportRight;
            float center = centerY ?? (viewportTop + viewportBottom) * 0.5f;
            // Unequal top/bottom reserves limit clearance; they must not move the
            // visual center away from the map. Fit symmetrically around that center.
            float availableHeight = 2f * Math.Min(viewportTop - center, center - viewportBottom);
            float scale = FitScale(width, height, max - min, availableHeight);
            // Seat the panel beside its button column, not at a prefab's off-screen origin.
            float top = center + height * scale * 0.5f;
            return new Placement(left ? max - width * scale : min, top, scale);
        }

        public static bool UseExpanded(bool boscaliLoaded, bool fitMapToPanels, bool mfdAvailable) =>
            boscaliLoaded && fitMapToPanels && mfdAvailable;

        // A missing/invalid prefab measurement must defer installation, never produce a
        // zero-size (or infinitely large) screen that still intercepts map input.
        public static float FitScale(float width, float height, float availableWidth, float availableHeight)
        {
            if (!PositiveFinite(width) || !PositiveFinite(height) ||
                !PositiveFinite(availableWidth) || !PositiveFinite(availableHeight)) return 0f;
            return Math.Min(1f, Math.Min(availableWidth / width, availableHeight / height));
        }

        private static bool PositiveFinite(float value) =>
            value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
