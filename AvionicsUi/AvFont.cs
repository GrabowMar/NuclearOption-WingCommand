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
