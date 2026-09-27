using System;

namespace WingCommand
{
    /// <summary>The bezel's tabs (spec bezel v2 §2): the flying tabs, a group rule, then the logistics tabs. Each control id's prefix
    /// names its tab, so a press from automation shows the page it lives on.</summary>
    internal static class WmcTabs
    {
        public const int Tactical = 0, Form = 1, Plan = 2, Inspect = 3, Supply = 4, Loadout = 5, Squadron = 6;

        /// <summary>At most 8 characters each: the 8th tab (SETUP) leaves a 48 px label box.</summary>
        public static readonly string[] Labels = { "TACTICAL", "FORM", "PLAN", "INSPECT", "SUPPLY", "LOADOUT", "SQUADRON" };

        /// <summary>The first logistics tab (the group rule sits on its left edge).</summary>
        public const int FirstLogistics = Supply;

        private static readonly string[][] prefixes =
        {
            new[] { "tac." }, new[] { "form." }, new[] { "plan." }, new[] { "insp." }, new[] { "sup." }, new[] { "lo." }, new[] { "wing.", "sq." },
        };

        /// <summary>A tab by its label (any case; the 0.9 name WING is SQUADRON); −1 when there is none. Numbers are refused: the
        /// 4-tab numbers meant other tabs (critic §14.9), so a scenario that still uses one fails instead of opening the wrong page.</summary>
        public static int Index(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (string.Equals(name, "WING", StringComparison.OrdinalIgnoreCase)) return Squadron;
            for (int i = 0; i < Labels.Length; i++)
                if (string.Equals(name, Labels[i], StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The tab a control id belongs to; −1 for the header's own controls and unknown ids.</summary>
        public static int Of(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < prefixes.Length; i++)
                foreach (string p in prefixes[i])
                    if (id.StartsWith(p, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>The x of the rule between the flying and the logistics tabs on a <paramref name="width"/> px tab bar.</summary>
        public static float GroupRuleX(float width, int tabs) => tabs > 0 ? width * FirstLogistics / tabs : 0f;
    }
}
