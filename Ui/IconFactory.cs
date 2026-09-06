using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WingCommand
{
    /// <summary>White PNG glyphs, cached once and tinted by their UI controls.</summary>
    internal static class IconFactory
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "root";
            if (cache.TryGetValue(key, out Sprite sprite)) return sprite;
            using (Stream source = typeof(IconFactory).Assembly.GetManifestResourceStream("WingCommand.Icons." + key + ".png"))
            {
                if (source == null)
                {
                    Plugin.Logger.LogWarning("Missing wing icon: " + key);
                    return cache[key] = key == "root" ? null : Get("root");
                }
                using (var bytes = new MemoryStream())
                {
                    source.CopyTo(bytes);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = "WingIcon_" + key,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!ImageConversion.LoadImage(texture, bytes.ToArray(), true))
                    {
                        Object.Destroy(texture);
                        Plugin.Logger.LogWarning("Invalid wing icon: " + key);
                        return cache[key] = key == "root" ? null : Get("root");
                    }
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = texture.name;
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    return cache[key] = sprite;
                }
            }
        }
    }
}
