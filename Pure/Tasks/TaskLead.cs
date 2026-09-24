using System;

namespace WingCommand
{
    /// <summary>The virtual anchor of a wing task (spec M4 §2.1): a kinematic aircraft the formation forms on. It flies a
    /// leg (the line from one point to the next) or an orbit round a point with coordinated turns (bank ≤
    /// <see cref="MaxBankDeg"/>, rolling at <see cref="RollRateDps"/>), climbs and descends at up to
    /// <see cref="MaxGammaDeg"/>, and changes speed at <see cref="SpeedRate"/>; a helicopter-only wing's lead can stop
    /// over a point (<see cref="FlyHover"/>: within <see cref="HoverFace"/> it slides to the point, as a helicopter moves
    /// without turning — its velocity slewing at <see cref="SpeedRate"/> toward the braking-limited velocity to the point —
    /// while it yaws to the task's heading, and climbs or descends at up to <see cref="HoverClimbRate"/>). The lateral law is a vector field: the course is the path's direction turned
    /// toward the path by atan(cross-track / <see cref="Capture"/>) (for an orbit, by atan(2·radial error / radius)), and
    /// the bank follows the course error (plus the orbit's own bank).</summary>
    internal sealed class TaskLead
    {
        public static float MaxBankDeg = 25f, OrbitBankDeg = 20f, RollRateDps = 30f, SpeedRate = 2f;
        public static float MaxGammaDeg = 5f, GammaRateDps = 2f, ClimbTau = 20f;
        /// <summary>More than this below its height, the lead may climb at up to <see cref="UrgentGammaDeg"/> (terrain rising
        /// ahead, review M4a I4).</summary>
        public static float UrgentClimbMetres = 50f, UrgentGammaDeg = 15f;
        public static float CourseGain = 1.5f, Capture = 1500f, OrbitTurnInMaxDeg = 70f;
        public static float HoverBrake = 1f, HoverStop = 5f, HoverFace = 30f, HoverYawDps = 20f, MinOrbitRadius = 300f;
        public static float HoverClimbRate = 3f, HoverClimbTau = 5f;

        public readonly bool CanHover;

        public TaskLead(Vec3 position, Vec3 velocity, bool canHover) : this(position, velocity, canHover, Vec3.Zero, 0f) { }

        /// <summary>A lead taking over from an anchor: its velocity (climb included), its nose when it barely moves, and its
        /// bank (review M4a I2).</summary>
        public TaskLead(Vec3 position, Vec3 velocity, bool canHover, Vec3 fwd, float bankDeg)
        {
            Position = position;
            Speed = velocity.Length;
            HeadingDeg = velocity.Horizontal.SqrLength > 1f ? Vec3.HeadingDeg(velocity)
                : fwd.Horizontal.SqrLength > 1e-4f ? Vec3.HeadingDeg(fwd) : 0f;
            GammaDeg = Speed > 1f ? Scalar.Clamp((float)Math.Asin(Scalar.Clamp(velocity.Y / Speed, -1f, 1f)) * Scalar.Rad2Deg, -MaxGammaDeg, MaxGammaDeg) : 0f;
            BankDeg = Scalar.Clamp(bankDeg, -MaxBankDeg, MaxBankDeg);
            CanHover = canHover;
            track = Vec3.FromHeading(HeadingDeg);
        }

        private Vec3 track, slide;
        private float climb;
        private bool sliding;

        public Vec3 Position { get; private set; }
        public float Speed { get; private set; }
        public float HeadingDeg { get; private set; }
        public float BankDeg { get; private set; }
        public float GammaDeg { get; private set; }

        public Vec3 Velocity => sliding
            ? track * Speed + Vec3.Up * climb
            : Vec3.FromHeading(HeadingDeg, Speed * Cos(GammaDeg)) + Vec3.Up * (Speed * Sin(GammaDeg));

        /// <summary>The orbit radius at <paramref name="speed"/> for <see cref="OrbitBankDeg"/> of bank.</summary>
        public static float OrbitRadius(float speed) =>
            Math.Max(MinOrbitRadius, speed * speed / (Scalar.G * (float)Math.Tan(OrbitBankDeg * Scalar.Deg2Rad)));

        public void FlyLeg(Vec3 from, Vec3 to, float altitude, float speed, float dt)
        {
            Vec3 dir = (to - from).Horizontal;
            if (dir.SqrLength < 1f) dir = (to - Position).Horizontal;
            if (dir.SqrLength < 1f) dir = Vec3.FromHeading(HeadingDeg);
            dir = dir.Normalized;
            var right = new Vec3(dir.Z, 0f, -dir.X);
            float cross = Vec3.Dot((Position - from).Horizontal, right);
            float turnIn = (float)Math.Atan(cross / Capture);
            Vec3 course = dir * (float)Math.Cos(turnIn) - right * (float)Math.Sin(turnIn);
            Steer(Vec3.HeadingDeg(course), 0f, altitude, speed, dt);
        }

