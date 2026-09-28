using System;

namespace WingCommand
{
    /// <summary>"Clear my six" (plan §Tasking and combat, the DCS/BMS pack): an aircraft in the leader's rear quarter —
    /// more than <see cref="RearAngleDeg"/> off its nose, horizontally — and within range.</summary>
    internal static class ClearSix
    {
        public static float RearAngleDeg = 110f, RangeMetres = 6000f;

        public static bool OnSix(Vec3 toTarget, Vec3 leaderFwd, float range)
        {
            Vec3 h = toTarget.Horizontal, f = leaderFwd.Horizontal;
            if (h.SqrLength > range * range || h.SqrLength < 1f || f.SqrLength < 1e-6f) return false;
            return Vec3.Dot(h.Normalized, f.Normalized) < (float)Math.Cos(RearAngleDeg * Math.PI / 180.0);
        }
    }
}
