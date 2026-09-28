namespace WingCommand
{
    /// <summary>Same-direction orbit lanes separated by roster slot radius.</summary>
    internal static class OrbitGeometry
    {
        public static (float x, float z) AimOffset(float fromX, float fromZ,
                                                   float radius, int slot, float spacing)
        {
            if (fromX * fromX + fromZ * fromZ < 1f) { fromX = 0f; fromZ = 1f; }
            const double lookahead = 70d * System.Math.PI / 180d;
            double bearing = System.Math.Atan2(fromZ, fromX) + lookahead;
            float laneRadius = System.Math.Max(200f, radius) +
                               System.Math.Max(0, slot - 1) * System.Math.Max(0f, spacing);
            // Project the aim tangent onto the desired radius; aiming directly on the circle would cut
            // inward.
            double aimRadius = laneRadius / System.Math.Cos(lookahead);
            return ((float)(System.Math.Cos(bearing) * aimRadius),
                    (float)(System.Math.Sin(bearing) * aimRadius));
        }
    }

    /// <summary>Convert world slot height to rotary AGL hold because terrain-following ignores destination
    /// height; preserve terrain clearance for low slots.</summary>
    internal static class RotaryAltitudePolicy
    {
        public static float SlotAgl(float ownAltitude, float ownRadarAltitude,
                                    float slotAltitude, float terrainClearance)
        {
            float groundAltitude = ownAltitude - System.Math.Max(0f, ownRadarAltitude);
            return System.Math.Max(terrainClearance, slotAltitude - groundAltitude);
        }
    }

    /// <summary>Restart timeout whenever a decreasing progress value advances.</summary>
    internal sealed class CargoProgressTracker
    {
        public int LastAmount { get; private set; }
        public float LastProgressAt { get; private set; }
        public bool MadeProgress { get; private set; }

        public void Reset(int amount, float now)
        {
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = false;
        }

        public bool Observe(int amount, float now)
        {
            if (amount >= LastAmount) return false;
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = true;
            return true;
        }

        public bool IsStalled(float now, float timeout) => now - LastProgressAt >= timeout;
    }
}
