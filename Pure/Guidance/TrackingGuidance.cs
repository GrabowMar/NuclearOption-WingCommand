using System;

namespace WingCommand
{
    /// <summary>Position → velocity → acceleration cascade toward a moving reference. One law at every
    /// distance: per-axis saturation turns a far error into pursuit at catch-up speed and a near error into
    /// a linear PD with the reference acceleration as feedforward. The along-track closure also respects a
    /// stopping-distance law, so arrival does not overshoot. The acceleration is clamped to what the
    /// airframe can do: normal part to (Nz_max − 1)·g, tangential part to thrust and drag. A velocity
    /// command far off the current track becomes a maximum turn toward the side it lies on that holds the
    /// commanded speed (blended in from ~45°, continuous through 90° and beyond).</summary>
    internal static class TrackingGuidance
    {
        public const float CatchUpMargin = 5f;
        public const float CatchUpCap = 80f;
        public const float NearOvertakeCap = 25f;
        public const float NearSpacings = 3f;
        public const float CrossTrackMaxDeg = 45f;
        public const float TurnBlendStartDeg = 20f, TurnBlendFullDeg = 60f;

        public static GuidanceCommand Evaluate(in FlightIntent intent, in AircraftState s, AirframeProfile p)
        {
            float precision = intent.Precision > 0f ? Math.Max(0.5f, intent.Precision) : 1f;
            RefState r = intent.Ref;

            Vec3 track = r.Vel.Horizontal.Normalized;
            if (track.SqrLength < 0.5f) track = s.Vel.Horizontal.Normalized;
            if (track.SqrLength < 0.5f) track = s.Fwd.Horizontal.Normalized;
            if (track.SqrLength < 0.5f) track = Vec3.Forward;
            Vec3 cross = Vec3.Cross(Vec3.Up, track).Normalized;

            Vec3 e = r.Pos - s.Pos;
            float along = Vec3.Dot(e, track), side = Vec3.Dot(e, cross);
            float refSpeed = r.Vel.Length;
            float available = intent.Limits.AfterburnerAllowed ? p.MaxSpeed : p.MilSpeed;
            if (intent.Limits.Max > 0f) available = Math.Min(available, intent.Limits.Max);
            float catchUp = Scalar.Clamp(available - refSpeed - CatchUpMargin, 0f, CatchUpCap);
            if (intent.Spacing > 0f && e.Length < NearSpacings * intent.Spacing)
                catchUp = Math.Min(catchUp, NearOvertakeCap);

            float vAlong;
            if (along >= 0f)
            {
                float braking = intent.Limits.AirbrakeAllowed ? p.AirbrakeDecel : p.BrakeDecel;
                vAlong = Math.Min(Math.Min(along * precision / p.TauAlong, catchUp),
                    FormationClosure.SafeClosure(along, braking, p.TauVel));
            }
            else
            {
                float slowdown = Math.Max(0f, refSpeed - Math.Max(0f, intent.Limits.Min));
                vAlong = -Math.Min(Math.Min(-along * precision / p.TauAlong, slowdown),
                    FormationClosure.SafeClosure(-along, p.ThrustAccelMax, p.TauVel));
            }

            float sideMax = Math.Max(refSpeed, 1f) * (float)Math.Tan(CrossTrackMaxDeg * Scalar.Deg2Rad);
            float vSide = Scalar.Clamp(side * precision / p.TauCross, -sideMax, sideMax);
            float climbMax = Math.Min(p.ClimbRateMax, 0.5f * Math.Max(s.Tas, refSpeed));
            float vUp = Scalar.Clamp(e.Y * precision / p.TauVert, -climbMax, climbMax);

            Vec3 velCmd = r.Vel + track * vAlong + cross * vSide + Vec3.Up * vUp;
            float magnitude = velCmd.Length;
            if (magnitude > available) velCmd *= available / magnitude;
            else if (intent.Limits.Min > 0f && magnitude > 1f && magnitude < intent.Limits.Min)
                velCmd *= intent.Limits.Min / magnitude;

            // Large heading changes: the plain vector difference would brake whenever the command's along-track
            // part is below the current speed, i.e. throughout any big turn. Past ~45° the along-track target
            // blends up to the full commanded speed (reached at 60°), and past 90° the sideways part stays at full
            // strength toward the side the command lies on, so the demand is a maximum turn that holds speed,
            // continuous in the angle. Small angles keep the plain law.
            Vec3 steer = velCmd;
            Vec3 heading = s.Vel.Horizontal, command = velCmd.Horizontal;
            float speedCmd = command.Length;
            if (heading.SqrLength > 1f && speedCmd > 1f)
            {
                Vec3 h = heading.Normalized;
                float cos = Scalar.Clamp(Vec3.Dot(command, h) / speedCmd, -1f, 1f);
                float angle = (float)Math.Acos(cos) * Scalar.Rad2Deg;
                float alongFactor = Math.Max(cos, Scalar.SmoothStep(TurnBlendStartDeg, TurnBlendFullDeg, angle));
                if (alongFactor > cos + 1e-4f)
                {
                    Vec3 aside = Vec3.Cross(Vec3.Up, h);
                    float lean = Vec3.Dot(command, aside);
                    if (Math.Abs(lean) < 1f) lean = Vec3.Dot(e, aside);
                    float sideways = cos >= 0f ? (float)Math.Sqrt(1f - cos * cos) : 1f;
                    steer = (h * alongFactor + aside * ((lean < 0f ? -1f : 1f) * sideways)) * speedCmd + Vec3.Up * velCmd.Y;
                }
            }
            Vec3 accel = ClampAccel(r.Acc + (steer - s.Vel) / Math.Max(0.1f, p.TauVel), s, p,
                intent.Limits.AirbrakeAllowed);

            return new GuidanceCommand
            {
                Accel = accel,
                VelCmd = velCmd,
                AfterburnerAllowed = intent.Limits.AfterburnerAllowed,
                AirbrakeAllowed = intent.Limits.AirbrakeAllowed,
            };
        }

        private static Vec3 ClampAccel(Vec3 accel, in AircraftState s, AirframeProfile p, bool airbrake)
        {
            float speed = s.Vel.Length;
            if (speed < 1f) return accel;
            Vec3 v = s.Vel / speed;
            float along = Vec3.Dot(accel, v);
            Vec3 normal = accel - v * along;
            float normalMax = Math.Max(0f, Math.Min(p.GLimit, p.LiftLimitedG(s.Eas)) - 1f) * Scalar.G;
            float magnitude = normal.Length;
            if (magnitude > normalMax) normal *= normalMax / magnitude;
            along = Scalar.Clamp(along, -(airbrake ? p.AirbrakeDecel : p.BrakeDecel), p.ThrustAccelMax);
            return v * along + normal;
        }
    }
}
