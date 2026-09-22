using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Font resolution and caching across avionics panels.
    /// Resolves the vanilla MFD TMP_FontAsset off screen templates or the active scene,
    /// preventing fallback to default generic TMP fonts.
    /// </summary>
    public static class AvFont
    {
        private static TMP_FontAsset font;

        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;

                // 1. Prioritize resolving from an MFDScreen template/label
                MFDScreen mfdScreen = Object.FindObjectOfType<MFDScreen>();
                if (mfdScreen != null && mfdScreen.label != null && mfdScreen.label.font != null)
                {
                    font = mfdScreen.label.font;
                    return font;
                }

                // 2. Prioritize tactical screen font from active theme system
                try
                {
                    var theme = ThemeManager.Active?.TacScreenTheme;
                    if (theme != null && theme.TextStyles != null)
                    {
                        for (int i = 0; i < theme.TextStyles.Count; i++)
                        {
                            var item = theme.TextStyles[i];
                            if (item?.Style?.Font != null)
                            {
                                font = item.Style.Font;
                                return font;
                            }
                        }
                    }
                }
                catch { }

                // 3. Fall back to any TextMeshProUGUI in the scene
                TMP_Text any = Object.FindObjectOfType<TextMeshProUGUI>();
                if (any != null) font = any.font;
                return font;
            }
            set
            {
                if (value != null) font = value;
            }
        }

        public static void Reset() => font = null;
    }
}
