using System;
using System.IO;
using UnityEngine;

namespace WingCommand
{
    // Preserve existing configured deck wallpapers without owning a SET bezel screen.
    internal static class MfdWallpaper
    {
        private static string loadedFile;
        private static Sprite sprite;
        private static Texture2D texture;

        public static Sprite Current
        {
            get
            {
                string file = Plugin.Settings?.MfdCustomImageFile.Value ?? "";
                if (file == loadedFile) return sprite;
                Reset();
                loadedFile = file;
                if (string.IsNullOrWhiteSpace(file)) return null;
                try
                {
                    string path = Path.IsPathRooted(file) ? file : Path.Combine(
                        BepInEx.Paths.ConfigPath, "WingCommand", "Backgrounds", file);
                    if (!File.Exists(path)) return null;
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                    {
                        name = Path.GetFileNameWithoutExtension(path),
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                        throw new InvalidDataException("Image could not be decoded.");
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = texture.name;
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                }
                catch (Exception e)
                {
                    Reset();
                    loadedFile = file;
                    Plugin.Logger.LogWarning("Could not load MFD wallpaper: " + e.Message);
                }
                return sprite;
            }
        }

        public static void Reset()
        {
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            texture = null;
            loadedFile = null;
        }
    }
}
