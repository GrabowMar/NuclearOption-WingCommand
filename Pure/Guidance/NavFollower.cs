namespace WingCommand
{
    /// <summary>NAV for the player's own autopilot (spec WMC rebuild §ROUTE: "your autopilot flies the route you drew"): a
    /// heading hold steered to the active point, which is captured within <see cref="CaptureRadius"/> and then the next one's
    /// turn; a point's altitude and speed become the held targets when it sets them. After the last point the autopilot keeps
    /// the last leg's heading. ponytail: no terrain floor (the ALT hold has none either); a low point is the pilot's call.</summary>
    internal sealed class NavFollower
    {
        public static float MinCapture = 600f, LeadSeconds = 12f;

        private Waypoint[] points = new Waypoint[0];

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
        }

        /// <summary>Steers <paramref name="spec"/> for this tick; false once the route is flown (or none is loaded).</summary>
        public bool Step(Vec3 position, float speed, ref HoldSpec spec)
        {
            if (!Active) return false;
            Aim(position, ref spec);
            Waypoint p = points[Index];
            float dx = p.X - position.X, dz = p.Z - position.Z;
            float r = CaptureRadius(speed);
            if (dx * dx + dz * dz > r * r) return true;
            Index++;
            if (Index >= Count)
            {
                Active = false;
                spec.Lateral = LateralHold.Heading;
                return false;
            }
            Aim(position, ref spec);
            return true;
        }

        private void Aim(Vec3 position, ref HoldSpec spec)
        {
            Waypoint p = points[Index];
            spec.HeadingDeg = Vec3.HeadingDeg(new Vec3(p.X - position.X, 0f, p.Z - position.Z));
            if (!float.IsNaN(p.Altitude)) spec.AltitudeM = p.Altitude;
            if (!float.IsNaN(p.Speed)) spec.SpeedMps = p.Speed;
        }
    }
}
