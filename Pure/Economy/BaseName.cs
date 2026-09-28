using System;

namespace WingCommand
{
    /// <summary>A launch field's name for the panel (spec WMC rebuild §SUPPLY): the game's display name, else its unique name
    /// formatted (<see cref="AirbaseNameFormatter"/>), shortened to <see cref="Max"/> characters by its common words
    /// ("Airbase" → "AB", "General Aviation" → "GA") and then at a word boundary — never with "…".</summary>
    internal static class BaseName
    {
        public const int Max = 20;

        private static readonly string[][] Short1 =
        {
            new[] { "General Aviation", "GA" }, new[] { "International", "Intl" }, new[] { "Airbase", "AB" },
            new[] { "Airfield", "AF" }, new[] { "Airport", "Apt" }, new[] { "Highway", "Hwy" },
        };

        public static string Of(string displayName, string uniqueName, string objectName) =>
            !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim()
            : AirbaseNameFormatter.Format(!string.IsNullOrWhiteSpace(uniqueName) ? uniqueName : objectName);

        public static string Short(string name, int max = Max)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= max) return name ?? "";
            string s = name;
            foreach (string[] pair in Short1)
                s = Replace(s, pair[0], pair[1]);
            if (s.Length <= max) return s;
            int cut = s.LastIndexOf(' ', max);
            return (cut > 0 ? s.Substring(0, cut) : s.Substring(0, max)).TrimEnd();
        }

        private static string Replace(string s, string word, string with)
        {
            int i = s.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + word.Length);
        }
    }
}
