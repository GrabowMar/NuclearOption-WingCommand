using System;

namespace WingCommand
{
    /// <summary>Position → velocity → acceleration cascade toward a moving reference. One law at every
    /// distance: per-axis saturation turns a far error into pursuit at catch-up speed and a near error into
    /// a linear PD with the reference acceleration as feedforward. The along-track closure also respects a
    /// stopping-distance law, so arrival does not overshoot.</summary>
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

            return new GuidanceCommand
            {
                Accel = r.Acc + (velCmd - s.Vel) / Math.Max(0.1f, p.TauVel),
                VelCmd = velCmd,
                AfterburnerAllowed = intent.Limits.AfterburnerAllowed,
                AirbrakeAllowed = intent.Limits.AirbrakeAllowed,
            };
        }
    }
}
