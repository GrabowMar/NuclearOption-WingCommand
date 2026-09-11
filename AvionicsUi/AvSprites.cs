using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Generates and caches chamfered 9-sliced sprites using an octagonal signed distance field (SDF).
    /// Gives all avionics MFD bezels and controls their 5th-gen fighter cockpit aesthetic.
    /// </summary>
    public static class AvSprites
    {
        private static Sprite panelSprite;
        private static Sprite cardSprite;
        private static Sprite controlSprite;
        private static Sprite groundGradientSprite;

        /// <summary>Outer panel frame: 6px chamfer, 2px edge. The interior fades from
        /// ground-dark at the top to mostly see-through at the bottom — legible over a busy
        /// map without sitting as a flat opaque block behind sparse content.</summary>
        public static Sprite Panel => panelSprite != null ? panelSprite : (panelSprite = CreateChamferSprite("Avionics_Panel", 32, 6f, 2f, 8f, fillMode: FillMode.Gradient));

        /// <summary>Tactical card / tile frame: 3px chamfer, 1px edge.</summary>
        public static Sprite Card => cardSprite != null ? cardSprite : (cardSprite = CreateChamferSprite("Avionics_Card", 24, 3f, 1f, 6f, fillMode: FillMode.Solid));

        /// <summary>Control / button / chip frame: 2px chamfer, 1px edge.</summary>
        public static Sprite Control => controlSprite != null ? controlSprite : (controlSprite = CreateChamferSprite("Avionics_Control", 16, 2f, 1f, 4f, fillMode: FillMode.Solid));

        /// <summary>
        /// The same top-to-bottom fade as <see cref="Panel"/>, but flat and unframed — for
        /// grounds drawn behind content that already has its own frame (the game's own stock
        /// map panels), where a second chamfered border would double up.
        /// </summary>
        public static Sprite GroundGradient => groundGradientSprite != null ? groundGradientSprite : (groundGradientSprite = CreateGradientSprite("Avionics_GroundGradient", 64));

        public static void Reset()
        {
            if (panelSprite != null) { Object.Destroy(panelSprite.texture); Object.Destroy(panelSprite); panelSprite = null; }
            if (cardSprite != null) { Object.Destroy(cardSprite.texture); Object.Destroy(cardSprite); cardSprite = null; }
            if (controlSprite != null) { Object.Destroy(controlSprite.texture); Object.Destroy(controlSprite); controlSprite = null; }
            if (groundGradientSprite != null) { Object.Destroy(groundGradientSprite.texture); Object.Destroy(groundGradientSprite); groundGradientSprite = null; }
        }

        private enum FillMode { None, Solid, Gradient }

        /// <summary>Fraction of the way up the panel (0 bottom, 1 top) below which the fill
        /// stays at full alpha; the fade is spent entirely on the remaining bottom slice, so
        /// most of a panel reads as solid and only its lower edge admits what's behind it.</summary>
        private const float GradientOpaqueUntil = 0.20f;
        private const float GradientFloorFactor = 0.88f;

        private static Color FillAt(int y, int size, FillMode fillMode)
        {
            if (fillMode == FillMode.None) return Color.clear;

            Color ground = AvTheme.Ground;
            if (fillMode == FillMode.Solid) return ground;

            float t = size <= 1 ? 1f : y / (float)(size - 1);   // 0 at the bottom row, 1 at the top
            float fade = t >= GradientOpaqueUntil
                ? 1f
                : Mathf.Clamp01(t / GradientOpaqueUntil);
            ground.a *= Mathf.Lerp(GradientFloorFactor, 1f, fade);
            return ground;
        }

        private static Sprite CreateChamferSprite(string name, int size, float chamfer, float edge, float border, FillMode fillMode)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = size * 0.5f;
            float half = size * 0.5f;
            const float shadow = 1f;

            Color frame = AvTheme.Frame;
            Color shadowColor = AvTheme.Unity(AvTokens.PanelShadow);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    float outerDist = ChamferDistance(px, py, centre, half + shadow, chamfer + shadow);
                    float coverage = Mathf.Clamp01(0.5f - outerDist);
                    if (coverage <= 0f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float actualDist = ChamferDistance(px, py, centre, half, chamfer);
                    float innerHalf = half - edge;
                    float innerDist = ChamferDistance(px, py, centre, innerHalf, Mathf.Max(0.5f, chamfer - edge));

                    Color pixel = actualDist > 0.5f
                        ? shadowColor
                        : innerDist <= -0.5f ? FillAt(y, size, fillMode) : frame;

                    pixel.a *= coverage;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>An unframed, unsliced vertical fade — <see cref="Panel"/>'s fill without
        /// its chamfered border, for content that draws its own frame.</summary>
        private static Sprite CreateGradientSprite(string name, int size)
        {
            var texture = new Texture2D(1, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (int y = 0; y < size; y++)
                texture.SetPixel(0, y, FillAt(y, size, FillMode.Gradient));

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static float ChamferDistance(float x, float y, float centre, float half, float c)
        {
            float px = Mathf.Abs(x - centre);
            float py = Mathf.Abs(y - centre);
            float qx = px - half;
            float qy = py - half;
            float diag = (qx + qy + c) * 0.70710678f;
            return Mathf.Max(Mathf.Max(qx, qy), diag);
        }
    }
}
