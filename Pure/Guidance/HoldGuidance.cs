using System;

namespace WingCommand
{
    internal enum LateralHold : byte { None, Level, Heading }
    internal enum VerticalHold : byte { None, Altitude, VerticalSpeed }

// Filled by the player autopilot (M1c) and the FlightSim.
#pragma warning disable CS0649
    /// <summary>Player autopilot modes and targets. Axes whose mode is None are the pilot's; the engine
    /// writer does not write them.</summary>
    internal struct HoldSpec
    {
        public LateralHold Lateral;
        public VerticalHold Vertical;
        public bool Speed;
        public float HeadingDeg, AltitudeM, VerticalSpeedMps, SpeedMps;
        /// <summary>Heading-mode bank cap; 0 means 30°.</summary>
        public float BankCapDeg;
    }
#pragma warning restore CS0649

    /// <summary>Autopilot hold modes as an acceleration command for the same pipeline the wingmen use.
    /// Heading: turn rate ∝ heading error within the bank cap. Altitude: climb rate ∝ error, bounded by
    /// a braking law √(2·g·0.3·|Δh|) so capture does not overshoot. Speed: first-order toward the target.
    /// </summary>
    internal static class HoldGuidance
    {
        public static float HeadingGain = 0.5f;
        public static float AltitudeGain = 0.2f;
        public static float CaptureG = 0.3f;
        public static float VerticalTau = 2f, SpeedTau = 5f;

        public static GuidanceCommand Evaluate(in HoldSpec h, in AircraftState s, AirframeProfile p)
        {
            float speed = Math.Max(1f, s.Speed);
            Vec3 horizontal = s.Vel.Horizontal.Normalized;
            if (horizontal.SqrLength < 0.5f) horizontal = s.Fwd.Horizontal.Normalized;
            if (horizontal.SqrLength < 0.5f) horizontal = Vec3.Forward;
            Vec3 right = Vec3.Cross(Vec3.Up, horizontal).Normalized;
            Vec3 accel = Vec3.Zero;

            if (h.Lateral == LateralHold.Heading)
            {
                float cap = h.BankCapDeg > 0f ? h.BankCapDeg : 30f;
                float maxRate = Scalar.G * (float)Math.Tan(cap * Scalar.Deg2Rad) / speed;
                float error = Scalar.Wrap180(h.HeadingDeg - s.TrackDeg);
                float rate = Scalar.Clamp(HeadingGain * error * Scalar.Deg2Rad, -maxRate, maxRate);
                accel += right * (speed * rate);
            }

            float vyCmd = s.Vel.Y;
            if (h.Vertical == VerticalHold.Altitude)
            {
                float dh = h.AltitudeM - s.Pos.Y;
                float capture = (float)Math.Sqrt(2f * Scalar.G * CaptureG * Math.Abs(dh));
                vyCmd = Scalar.Clamp(Scalar.Clamp(AltitudeGain * dh, -capture, capture), -p.ClimbRateMax, p.ClimbRateMax);
            }
            else if (h.Vertical == VerticalHold.VerticalSpeed)
                vyCmd = Scalar.Clamp(h.VerticalSpeedMps, -p.ClimbRateMax, p.ClimbRateMax);
            accel += Vec3.Up * ((vyCmd - s.Vel.Y) / VerticalTau);

            float targetSpeed = h.Speed ? h.SpeedMps : s.Tas;
            if (h.Speed)
                accel += horizontal * Scalar.Clamp((h.SpeedMps - s.Tas) / SpeedTau, -p.BrakeDecel, p.ThrustAccelMax);

            return new GuidanceCommand
            {
                Accel = accel,
                VelCmd = horizontal * targetSpeed + Vec3.Up * vyCmd,
                AfterburnerAllowed = false,
                AirbrakeAllowed = true,
            };
        }
    }
}
