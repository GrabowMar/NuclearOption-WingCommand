using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>LOADOUT template names (spec WMC rebuild §LOADOUT: names capped at 16): typed text is cleaned, upper-cased and cut at a
    /// word; a blank name keeps the old one; new templates take the first free "TEMPLATE n"; a copy or a rename to a taken name gets
    /// the next free number and still fits. Names compare without case. Five per airframe, so SUPPLY's FIT (AUTO, YOUR LOADOUT and the
    /// templates) is exactly the toolkit popup's seven rows.</summary>
    internal static class TemplateNames
    {
        public const int MaxChars = 16, PerAirframe = 5;

        /// <summary>The name as saved, or null to keep the old one.</summary>
        public static string Clean(string typed)
        {
            if (typed == null) return null;
            var sb = new StringBuilder(typed.Length);
            bool space = false;
            foreach (char ch in typed)
            {
                if (char.IsWhiteSpace(ch))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (char.IsControl(ch)) continue;
                if (space) sb.Append(' ');
                space = false;
                sb.Append(ch);
            }
            string s = WmcText.Cut(sb.ToString().ToUpperInvariant(), MaxChars);
            return s.Length == 0 ? null : s;
        }

        public static string NextDefault(IReadOnlyList<string> taken)
        {
            for (int n = 1; ; n++)
            {
                string name = "TEMPLATE " + n.ToString(CultureInfo.InvariantCulture);
                if (!Taken(name, taken)) return name;
            }
        }

        /// <summary><paramref name="name"/> when free, else "NAME 2", "NAME 3" (the base cut so the whole fits).</summary>
        public static string Unique(string name, IReadOnlyList<string> taken)
        {
            if (!Taken(name, taken)) return name;
            for (int n = 2; ; n++)
            {
                string suffix = " " + n.ToString(CultureInfo.InvariantCulture);
                string base0 = name.Length + suffix.Length <= MaxChars ? name : name.Substring(0, MaxChars - suffix.Length).TrimEnd();
                string candidate = base0 + suffix;
                if (!Taken(candidate, taken)) return candidate;
            }
        }

        public static bool Taken(string name, IReadOnlyList<string> taken)
        {
            if (taken == null) return false;
            foreach (string t in taken)
                if (string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
