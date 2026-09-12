using System;
using System.Text.RegularExpressions;

namespace WingCommand
{
    /// <summary>
    /// Engine-free string formatter that converts raw scene object identifiers and unit names
    /// into clean, immersion-friendly military airbase and carrier designations.
    /// </summary>
    public static class AirbaseNameFormatter
    {
        private static readonly Regex CloneSuffix = new Regex(@"\s*\(Clone\)$", RegexOptions.Compiled);
        private static readonly Regex UnitAirbasePrefix = new Regex(@"^<UNIT_AIRBASE>\+\+", RegexOptions.Compiled);

        public static string Format(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "FIELD";

            string s = raw.Trim();
            s = UnitAirbasePrefix.Replace(s, "");
            s = CloneSuffix.Replace(s, "").Trim();

            if (string.IsNullOrEmpty(s)) return "FIELD";

            // If it already has spaces and capitalized words without underscores, keep it clean.
            if (s.Contains(" ") && !s.Contains("_"))
            {
                return s;
            }

            // Specific known patterns
            // airbase_island5 -> Island 5 Airfield
            Match mIsland = Regex.Match(s, @"^airbase_island(\d+)$", RegexOptions.IgnoreCase);
            if (mIsland.Success)
            {
                return "Island " + mIsland.Groups[1].Value + " Airfield";
            }

            // airbase_NE, airbase_SE, etc.
            Match mCardinal = Regex.Match(s, @"^airbase_([A-Za-z]+)$", RegexOptions.IgnoreCase);
            if (mCardinal.Success)
            {
                string tag = mCardinal.Groups[1].Value.ToUpperInvariant();
                switch (tag)
                {
                    case "NE": return "Northeast Airbase";
                    case "NW": return "Northwest Airbase";
                    case "SE": return "Southeast Airbase";
                    case "SW": return "Southwest Airbase";
                    case "N": return "North Airbase";
                    case "S": return "South Airbase";
                    case "E": return "East Airbase";
                    case "W": return "West Airbase";
                    case "MAIN": return "Main Airbase";
                    case "CENTRAL": return "Central Airfield";
                    default:
                        return char.ToUpperInvariant(tag[0]) + tag.Substring(1).ToLowerInvariant() + " Airbase";
                }
            }

            // AssaultCarrier1 -> Assault Carrier 01
            Match mCarrier = Regex.Match(s, @"^(AssaultCarrier|Carrier)(\d+)$", RegexOptions.IgnoreCase);
            if (mCarrier.Success)
            {
                string type = mCarrier.Groups[1].Value;
                string num = mCarrier.Groups[2].Value;
                if (int.TryParse(num, out int n))
                {
                    num = n.ToString("00");
                }
                string prefix = type.StartsWith("Assault", StringComparison.OrdinalIgnoreCase)
                    ? "Assault Carrier "
                    : "Carrier ";
                return prefix + num;
            }

            // CamelCase or snake_case conversion fallback
            // Replace underscores with spaces
            string clean = s.Replace("_", " ");

            // Insert space before capital letters if preceded by lowercase letter
            clean = Regex.Replace(clean, @"([a-z])([A-Z])", "$1 $2");

            // Insert space before numbers if preceded by letters
            clean = Regex.Replace(clean, @"([a-zA-Z])(\d+)", "$1 $2");

            // Capitalize individual words
            string[] words = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Equals("airbase", StringComparison.OrdinalIgnoreCase)) words[i] = "Airbase";
                else if (w.Equals("carrier", StringComparison.OrdinalIgnoreCase)) words[i] = "Carrier";
                else if (w.Equals("island", StringComparison.OrdinalIgnoreCase)) words[i] = "Island";
                else if (w.Length > 0 && char.IsLower(w[0]))
                {
                    words[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "");
                }
            }

            return string.Join(" ", words);
        }
    }
}
