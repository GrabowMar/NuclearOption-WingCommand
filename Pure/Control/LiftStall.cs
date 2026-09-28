using System;

namespace WingCommand
{
    /// <summary>The 1-g stall speed from the airframe's own wings (overnight 2026-09-28: published stall speeds can be far low — the
    /// EW-25 lists 33 m/s and stalled near 55 — and every speed and bank limit is built on it). The game's lift per part is
    /// CL(α)·½ρV²·S·effectiveness; at sea level, the equivalent airspeed the envelope works in, m·g = ½·ρ₀·V²·max ΣCL·S.</summary>
    internal static class LiftStall
    {
        public const float SeaLevelDensity = 1.225f, Gravity = 9.81f;

        /// <summary>m/s EAS for <paramref name="massKg"/> and the wings' best total lift coefficient × area (m²); 0 when unknown.</summary>
        public static float Speed(float massKg, float clsMax) =>
            massKg > 0f && clsMax > 0f ? (float)Math.Sqrt(2f * massKg * Gravity / (SeaLevelDensity * clsMax)) : 0f;
    }
}
