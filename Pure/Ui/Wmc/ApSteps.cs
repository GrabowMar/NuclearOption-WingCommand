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
                    spec.AltitudeM = Clamp(Finite(spec.AltitudeM) + dir * AltitudeStep, 0f, MaxAltitude);
                    break;
                case ApField.VerticalSpeed:
                    spec.VerticalSpeedMps = Clamp(Finite(spec.VerticalSpeedMps) + dir * VerticalSpeedStep, -MaxVerticalSpeed, MaxVerticalSpeed);
                    break;
                default:
                    spec.SpeedMps = Clamp(Finite(spec.SpeedMps) + dir * SpeedStepKmh / 3.6f, 0f, MaxSpeedKmh / 3.6f);
                    break;
            }
        }

        /// <summary>The held value as the HUD writes it: "005°", "3200 m", "+2.0 m/s", "180 km/h".</summary>
        public static string Readout(in HoldSpec spec, ApField field)
        {
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

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
