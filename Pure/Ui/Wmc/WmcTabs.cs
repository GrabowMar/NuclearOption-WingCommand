using System;

namespace WingCommand
{
    /// <summary>The bezel's tabs (spec bezel v2 §2): the flying tabs, a group rule, then the logistics tabs. Each control id's prefix
    /// names its tab, so a press from automation shows the page it lives on.</summary>
    internal static class WmcTabs
    {
        public const int Tactical = 0, Behaviour = 1, Supply = 2, Loadout = 3, Squadron = 4;

        /// <summary>The user (2026-09-28): fewer tabs — PLAN went into FORM, renamed BEHAVIOUR (formation, behaviour options, the plan,
        /// the route, the timeline and the log as its sub-pages); INSPECT went into SQUADRON.</summary>
        public static readonly string[] Labels = { "TACTICAL", "BEHAVIOUR", "SUPPLY", "LOADOUT", "SQUADRON" };

        /// <summary>The first logistics tab (the group rule sits on its left edge).</summary>
        public const int FirstLogistics = Supply;

        private static readonly string[][] prefixes =
        {
            new[] { "tac." }, new[] { "form.", "opt.", "plan." }, new[] { "sup." }, new[] { "lo." }, new[] { "wing.", "sq.", "insp." },
        };

        /// <summary>A tab by its label (any case; the 0.9 name WING is SQUADRON); −1 when there is none. Numbers are refused: the
        /// 4-tab numbers meant other tabs (critic §14.9), so a scenario that still uses one fails instead of opening the wrong page.</summary>
        public static int Index(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            // Older names still open the tab that holds them now (WING was 0.9's SQUADRON).
            if (string.Equals(name, "WING", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "INSPECT", StringComparison.OrdinalIgnoreCase)) return Squadron;
            if (string.Equals(name, "FORM", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "PLAN", StringComparison.OrdinalIgnoreCase)) return Behaviour;
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
