using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The temporary pilot portrait supplied with the Wing-page redesign. Keeping it in
    /// the assembly means the WMC still has a portrait when the mod is installed as its
    /// usual single DLL; a future roster can replace this one sprite with per-pilot art.
    /// </summary>
    internal static class PilotPortrait
    {
        private static Sprite sprite;

        public static Sprite Sprite
        {
            get
            {
                if (sprite != null) return sprite;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "WingCommand_PilotPlaceholder",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(Png), false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                sprite = UnityEngine.Sprite.Create(
                    texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "WingCommand_PilotPlaceholder";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
        }

        // Palette-quantized PNG of the user-provided 128px placeholder. Quantizing keeps
        // the exact composition while avoiding a needless 25 KB of WebP decoder baggage.
        private const string Png =
            "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";
    }
}
