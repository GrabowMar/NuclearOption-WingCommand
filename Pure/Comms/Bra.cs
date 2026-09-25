using System;

namespace WingCommand
{
    /// <summary>Spec M7 §1.5: bearing, range, altitude and aspect of a target from a listener, as a radio call says them
    /// ("BRA 045, 12, 8 thousand, hot"; metric "BRA 045, 22 kilometres, 2400 metres, hot").</summary>
    internal static class Bra
    {
        public static float HotDeg = 30f, FlankDeg = 70f, BeamDeg = 110f, MinAspectSpeed = 20f;

        public static string Format(Vec3 from, Vec3 target, Vec3 targetVel, bool imperial)
        {
            Vec3 to = (target - from).Horizontal;
            int bearing = (int)Math.Round(Vec3.HeadingDeg(to)) % 360;
            float metres = to.Length;
            string range = imperial ? $"{Math.Round(metres / 1852f):0}" : $"{Math.Round(metres / 1000f):0} kilometres";
            string altitude = imperial
                ? $"{Math.Round(target.Y * 3.28084f / 1000f):0} thousand"
                : $"{Math.Round(target.Y / 100f) * 100f:0} metres";
            string aspect = Aspect(from, target, targetVel);
            return $"BRA {bearing:000}, {range}, {altitude}{(aspect.Length > 0 ? ", " + aspect : "")}";
        }

        /// <summary>The target's heading against the line back to the listener: hot, flanking, beaming or cold; empty for a
        /// target slower than <see cref="MinAspectSpeed"/>.</summary>
        public static string Aspect(Vec3 from, Vec3 target, Vec3 targetVel)
        {
            Vec3 v = targetVel.Horizontal;
            if (v.Length < MinAspectSpeed) return "";
            Vec3 back = (from - target).Horizontal;
            if (back.Length < 1f) return "";
            float cos = Vec3.Dot(v, back) / (v.Length * back.Length);
            float deg = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180.0 / Math.PI);
            return deg < HotDeg ? "hot" : deg < FlankDeg ? "flanking" : deg < BeamDeg ? "beaming" : "cold";
        }
    }
}
