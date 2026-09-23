using System;

namespace WingCommand
{
    /// <summary>Position → velocity → acceleration cascade toward a moving reference. One law at every
    /// distance: per-axis saturation turns a far error into pursuit at catch-up speed and a near error into
    /// a linear PD with the reference acceleration as feedforward. The along-track closure also respects a
    /// stopping-distance law, so arrival does not overshoot. The acceleration is clamped to what the
    /// airframe can do: normal part to (Nz_max − 1)·g, tangential part to thrust and drag. A velocity
    /// command more than 90° off the current track becomes a maximum turn toward the side the command lies on
    /// that holds the commanded speed.</summary>
    internal static class TrackingGuidance
    {
        public const float CatchUpMargin = 5f;
        public const float CatchUpCap = 80f;
        public const float NearOvertakeCap = 25f;
        public const float NearSpacings = 3f;
        public const float CrossTrackMaxDeg = 45f;

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

            Vec3 steer = velCmd;
            Vec3 heading = s.Vel.Horizontal;
            if (heading.SqrLength > 1f && Vec3.Dot(velCmd.Horizontal, heading) < 0f)
            {
                Vec3 aside = Vec3.Cross(Vec3.Up, heading).Normalized;
                float lean = Vec3.Dot(velCmd, aside);
                if (Math.Abs(lean) < 1f) lean = Vec3.Dot(e, aside);
                // Along the current track at the commanded speed plus as much again to the chosen side: a maximum
                // turn that holds speed, instead of a sideways vector whose projection says "brake to zero".
                float speedCmd = velCmd.Horizontal.Length;
                steer = (heading.Normalized + aside * (lean < 0f ? -1f : 1f)) * speedCmd + Vec3.Up * velCmd.Y;
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
