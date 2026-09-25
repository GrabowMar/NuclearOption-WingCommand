using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>Money as the panel reads it (spec WMC rebuild §SUPPLY): the game's values and the player's allocation are in
    /// millions (UnitConverter.ValueReading), shown as credits — "87 CR", "9,620 CR" — in one invariant format everywhere
    /// (refusals, toasts, logs, the FUNDS tile).</summary>
    internal static class Credits
    {
        public static string Text(float millions) => Number(millions) + " CR";

        /// <summary>A price: nothing is FREE.</summary>
        public static string Price(float millions) => millions <= 0f ? "FREE" : Text(millions);

        /// <summary>The FUNDS tile's value: at most seven characters, no unit.</summary>
        public static string Short(float millions)
        {
            if (Math.Abs(millions) < 1_000_000f) return Number(millions);
            float m = millions / 1_000_000f;
            return (Math.Abs(m) < 10f ? m.ToString("0.#", CultureInfo.InvariantCulture) : m.ToString("0", CultureInfo.InvariantCulture)) + "M";
        }

        private static string Number(float v) =>
            Math.Abs(v) < 100f && Math.Abs(v - (float)Math.Round(v)) > 0.001f
                ? v.ToString("0.#", CultureInfo.InvariantCulture)
                : v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
