using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WingCommand
{
    internal static class PilotPortrait
    {
        private static readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        private static byte[] layers;
        private static bool loadAttempted;

        public static Sprite Sprite => For(null);

        public static Sprite For(WingPilot pilot)
        {
            string identity = pilot == null ? "WingCommand" : pilot.Name + "|" + pilot.Callsign;
            if (portraits.TryGetValue(identity, out Sprite portrait)) return portrait;
            if (!LoadLayers()) return null;

            var texture = new Texture2D(PilotPortraitGenerator.Width, PilotPortraitGenerator.Height,
                                        TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_Pilot_" + identity,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.LoadRawTextureData(PilotPortraitGenerator.Compose(identity, layers));
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            portrait = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                                 new Vector2(0.5f, 0.5f), 100f);
            portrait.name = texture.name;
            portrait.hideFlags = HideFlags.HideAndDontSave;
            portraits.Add(identity, portrait);
            return portrait;
        }

        private static bool LoadLayers()
        {
            if (loadAttempted) return layers != null;
            loadAttempted = true;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                using (Stream stream = typeof(PilotPortrait).Assembly.GetManifestResourceStream("WingCommand.PilotLayers.png"))
                using (var bytes = new MemoryStream())
                {
                    if (stream == null) throw new InvalidDataException("Embedded portrait layers missing.");
                    stream.CopyTo(bytes);
                    if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false) ||
                        texture.width != PilotPortraitGenerator.AtlasWidth || texture.height != PilotPortraitGenerator.AtlasHeight)
                        throw new InvalidDataException("Invalid portrait atlas dimensions.");
                    // LoadImage can change PNG storage to ARGB32; the compositor needs RGBA.
                    Color32[] pixels = texture.GetPixels32();
                    layers = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        layers[i * 4] = pixels[i].r;
                        layers[i * 4 + 1] = pixels[i].g;
                        layers[i * 4 + 2] = pixels[i].b;
                        layers[i * 4 + 3] = pixels[i].a;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Pilot] Could not load portrait layers: " + e.Message);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
            return layers != null;
        }

        public static void Reset()
        {
            foreach (Sprite portrait in portraits.Values)
            {
                UnityEngine.Object.Destroy(portrait.texture);
                UnityEngine.Object.Destroy(portrait);
            }
            portraits.Clear();
            layers = null;
            loadAttempted = false;
        }
    }
}
