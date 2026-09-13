using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal static class AceIngress
    {
        internal readonly struct Base
        {
            internal readonly float X, Z;
            internal readonly bool Enemy;
            internal Base(float x, float z, bool enemy) { X = x; Z = z; Enemy = enemy; }
        }
        internal static string Airframe(int tier) => tier == 1 ? "T/A-30" : tier == 2 ? "CT-7" :
            tier == 3 ? "FS-12" : tier == 4 ? "FS-20" : tier == 5 ? "KR-67" : null;

        // Fixed perimeter samples, 2 km inset for formation clearance. Ownership is the
        // nearest live ground airbase, with a 2 km buffer against other factions/neutral bases.
        internal static bool TryPick(float width, float height, float playerX, float playerZ,
            IReadOnlyList<Base> bases, int seed, out float x, out float z)
        {
            x = z = 0;
            if (float.IsNaN(width) || float.IsInfinity(width) || float.IsNaN(height) ||
                float.IsInfinity(height) || width < 8000 || height < 8000 || bases == null ||
                bases.Count == 0 || bases.Count > 256) return false;
            float halfX = width * 0.5f - 2000, halfZ = height * 0.5f - 2000;
            int start = (int)((uint)seed % 64);
            for (int n = 0; n < 64; n++)
            {
                int index = (start + n) % 64;
                float t = (index % 16 + 0.5f) / 16f;
                float cx = index / 16 < 2 ? (index / 16 == 0 ? -halfX : halfX) : -halfX + 2 * halfX * t;
                float cz = index / 16 >= 2 ? (index / 16 == 2 ? -halfZ : halfZ) : -halfZ + 2 * halfZ * t;
                if (Distance(cx, cz, playerX, playerZ) < 9000) continue;
                float enemy = float.MaxValue, other = float.MaxValue;
                foreach (Base b in bases)
                {
                    float distance = Distance(cx, cz, b.X, b.Z);
                    if (b.Enemy) enemy = Math.Min(enemy, distance);
                    else other = Math.Min(other, distance);
                }
                if (enemy == float.MaxValue || enemy + 2000 >= other) continue;
                x = cx; z = cz; return true;
            }
            return false;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }
    }
}
