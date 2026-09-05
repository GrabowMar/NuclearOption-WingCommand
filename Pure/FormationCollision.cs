using System;

namespace WingCommand
{
    internal static class FormationCollision
    {
        // Fold an inverted leader's roll continuously; clamping signed bank directly
        // jumps the slot frame from +80 to -80 when the angle wraps at 180 degrees.
        public static float SlotBank(float bankDegrees) => (float)Math.Max(-80d, Math.Min(80d,
            Math.Asin(Math.Sin(bankDegrees * Math.PI / 180d)) * 180d / Math.PI));

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
