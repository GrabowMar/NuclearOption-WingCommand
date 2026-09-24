using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>TACTICAL's short words (spec WMC rebuild §bezel shell): posture, scope, the armed-order cue and telemetry
    /// fields — invariant culture, a dash when unknown, bounded length so nothing is cut with "…".</summary>
    internal static class WmcWords
    {
        /// <summary>"2 ATK · 1 FORM · 1 DEF" in that order (then RTB, GND); NONE for nobody.</summary>
        public static string Posture(SnapshotMember[] rows, int count)
        {
            int atk = 0, form = 0, def = 0, rtb = 0, gnd = 0;
            for (int i = 0; i < count; i++)
                switch ((MemberDuty)rows[i].Duty)
                {
                    case MemberDuty.Engaged: atk++; break;
                    case MemberDuty.Defending: def++; break;
                    case MemberDuty.Recovering: rtb++; break;
                    case MemberDuty.Grounded:
                    case MemberDuty.Settled: gnd++; break;
                    default: form++; break;
                }
            var sb = new StringBuilder();
            Part(sb, atk, "ATK");
            Part(sb, form, "FORM");
            Part(sb, def, "DEF");
            Part(sb, rtb, "RTB");
            Part(sb, gnd, "GND");
            return sb.Length > 0 ? sb.ToString() : "NONE";
        }

        private static void Part(StringBuilder sb, int n, string word)
        {
            if (n == 0) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(word);
        }

        /// <summary>The scope row's words: the wing or an element with its size; up to three member numbers, else a count.</summary>
        public static string ScopeText(string label, int members)
        {
            string n = members.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(label) || label == "WING") return "WING · " + n;
            if (label.StartsWith("ELEMENT", System.StringComparison.Ordinal)) return label + " · " + n;
            int numbers = 0;
            foreach (char ch in label)
                if (ch == '#') numbers++;
            return numbers > 3 ? n + " AIRCRAFT" : label;
        }

        /// <summary>What the armed order waits for.</summary>
        public static string Cue(MapMode mode)
        {
            switch (mode)
            {
                case MapMode.Off: return null;
                case MapMode.Attack: return "RIGHT-CLICK AN ENEMY · SHIFT ADDS";
                case MapMode.Route: return "RIGHT-CLICK TO ADD POINTS";
                default: return "RIGHT-CLICK A POINT";
            }
        }

        /// <summary>The ORDERS cue banner while a map order is armed; null when none is.</summary>
        public static string Banner(MapMode mode) => mode == MapMode.Off ? null : "ARMED " + MapOrders.Label(mode) + " · " + Cue(mode);

        private static readonly System.Collections.Generic.Dictionary<string, string> shapeCodes =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "echelon-right", "ECH R" }, { "echelon-left", "ECH L" }, { "line-abreast", "ABREAST" }, { "trail", "TRAIL" },
                { "vic", "VIC" }, { "finger-four-right", "FNGR R" }, { "finger-four-left", "FNGR L" }, { "diamond", "DIAMOND" },
                { "box", "BOX" }, { "ladder", "LADDER" }, { "combat-spread", "SPREAD" }, { "fluid-four", "FLUID 4" },
                { "wall", "WALL" }, { "offset-box", "OFF BOX" }, { "card", "CARD" }, { "staggered-trail", "STG TRL" },
                { "echelon-staggered", "ECH STG" }, { "heavy-stream", "STREAM" }, { "high-cover", "HI COVER" },
                { "low-cover", "LO COVER" }, { "sweep-ahead", "SWEEP" }, { "close-escort", "CLOSE" },
            };

        /// <summary>A shape in eight characters (review R1 I3): the shipped shapes by code, anything else by its name without
        /// the bracketed part, upper-cased and cut to eight (the full name is in the tooltip).</summary>
        public static string Shape(string id, string name)
        {
            if (id != null && shapeCodes.TryGetValue(id, out string code)) return code;
            if (string.IsNullOrEmpty(name)) return WmcText.Unknown;
            int bracket = name.IndexOf('(');
            string plain = (bracket > 0 ? name.Substring(0, bracket) : name).Trim().ToUpperInvariant();
            return plain.Length <= 8 ? plain : plain.Substring(0, 8).TrimEnd();
        }

        public static string Altitude(float metres) =>
            float.IsNaN(metres) || float.IsInfinity(metres) ? WmcText.Unknown
                : System.Math.Round(metres).ToString("#,##0", CultureInfo.InvariantCulture) + " m";

        public static string Speed(float metresPerSecond) =>
            float.IsNaN(metresPerSecond) || float.IsInfinity(metresPerSecond) ? WmcText.Unknown
                : System.Math.Round(metresPerSecond * 3.6f).ToString("0", CultureInfo.InvariantCulture) + " km/h";
    }
}
