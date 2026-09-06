using System;

namespace WingCommand
{
    internal static class FormationCollision
    {
        // Fold an inverted leader's roll continuously; clamping signed bank directly
        // jumps the slot frame from +80 to -80 when the angle wraps at 180 degrees.
        public static float SlotBank(float bankDegrees) => (float)Math.Max(-80d, Math.Min(80d,
            Math.Asin(Math.Sin(bankDegrees * Math.PI / 180d)) * 180d / Math.PI));

        /// <summary>
        /// Limit the whole formation's roll before a terrain floor flattens its low
        /// slots. Reserve the existing step-down and the altitude used by aft slots
        /// in a climb, then spend only the remaining clearance on lateral roll.
        /// Extents describe every member's local slot, including shape/turn transitions.
        /// </summary>
        public static float TerrainBank(float requestedBank, float leaderRadarAltitude,
            float terrainClearance, float lateralExtent, float downwardStack, float aftExtent,
            float trackVertical = 0f)
        {
            float requested = SlotBank(requestedBank);
            double vertical = Math.Max(-1d, Math.Min(1d, trackVertical));
            double horizontal = Math.Sqrt(Math.Max(0d, 1d - vertical * vertical));
            double clearance = Math.Max(0f, leaderRadarAltitude - Math.Max(0f, terrainClearance));
            double reserve = Math.Max(0f, downwardStack) * horizontal +
                Math.Max(0f, aftExtent) * Math.Max(0d, vertical);
            // An already-clamped stack needs level lateral lanes, not additional roll.
            if (clearance <= reserve) return 0f;
            double arm = Math.Max(0f, lateralExtent) * horizontal;
            if (arm < 0.001d) return requested;
            // Keeping the full stack reserve is conservative: bank's cosine would
            // otherwise reduce that downward component as the formation rolls.
            double limit = Math.Asin(Math.Min(1d, (clearance - reserve) / arm)) * 180d / Math.PI;
            return (float)(Math.Sign(requested) * Math.Min(Math.Abs(requested), limit));
        }

        public static float Threat(float px, float py, float pz, float vx, float vy, float vz,
            float radius, out float time, out float miss)
        {
            float speedSquared = vx * vx + vy * vy + vz * vz;
            time = speedSquared > 1f ? Math.Max(0f, Math.Min(WingTuning.CollisionHorizon,
                -(px * vx + py * vy + pz * vz) / speedSquared)) : 0f;
            float x = px + vx * time, y = py + vy * time, z = pz + vz * time;
            miss = (float)Math.Sqrt(x * x + y * y + z * z);
            if (radius <= 0f || miss >= radius) return 0f;
            return (1f - miss / radius) * (1f + 1f / (1f + time));
        }

        // Near the slot, HOLD improves correction and damping together. Distant
        // intercepts retain their existing gains and closure limits.
        public static float HoldBlend(bool hold, float distance, float spacing) =>
            hold ? Math.Max(0f, Math.Min(1f, 1f - distance / Math.Max(1f, spacing * 3f))) : 0f;
    }
}
