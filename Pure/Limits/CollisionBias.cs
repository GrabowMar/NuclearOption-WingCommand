using System;

namespace WingCommand
{
    /// <summary>One aircraft as the wing-scope collision check sees it.</summary>
    internal struct CollisionBody
    {
        public Vec3 Pos, Vel;
        public float Radius;
        /// <summary>0 = the leader; a higher rank yields to a lower one.</summary>
        public int Rank;
    }

    /// <summary>Wing-scope collision avoidance as a bias, never a mode.
    /// <list type="bullet">
    /// <item>For each pair whose closest approach within 8 s falls inside R = max(2·r_max + 15, 0.35·spacing),
    /// the higher-ranked aircraft gets up to 0.5 g away from the other, scaled by penetration: it ramps in from
    /// R and is at full strength by the safe radius 2·r_max + 10 m.</item>
    /// <item>Inside 1.2·(r_a + r_b) within 2 s the bias may reach 2 g.</item>
    /// <item>A 0.4 s low-pass makes it fade deterministically.</item>
    /// </list>
    /// The bias is added to slot steering and never replaces it. Pairs are checked once per wing per tick,
    /// at most 28 for eight aircraft.</summary>
    internal sealed class CollisionBias
    {
        public const float Horizon = 8f, EmergencyHorizon = 2f, BiasG = 0.5f, EmergencyG = 2f, FilterTau = 0.4f;
        public const float Margin = 15f, SafeMargin = 10f, SpacingFraction = 0.35f;

        private readonly Vec3[] raw;
        public readonly Vec3[] Bias;
        public readonly bool[] Emergency;

        public CollisionBias(int capacity)
        {
            raw = new Vec3[capacity];
            Bias = new Vec3[capacity];
            Emergency = new bool[capacity];
        }

        /// <summary>Bias radius R = max(2·r_max + 15, 0.35·spacing) for a pair whose larger radius is
        /// <paramref name="largestRadius"/>.</summary>
        public static float RadiusFor(float largestRadius, float spacing) =>
            Math.Max(2f * largestRadius + Margin, SpacingFraction * spacing);

        public void Update(CollisionBody[] bodies, int count, float spacing, float dt)
        {
            for (int i = 0; i < count; i++)
            {
                raw[i] = Vec3.Zero;
                Emergency[i] = false;
            }
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                {
                    int yielder = bodies[b].Rank >= bodies[a].Rank ? b : a, other = yielder == b ? a : b;
                    Vec3 p = bodies[yielder].Pos - bodies[other].Pos, v = bodies[yielder].Vel - bodies[other].Vel;
                    float vv = v.SqrLength;
                    float t = vv > 1e-3f ? Scalar.Clamp(-Vec3.Dot(p, v) / vv, 0f, Horizon) : 0f;
                    Vec3 miss = p + v * t;
                    float distance = miss.Length;
                    float largest = Math.Max(bodies[a].Radius, bodies[b].Radius);
                    float radius = RadiusFor(largest, spacing);
                    if (distance >= radius) continue;
                    Vec3 away = distance > 0.5f ? miss / distance : Sideways(bodies[yielder].Vel);
                    // Full strength by the safe radius (bounding spheres + 10 m), ramping in from the bias radius.
                    float safe = 2f * largest + SafeMargin;
                    float g = BiasG * Scalar.Clamp01((radius - distance) / Math.Max(1f, radius - safe));
                    float hard = 1.2f * (bodies[a].Radius + bodies[b].Radius);
                    if (t < EmergencyHorizon && distance < hard)
                    {
                        g = Math.Max(g, EmergencyG * (1f - distance / hard));
                        Emergency[yielder] = true;
                    }
                    raw[yielder] += away * (g * Scalar.G);
                }
            float k = dt > 0f ? 1f - (float)Math.Exp(-dt / FilterTau) : 0f;
            for (int i = 0; i < count; i++)
            {
                Vec3 target = raw[i];
                float limit = (Emergency[i] ? EmergencyG : BiasG) * Scalar.G, length = target.Length;
                if (length > limit) target *= limit / length;
                Bias[i] += (target - Bias[i]) * k;
            }
        }

        private static Vec3 Sideways(Vec3 velocity)
        {
            Vec3 side = Vec3.Cross(Vec3.Up, velocity).Normalized;
            return side.SqrLength > 0.5f ? side : Vec3.Right;
        }
    }
}
