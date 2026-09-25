using System;

namespace WingCommand
{
    /// <summary>FORMATION's plan view (spec WMC rebuild §FORMATION; the 0.9 preview): the leader a third down the middle of a
    /// square, slots right and aft of it at the shape's spacing, scaled so the farthest slot fits inside a 12 px margin; live
    /// positions beyond the square sit on its edge.</summary>
    internal static class PlanView
    {
        public const float Margin = 12f, Edge = 4f;

        /// <summary>Metres per pixel that fit every slot of <paramref name="slots"/> at <paramref name="spacing"/> metres into a
        /// <paramref name="size"/> px square; a small shape is not blown up past 40 px a spacing.</summary>
        public static void Fit(SlotDef[] slots, float spacing, float size, out float metresPerPixel)
        {
            float right = 0f, aft = 0f, ahead = 0f;
            if (slots != null)
                foreach (SlotDef s in slots)
                {
                    right = Math.Max(right, Math.Abs(s.Right * spacing));
                    if (s.Aft >= 0f) aft = Math.Max(aft, s.Aft * spacing);
                    else ahead = Math.Max(ahead, -s.Aft * spacing);
                }
            float half = size * 0.5f - Margin, below = size * 2f / 3f - Margin, above = size / 3f - Margin;
            metresPerPixel = Math.Max(Math.Max(right / half, aft / below), Math.Max(ahead / above, spacing / 40f));
            if (!(metresPerPixel > 0f)) metresPerPixel = 1f;
        }

        /// <summary>Pixels from the square's top-left (y down) of a point <paramref name="rightM"/> right and
        /// <paramref name="aftM"/> aft of the leader, clamped inside the square.</summary>
        public static (float X, float Y) Point(float rightM, float aftM, float metresPerPixel, float size)
        {
            float x = size * 0.5f + rightM / metresPerPixel, y = size / 3f + aftM / metresPerPixel;
            return (Clamp(x, size), Clamp(y, size));
        }

        private static float Clamp(float v, float size) => v < Edge ? Edge : v > size - Edge ? size - Edge : v;
    }
}
