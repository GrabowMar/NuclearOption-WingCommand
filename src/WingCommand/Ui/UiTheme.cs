using UnityEngine;
using NuclearOption.UIStyleSystem;

namespace WingCommand
{
    /// <summary>
    /// The handful of presentation details the wing's two surfaces share: the game's own
    /// theme colours, and fitting text into a fixed-width column.
    ///
    /// Both were copied into every UI file that needed them, which is how three separate
    /// <c>Truncate</c> implementations and three separate theme lookups came to exist.
    /// Every accessor falls back to a fixed colour, because <c>ThemeManager.Active</c>
    /// throws before the game's UI has initialised and these are called from paint paths.
    /// </summary>
    internal static class UiTheme
    {
        /// <summary>The theme colour the game uses for friendly symbology.</summary>
        public static Color Green
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.AllClear; }
                catch { return new Color(0.30f, 1f, 0.35f); }
            }
        }

        /// <summary>The map's friendly-icon colour, used for secondary text.</summary>
        public static Color Friendly
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
                catch { return new Color(0.45f, 0.95f, 0.55f); }
            }
        }

        /// <summary>The friendly colour used by the in-cockpit HUD.</summary>
        public static Color HudFriendly
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.HudUnitFriendly; }
                catch { return Friendly; }
            }
        }

        /// <summary>The stock warning colour used for low reserves and degraded state.</summary>
        public static Color Warning
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.Warning; }
                catch { return new Color(1f, 0.55f, 0.2f); }
            }
        }

        /// <summary>The stock alert colour used for failed or badly damaged systems.</summary>
        public static Color Alert
        {
            get
            {
                try { return ThemeManager.Active.ColorTheme.Alert; }
                catch { return new Color(1f, 0.18f, 0.12f); }
            }
        }

        /// <summary>Clip a string to a column width. Null-safe.</summary>
        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
