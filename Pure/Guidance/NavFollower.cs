namespace WingCommand
{
    /// <summary>NAV for the player's own autopilot (spec WMC rebuild §ROUTE: "your autopilot flies the route you drew"): a
    /// heading hold steered to the active point, which is captured within <see cref="CaptureRadius"/> and then the next one's
    /// turn; a point's altitude (never below the terrain floor plus <see cref="Clearance"/>) and speed (never below the safe
    /// minimum) become the held targets when it sets them. After the last point the autopilot keeps the last leg's heading.</summary>
    internal sealed class NavFollower
    {
        public static float MinCapture = 600f, LeadSeconds = 12f, Clearance = 150f;

        private Waypoint[] points = new Waypoint[0];
        private Vec3 legFrom;
        private bool haveFrom;

        public int Count { get; private set; }
        public int Index { get; private set; }
        public bool Active { get; private set; }

        public static float CaptureRadius(float speed) => System.Math.Max(MinCapture, speed * LeadSeconds);

        public void Load(Waypoint[] route, int count)
        {
            int n = route == null ? 0 : System.Math.Min(count, route.Length);
            if (points.Length < n) points = new Waypoint[n];
            for (int i = 0; i < n; i++) points[i] = route[i];
            Count = n;
            Index = 0;
            Active = n > 0;
            haveFrom = false;
        }

        public bool Step(Vec3 position, float speed, ref HoldSpec spec) => Step(position, speed, float.NaN, 0f, ref spec);

        /// <summary>Steers <paramref name="spec"/> for this tick; false once the route is flown (or none is loaded).
        /// <paramref name="floorY"/> is the terrain ahead (NaN unknown); <paramref name="minSpeed"/> the slowest safe speed.</summary>
        public bool Step(Vec3 position, float speed, float floorY, float minSpeed, ref HoldSpec spec)
        {
            if (!Active) return false;
            if (!haveFrom)
            {
                legFrom = new Vec3(position.X, 0f, position.Z);
                haveFrom = true;
            }
            Aim(position, floorY, minSpeed, ref spec);
            Waypoint p = points[Index];
            var point = new Vec3(p.X, 0f, p.Z);
            if (!Reached(position, point, CaptureRadius(speed))) return true;
            Vec3 leg = point - legFrom;
            legFrom = point;
            Index++;
            if (Index >= Count)
            {
                // The last leg's bearing, not the bearing from wherever the capture happened (review R2).
                if (leg.Horizontal.SqrLength > 1f) spec.HeadingDeg = Vec3.HeadingDeg(leg);
                Active = false;
                spec.Lateral = LateralHold.Heading;
                return false;
            }
            Aim(position, floorY, minSpeed, ref spec);
            return true;
        }

        /// <summary>Within the radius, or past the point along the leg: a point inside the 30-degree turn circle is never flown
        /// over, and pure pursuit would orbit it forever (review R2 I1; the planner's own rule, WingPlanner.Reached).</summary>
        private bool Reached(Vec3 position, Vec3 point, float radius)
        {
            Vec3 at = new Vec3(position.X, 0f, position.Z);
            if ((point - at).SqrLength <= radius * radius) return true;
            Vec3 leg = point - legFrom;
            float length = leg.Length;
            return length > 1f && Vec3.Dot(at - legFrom, leg * (1f / length)) >= length;
        }

        private void Aim(Vec3 position, float floorY, float minSpeed, ref HoldSpec spec)
        {
            Waypoint p = points[Index];
            spec.HeadingDeg = Vec3.HeadingDeg(new Vec3(p.X - position.X, 0f, p.Z - position.Z));
            if (!float.IsNaN(p.Altitude))
                spec.AltitudeM = float.IsNaN(floorY) ? p.Altitude : System.Math.Max(p.Altitude, floorY + Clearance);
            if (!float.IsNaN(p.Speed)) spec.SpeedMps = System.Math.Max(p.Speed, minSpeed);
        }
    }
}
