using System;

namespace WingCommand
{
    /// <summary>International Standard Atmosphere density (troposphere lapse, isothermal stratosphere),
    /// used for dynamic-pressure gain scheduling and the flight-sim plant.</summary>
    internal static class Isa
    {
        public const float SeaLevelDensity = 1.225f;
        private const float TropopauseAltitude = 11000f;
        private const float TropopauseDensity = 0.36392f;
        private const float StratosphereScaleHeight = 6341.6f;

        public static float Density(float altitude)
        {
            if (altitude <= TropopauseAltitude)
            {
                double ratio = 1.0 - 2.25577e-5 * Math.Max(-1000f, altitude);
                return (float)(SeaLevelDensity * Math.Pow(ratio, 4.25588));
            }
            return TropopauseDensity * (float)Math.Exp(-(altitude - TropopauseAltitude) / StratosphereScaleHeight);
        }

        public static float DynamicPressure(float altitude, float airspeed) =>
            0.5f * Density(altitude) * airspeed * airspeed;
    }
}
