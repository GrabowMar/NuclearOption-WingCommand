using System;

namespace WingCommand
{
    /// <summary>TACTICAL › FORMATION's MANEUVER row, in its order (spec WMC rebuild R3). Not <see cref="ManeuverKind"/>: those
    /// are the dormant scripted-stick recipes.</summary>
    internal enum ReactionKind : byte { BreakLeft, BreakRight, PullUp, Split, Beam }

    /// <summary>One member's maneuver order: SPLIT's side (+1 right, −1 left) and BEAM's threat position.</summary>
    internal struct ReactionOrder
    {
        public ReactionKind Kind;
        public int Side;
        public bool HasThreat;
        public Vec3 Threat;
    }

    /// <summary>A reaction flown through the member's own pipeline (design wmc-rebuild/maneuver.md §2): a reference velocity
    /// along a direction fixed at the start, at the speed held (never below home's), and an altitude — so the terrain floor,
    /// GCAS, the envelope and the collision bias all still apply. No sticks are scripted.
    /// <list type="bullet">
    /// <item>BRK L / BRK R: 90° off the track, level.</item>
    /// <item>SPLIT: 60° off to its side, level (ponytail: no vertical split — the floor makes one unreliable low down).</item>
    /// <item>PULL UP: straight on, climbing <see cref="PullUpHeight"/>.</item>
    /// <item>BEAM: perpendicular to the threat's line of sight, on the side nearer the heading (biased toward home);
    /// ponytail: the bearing is fixed at the start (no refresh) and comes from the faction's tracks only.</item>
    /// </list></summary>
    internal sealed class ReactionManeuver
    {
        public static float BreakSeconds = 8f, SplitSeconds = 8f, PullUpSeconds = 10f, BeamSeconds = 20f, MaxSeconds = 120f;
        /// <summary>A new maneuver waits this long into the current one.</summary>
        public static float MinDwell = 4f;
        public static float PullUpHeight = 500f, PullUpDone = 50f, SplitAngleDeg = 60f;
        /// <summary>Flown at full effort (the bank ceiling's top).</summary>
        public static float Aggression = 1f;

        private Vec3 goal;
        private float goalY, duration;

        public bool Active { get; private set; }
        /// <summary>The last maneuver begun (kept after it ends: the radio line reads it).</summary>
        public ReactionKind Kind { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>Null when a member may fly <paramref name="kind"/> now; else why not. Height gates are the catalog's
        /// (breaks 120 m, a notch 80 m; a pull-up climbs, so none); the speed must clear both the catalog's fraction of the
        /// top speed and the speed-protect floor, or the pull-up's climb is clamped to nothing.</summary>
        public static string Refusal(ReactionKind kind, in AircraftState s, float agl, AirframeProfile p, bool hasThreat)
        {
            if (kind == ReactionKind.Beam && !hasThreat) return "no threat to beam";
            float gate = kind == ReactionKind.PullUp ? 0f
                : ManeuverCatalog.MinEntryAltitudeAgl(kind == ReactionKind.Beam ? ManeuverKind.NotchThreat : ManeuverKind.BreakLeft);
            if (agl < gate) return "too low (below " + ((int)gate).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m)";
            if (s.Tas < ManeuverCatalog.MinEntrySpeedFraction(ManeuverKind.BreakLeft) * p.MaxSpeed ||
                s.Eas < ConstraintChain.SpeedProtectFactor * p.MinimumSpeed(1f)) return "too slow";
            return null;
        }

        public void Begin(in ReactionOrder o, in AircraftState s, in RefState home)
        {
            Vec3 h = s.Vel.Horizontal;
            if (h.SqrLength < 1f) h = s.Fwd.Horizontal;
            h = h.SqrLength > 1e-6f ? h.Normalized : Vec3.Forward;
            Vec3 right = Vec3.Cross(Vec3.Up, h);
            goalY = s.Pos.Y;
            switch (o.Kind)
            {
                case ReactionKind.BreakLeft:
                    goal = -right;
                    duration = BreakSeconds;
                    break;
                case ReactionKind.BreakRight:
                    goal = right;
                    duration = BreakSeconds;
                    break;
                case ReactionKind.Split:
                {
                    double a = SplitAngleDeg * Math.PI / 180.0;
                    goal = (h * (float)Math.Cos(a) + right * ((o.Side >= 0 ? 1f : -1f) * (float)Math.Sin(a))).Normalized;
                    duration = SplitSeconds;
                    break;
                }
                case ReactionKind.PullUp:
                    goal = h;
                    goalY = s.Pos.Y + PullUpHeight;
                    duration = PullUpSeconds;
                    break;
                default:
                {
                    Vec3 source = s.Pos - o.Threat, toHome = home.Pos - s.Pos;
                    (float x, float z, int _) = RadarDefenceGeometry.Notch(source.X, source.Z, h.X, h.Z, toHome.X, toHome.Z, true, 0);
                    goal = new Vec3(x, 0f, z);
                    goal = goal.SqrLength > 1e-6f ? goal.Normalized : h;
                    duration = BeamSeconds;
                    break;
                }
            }
            duration = Math.Min(duration, MaxSeconds);
            Kind = o.Kind;
            Elapsed = 0f;
            Active = true;
        }

        /// <summary>This tick's reference; <paramref name="done"/> once its time is up (a pull-up also within
        /// <see cref="PullUpDone"/> of its height).</summary>
        public RefState Step(in AircraftState s, in RefState home, float dt, out bool done)
        {
            Elapsed += dt;
            float speed = Math.Max(home.Vel.Length, s.Vel.Length);
            done = !Active || Elapsed >= duration || (Kind == ReactionKind.PullUp && s.Pos.Y >= goalY - PullUpDone);
            return new RefState(new Vec3(s.Pos.X, goalY, s.Pos.Z), goal * speed, Vec3.Zero);
        }

        public void End() => Active = false;

        public static string Label(ReactionKind k)
        {
            switch (k)
            {
                case ReactionKind.BreakLeft: return "BRK L";
                case ReactionKind.BreakRight: return "BRK R";
                case ReactionKind.PullUp: return "PULL UP";
                case ReactionKind.Split: return "SPLIT";
                default: return "BEAM";
            }
        }

        public static string Words(ReactionKind k)
        {
            switch (k)
            {
                case ReactionKind.BreakLeft: return "breaking left";
                case ReactionKind.BreakRight: return "breaking right";
                case ReactionKind.PullUp: return "pulling up";
                case ReactionKind.Split: return "splitting";
                default: return "beaming";
            }
        }

        /// <summary>SPLIT's side for one member: away from the middle of those ordered (a pair turns apart), else away from
        /// the lead, across the lead's track; straight in line, by seat parity.</summary>
        public static int SplitSide(Vec3 pos, Vec3 centroid, bool haveCentroid, Vec3 leaderPos, Vec3 leaderTrack, int seat)
        {
            Vec3 t = leaderTrack.Horizontal;
            t = t.SqrLength > 1e-6f ? t.Normalized : Vec3.Forward;
            Vec3 right = Vec3.Cross(Vec3.Up, t);
            if (haveCentroid)
            {
                float d = Vec3.Dot(pos - centroid, right);
                if (Math.Abs(d) > 1f) return d > 0f ? 1 : -1;
            }
            float l = Vec3.Dot(pos - leaderPos, right);
            if (Math.Abs(l) > 1f) return l > 0f ? 1 : -1;
            return seat % 2 == 0 ? 1 : -1;
        }
    }
}
