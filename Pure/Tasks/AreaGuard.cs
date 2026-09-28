using System;

namespace WingCommand
{
    /// <summary>CAP and SWEEP (program R8; bezel v2 A1): a task that guards an area. CAP orbits its point, SWEEP loops round the
    /// area; hostile aircraft entering the radius are engaged by the element, and while it fights its leash is the area (and a
    /// half), not the wing's anchor. The task itself flies as the orbit or patrol it is.</summary>
    internal static class AreaGuard
    {
        public const float MinRadius = 2000f, MaxRadius = 40000f, CapRadius = 8000f, SweepRadius = 12000f, MinLeash = 10000f;
        public const float LeashFactor = 1.5f;
        /// <summary>SWEEP's circuit: this many points on a circle at <see cref="SweepCircuit"/> of the radius.</summary>
        public const int SweepPoints = 6;
        public const float SweepCircuit = 0.6f;

        public static float Clamp(float radius) =>
            float.IsNaN(radius) ? CapRadius : radius < MinRadius ? MinRadius : radius > MaxRadius ? MaxRadius : radius;

        /// <summary>The area's word for the task card, or null for a task that guards nothing.</summary>
        public static string Word(WingTask t) =>
            t == null || t.GuardRadius <= 0f ? null : t.Kind == TaskKind.Patrol ? "SWEEP" : "CAP";

        /// <summary>A point inside the guarded area (horizontally: a threat above or below it is in it).</summary>
        public static bool Inside(WingTask t, Vec3 p)
        {
            if (t == null || t.GuardRadius <= 0f) return false;
            float dx = p.X - t.Center.X, dz = p.Z - t.Center.Z;
            return dx * dx + dz * dz <= t.GuardRadius * t.GuardRadius;
        }

        /// <summary>How far from the area's centre an engaged member may chase (0: the wing's own leash applies).</summary>
        public static float Leash(WingTask t) =>
            t == null || t.GuardRadius <= 0f ? 0f : Math.Max(MinLeash, LeashFactor * t.GuardRadius);

        /// <summary>SWEEP's loop round <paramref name="center"/>, clockwise from north, at the centre's height and speed.</summary>
        public static Waypoint[] Circuit(Waypoint center, float radius)
        {
            var points = new Waypoint[SweepPoints];
            float r = SweepCircuit * radius;
            for (int i = 0; i < SweepPoints; i++)
            {
                double a = 2.0 * Math.PI * i / SweepPoints;
                points[i] = new Waypoint
                {
                    X = center.X + (float)(Math.Sin(a) * r), Z = center.Z + (float)(Math.Cos(a) * r),
                    Altitude = center.Altitude, Speed = center.Speed,
                };
            }
            return points;
        }
    }
}
