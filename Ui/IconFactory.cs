using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Cached white PNG silhouettes, tinted by their controls.</summary>
    internal static class IconFactory
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<int, Sprite> aircraftCache = new Dictionary<int, Sprite>();

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

        /// <summary>Copy the airframe map silhouette, falling back to its friendly icon, and remove opaque
        /// backgrounds without modifying native map/HUD sprites.</summary>
        public static Sprite Aircraft(AircraftDefinition definition)
        {
            Sprite source = null;
            if (definition != null)
            {
                if (definition.mapIcon != null) source = definition.mapIcon;
                else if (definition.friendlyIcon != null) source = definition.friendlyIcon;
            }

            if (source == null) return Get("airframe");
            return TransparentGlyph(source);
        }

        private static Sprite TransparentGlyph(Sprite source)
        {
            int id = source.GetInstanceID();
            if (aircraftCache.TryGetValue(id, out Sprite cached) && cached != null)
                return cached;

            Sprite prepared = PrepareTransparentGlyph(source) ?? source;
            aircraftCache[id] = prepared;
            return prepared;
        }

        private static Sprite PrepareTransparentGlyph(Sprite source)
        {
            if (!TryCopySpritePixels(source, out Texture2D copy)) return null;

            Color[] colors = copy.GetPixels();
            float[] rgba = new float[colors.Length * 4];
            for (int i = 0; i < colors.Length; i++)
            {
                Color c = colors[i];
                int o = i * 4;
                rgba[o] = c.r;
                rgba[o + 1] = c.g;
                rgba[o + 2] = c.b;
                rgba[o + 3] = c.a;
            }

            if (!GlyphKnockout.NeedsKnockout(rgba, copy.width, copy.height))
            {
                Object.Destroy(copy);
                return source;
            }

            GlyphKnockout.Apply(rgba, copy.width, copy.height);
            for (int i = 0; i < colors.Length; i++)
            {
                int o = i * 4;
                colors[i] = new Color(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
            }

            copy.SetPixels(colors);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            float pixelsPerUnit = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 100f;
            Sprite sprite = Sprite.Create(
                copy,
                new Rect(0f, 0f, copy.width, copy.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0u,
                SpriteMeshType.Tight);
            sprite.name = source.name + "_WmcGlyph";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.name = sprite.name;
            return sprite;
        }

        private static bool TryCopySpritePixels(Sprite sprite, out Texture2D copy)
        {
            copy = null;
            Texture2D source = sprite != null ? sprite.texture : null;
            if (source == null) return false;

            Rect region;
            try { region = sprite.textureRect; }
            catch { return false; }

            int x = Mathf.RoundToInt(region.x);
            int y = Mathf.RoundToInt(region.y);
            int w = Mathf.RoundToInt(region.width);
            int h = Mathf.RoundToInt(region.height);
            if (w < 1 || h < 1) return false;

            if (source.isReadable)
            {
                try
                {
                    Color[] pixels = source.GetPixels(x, y, w, h);
                    copy = NewGlyphTexture(w, h, source.filterMode);
                    copy.SetPixels(pixels);
                    copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    return true;
                }
                catch
                {
                    if (copy != null) Object.Destroy(copy);
                    copy = null;
                    return false;
                }
            }

            RenderTexture target = RenderTexture.GetTemporary(
                w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                float texW = source.width;
                float texH = source.height;
                if (texW < 1f || texH < 1f) return false;

                Graphics.Blit(
                    source,
                    target,
                    new Vector2(region.width / texW, region.height / texH),
                    new Vector2(region.x / texW, region.y / texH));
                RenderTexture.active = target;
                copy = NewGlyphTexture(w, h, source.filterMode);
                copy.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, recalculateMipMaps: false);
                copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return true;
            }
            catch
            {
                if (copy != null) Object.Destroy(copy);
                copy = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Texture2D NewGlyphTexture(int width, int height, FilterMode filter) =>
            new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
    }
}
