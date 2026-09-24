using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>Spec M7b §3: WMC's number formats — invariant culture, metric, a dash when unknown.</summary>
    internal static class WmcText
    {
        public const string Unknown = "—";

        /// <summary>"m:ss" (minutes unbounded); negative → 0:00; NaN → dash.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return Unknown;
            int s = Math.Max(0, (int)seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60);
        }

        public static string Km(float metres) =>
            (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";

        public static string Percent(float fraction) =>
            float.IsNaN(fraction) ? Unknown : ((int)Math.Round(fraction * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
