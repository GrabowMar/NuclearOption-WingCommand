using System;

namespace WingCommand
{
    /// <summary>Helicopter guidance: position → velocity → acceleration toward a moving reference, with no minimum
    /// speed (spec M2 §4.2).
    /// <list type="bullet">
    /// <item>Horizontal: the reference velocity plus a correction toward it (error / TauAlong), the correction capped by
    /// the cruise speed and by a stopping law √(2·a_brake·d), so it arrives without overshoot; the total is capped by
    /// the cruise speed. The helo flies toward the reference in any direction: a reference behind it means backwards.</item>
    /// <item>Vertical: the reference climb plus error / TauVert, within ±ClimbRateMax.</item>
    /// <item>Acceleration: reference acceleration + velocity error / TauVel; horizontal within g·tan(MaxTilt),
    /// vertical within ±VerticalAccelMax.</item>
    /// <item>Heading: along the command above <see cref="YawAlignSpeed"/>; below it the intent's heading (the leader's
    /// way), else along a moving reference, else held.</item>
    /// </list></summary>
    internal static class RotaryGuidance
    {
        public static float YawAlignSpeed = 15f, BrakeFraction = 0.6f, MovingReferenceSpeed = 1f;

        public static GuidanceCommand Evaluate(in FlightIntent intent, in AircraftState s, AirframeProfile p)
        {
            float precision = intent.Precision > 0f ? Math.Max(0.5f, intent.Precision) : 1f;
            RefState r = intent.Ref;
            float tiltAccel = Scalar.G * (float)Math.Tan(p.MaxTiltDeg * Scalar.Deg2Rad);
            float cruise = intent.Limits.Max > 0f ? Math.Min(p.CruiseSpeed, intent.Limits.Max) : p.CruiseSpeed;

            Vec3 e = r.Pos - s.Pos;
            Vec3 eh = e.Horizontal;
            float distance = eh.Length;
            Vec3 correction = eh * (precision / p.TauAlong);
            float allowed = Math.Min(cruise, (float)Math.Sqrt(2f * BrakeFraction * tiltAccel * distance));
            if (correction.Length > allowed) correction *= allowed / correction.Length;
            Vec3 vh = r.Vel.Horizontal + correction;
            if (vh.Length > cruise) vh *= cruise / vh.Length;

            float climbMax = p.ClimbRateMax;
            float vUp = Scalar.Clamp(r.Vel.Y + Scalar.Clamp(e.Y * precision / p.TauVert, -climbMax, climbMax), -climbMax, climbMax);
            Vec3 velCmd = vh + Vec3.Up * vUp;

            Vec3 accel = r.Acc + (velCmd - s.Vel) / Math.Max(0.1f, p.TauVel);
            Vec3 ah = accel.Horizontal;
            if (ah.Length > tiltAccel) ah *= tiltAccel / ah.Length;
            accel = ah + Vec3.Up * Scalar.Clamp(accel.Y, -p.VerticalAccelMax, p.VerticalAccelMax);

            var g = new GuidanceCommand { Accel = accel, VelCmd = velCmd, AirbrakeAllowed = intent.Limits.AirbrakeAllowed };
            if (vh.Length > YawAlignSpeed)
            {
                g.HasHeading = true;
                g.HeadingDeg = Vec3.HeadingDeg(vh);
            }
            else if (intent.HasHeading)
            {
                g.HasHeading = true;
                g.HeadingDeg = intent.HeadingDeg;
            }
            else if (r.Vel.Horizontal.Length > MovingReferenceSpeed)
            {
                g.HasHeading = true;
                g.HeadingDeg = Vec3.HeadingDeg(r.Vel);
            }
            return g;
        }
    }
}
