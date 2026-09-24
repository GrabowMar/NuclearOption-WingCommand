using System;
using System.Globalization;

namespace WingCommand
{
    internal enum ApField : byte { Heading, Altitude, VerticalSpeed, Speed }

    /// <summary>The AP tab's ± steppers (spec M7b §3): heading wraps, the rest clamp; a NaN target starts from 0.</summary>
    internal static class ApSteps
    {
        public static float HeadingStep = 5f, AltitudeStep = 100f, VerticalSpeedStep = 1f, SpeedStepKmh = 10f;
        public static float MaxAltitude = 15000f, MaxVerticalSpeed = 30f, MaxSpeedKmh = 2500f;

        public static void Adjust(ref HoldSpec spec, ApField field, int direction)
        {
            int dir = Math.Sign(direction);
            switch (field)
            {
                case ApField.Heading:
                    float h = Finite(spec.HeadingDeg) + dir * HeadingStep;
                    spec.HeadingDeg = ((h % 360f) + 360f) % 360f;
                    break;
                case ApField.Altitude:
                    spec.AltitudeM = Step(Finite(spec.AltitudeM), AltitudeStep, dir, 0f, MaxAltitude);
                    break;
                case ApField.VerticalSpeed:
                    spec.VerticalSpeedMps = Step(Finite(spec.VerticalSpeedMps), VerticalSpeedStep, dir, -MaxVerticalSpeed, MaxVerticalSpeed);
                    break;
                default:
                    spec.SpeedMps = Step(Finite(spec.SpeedMps), SpeedStepKmh / 3.6f, dir, 0f, MaxSpeedKmh / 3.6f);
                    break;
            }
        }

        /// <summary>The held value as the HUD writes it: "005°", "3200 m", "+2.0 m/s", "180 km/h".</summary>
        public static string Readout(in HoldSpec spec, ApField field, bool held = true)
        {
            // A value no mode holds is a dash, not a made-up 0 (review M7b-1 minor).
            if (!held) return WmcText.Unknown;
            switch (field)
            {
                case ApField.Heading:
                    int hdg = (int)Math.Round(Finite(spec.HeadingDeg)) % 360;
                    if (hdg < 0) hdg += 360;
                    return hdg.ToString("000", CultureInfo.InvariantCulture) + "°";
                case ApField.Altitude:
                    return Finite(spec.AltitudeM).ToString("0", CultureInfo.InvariantCulture) + " m";
                case ApField.VerticalSpeed:
                    return Finite(spec.VerticalSpeedMps).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " m/s";
                default:
                    return (Finite(spec.SpeedMps) * 3.6f).ToString("0", CultureInfo.InvariantCulture) + " km/h";
            }
        }

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        /// <summary>A step in the direction pressed, stopped at the limit — a value already beyond it never moves back
        /// towards it against the press (review M7b-1 minor).</summary>
        private static float Step(float v, float step, int dir, float lo, float hi) =>
            dir > 0 ? Math.Min(v + step, Math.Max(hi, v)) : dir < 0 ? Math.Max(v - step, Math.Min(lo, v)) : v;
    }
}