        public void FlyOrbit(Vec3 centre, float radius, bool left, float altitude, float speed, float dt)
        {
            Vec3 rel = (Position - centre).Horizontal;
            float d = rel.Length;
            Vec3 radial = d > 1f ? rel * (1f / d) : Vec3.FromHeading(HeadingDeg - 90f);
            Vec3 tangent = left ? new Vec3(-radial.Z, 0f, radial.X) : new Vec3(radial.Z, 0f, -radial.X);
            float limit = OrbitTurnInMaxDeg * Scalar.Deg2Rad;
            float turnIn = Scalar.Clamp((float)Math.Atan(2f * (d - radius) / Math.Max(radius, 1f)), -limit, limit);
            Vec3 course = tangent * (float)Math.Cos(turnIn) - radial * (float)Math.Sin(turnIn);
            float vh = Math.Max(1f, Speed * Cos(GammaDeg));
            float feed = (float)Math.Atan(vh * vh / (Scalar.G * Math.Max(radius, 1f))) * Scalar.Rad2Deg;
            Steer(Vec3.HeadingDeg(course), left ? -feed : feed, altitude, speed, dt);
        }

        public void FlyHover(Vec3 point, float headingDeg, float altitude, float cruise, float dt)
        {
            Vec3 to = (point - Position).Horizontal;
            float d = to.Length;
            float speed = Math.Min(cruise, (float)Math.Sqrt(2f * HoverBrake * Math.Max(0f, d - HoverStop)));
            if (!sliding && d > HoverFace)
            {
                Steer(Vec3.HeadingDeg(to), 0f, altitude, speed, dt);
                return;
            }
            if (!sliding)
            {
                sliding = true;
                slide = Velocity.Horizontal;
            }
            // The velocity itself slews toward the braking-limited velocity to the point (review M4a C1: snapping the track
            // toward the point while only the speed slewed reversed the lead every tick when it started on the point).
            Vec3 want = d > 0.01f ? to * (speed / d) : Vec3.Zero;
            Vec3 change = want - slide;
            float most = SpeedRate * dt;
            if (change.Length > most) change = change * (most / change.Length);
            slide += change;
            Speed = slide.Length;
            if (Speed > 0.01f) track = slide * (1f / Speed);
            BankDeg = Slew.Step(BankDeg, 0f, RollRateDps, dt);
            GammaDeg = 0f;
            climb = float.IsNaN(altitude) ? 0f : Scalar.Clamp((altitude - Position.Y) / HoverClimbTau, -HoverClimbRate, HoverClimbRate);
            HeadingDeg = Wrap360(HeadingDeg + Scalar.Clamp(Scalar.Wrap180(headingDeg - HeadingDeg), -HoverYawDps * dt, HoverYawDps * dt));
            Position += Velocity * dt;
        }

        public AnchorSample Sample() => new AnchorSample
        {
            Kind = AnchorKind.Aircraft, Pos = Position, Vel = Velocity, BankDeg = BankDeg, Present = true, Airborne = true,
            IsPlayer = false, Fwd = Vec3.FromHeading(HeadingDeg), CanHover = CanHover,
        };

        private void Steer(float courseDeg, float bankFeedDeg, float altitude, float speed, float dt)
        {
            sliding = false;
            float error = Scalar.Wrap180(courseDeg - HeadingDeg);
            Speed = Slew.Step(Speed, Math.Max(CanHover ? 0f : 1f, speed), SpeedRate, dt);
            float vh = Speed * Cos(GammaDeg);
            float up = !float.IsNaN(altitude) && altitude - Position.Y > UrgentClimbMetres ? UrgentGammaDeg : MaxGammaDeg;
            float gammaCmd = float.IsNaN(altitude) ? 0f
                : Scalar.Clamp((float)Math.Atan2(altitude - Position.Y, Math.Max(vh, 1f) * ClimbTau) * Scalar.Rad2Deg,
                    -MaxGammaDeg, up);
            GammaDeg = Slew.Step(GammaDeg, gammaCmd, GammaRateDps, dt);
            if (vh > 1f)
            {
                float bankCmd = Scalar.Clamp(bankFeedDeg + CourseGain * error, -MaxBankDeg, MaxBankDeg);
                BankDeg = Slew.Step(BankDeg, bankCmd, RollRateDps, dt);
                float turn = Scalar.G * (float)Math.Tan(BankDeg * Scalar.Deg2Rad) / vh * Scalar.Rad2Deg;
                HeadingDeg = Wrap360(HeadingDeg + turn * dt);
            }
            else
            {
                BankDeg = Slew.Step(BankDeg, 0f, RollRateDps, dt);
                HeadingDeg = Wrap360(HeadingDeg + Scalar.Clamp(error, -HoverYawDps * dt, HoverYawDps * dt));
            }
            Position += Velocity * dt;
        }

        private static float Cos(float deg) => (float)Math.Cos(deg * Scalar.Deg2Rad);
        private static float Sin(float deg) => (float)Math.Sin(deg * Scalar.Deg2Rad);
        private static float Wrap360(float deg) => deg < 0f ? deg + 360f : deg >= 360f ? deg - 360f : deg;
    }
}
