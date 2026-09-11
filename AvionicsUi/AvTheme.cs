using System;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Live game theme accessor and color/string conversion helpers shared across mods.
    /// Bridges safely to ThemeManager.Active with fixed fallbacks.
    /// </summary>
    public static class AvTheme
    {
        public static Color Accent
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.AllClear; }
                catch { return new Color(0.30f, 1.00f, 0.35f, 1f); }
            }
        }

        public static Color Friendly
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
                catch { return new Color(0.45f, 0.95f, 0.55f, 1f); }
            }
        }

        public static Color HudFriendly
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.HudUnitFriendly; }
                catch { return Friendly; }
            }
        }

        public static Color Warning
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.Warning; }
                catch { return new Color(1.00f, 0.55f, 0.20f, 1f); }
            }
        }

        public static Color Alert
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.Alert; }
                catch { return new Color(1.00f, 0.18f, 0.12f, 1f); }
            }
        }

        public static Color TextPrimary => Unity(AvTokens.TextPrimary);
        public static Color Dim => Unity(AvTokens.TextDim);
        public static Color Disabled => Unity(AvTokens.TextMuted);
        public static Color Frame => Unity(AvTokens.Frame);
        public static Color Hairline => Unity(AvTokens.Hairline);
        public static Color Surface => Unity(AvTokens.Surface);
        public static Color SurfaceRaised => Unity(AvTokens.SurfaceRaised);
        public static Color SurfaceInert => Unity(AvTokens.SurfaceInert);
        public static Color Ground => Unity(AvTokens.Ground);

        public static Color RailReady => Unity(AvTokens.RailReady);
        public static Color RailCaution => Unity(AvTokens.RailCaution);
        public static Color RailDanger => Unity(AvTokens.RailDanger);
        public static Color RailInfo => Unity(AvTokens.RailInfo);
        public static Color RailInert => Unity(AvTokens.RailInert);

        public static Color Unity(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        public static Rgba ToRgba(this Color c) => new Rgba(c.r, c.g, c.b, c.a);

        public static Color WithAlpha(this Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
