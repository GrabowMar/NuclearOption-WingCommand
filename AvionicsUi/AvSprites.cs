using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Cached, lightly chamfered panel and control masks. Widget masks are white so the
    /// shared palette is applied once by Image.color, rather than multiplied into baked ink.
    /// </summary>
    public static class AvSprites
    {
        private static Sprite panelSprite;
        private static Sprite cardSprite;
        private static Sprite controlSprite;
        private static Sprite controlFrameSprite;
        private static Sprite slotSprite;
        private static Sprite groundGradientSprite;
        private static Sprite displayGlassSprite;
        private static Sprite displayScreenSprite;
        private static Sprite ledSprite;
        private static Sprite whiteSprite;

        /// <summary>Untinted rectangle for Image.Filled, which requires an actual sprite.</summary>
        public static Sprite White => whiteSprite != null ? whiteSprite :
            (whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect));

        /// <summary>Outer panel frame with a 1.5px edge and nearly opaque ground;
        /// the subtle lower-edge fade preserves readability over a bright map.</summary>
        public static Sprite Panel => panelSprite != null ? panelSprite : (panelSprite = CreateChamferSprite("Avionics_Panel", 48, 7f, 1.5f, 10f, fillMode: FillMode.Gradient));

        /// <summary>Subtle 2px chamfer for a tinted card.</summary>
        public static Sprite Card => cardSprite != null ? cardSprite : (cardSprite = CreateChamferSprite("Avionics_Card", 32, 2f, 1f, 8f, fillMode: FillMode.Tinted));

        /// <summary>Legacy style alias; state emphasis now comes from the shared palette.</summary>
        public static Sprite GlowCard => Card;

        /// <summary>Flat tintable control, with a small chamfer shared by every button.</summary>
        public static Sprite Control => controlSprite != null ? controlSprite : (controlSprite = CreateChamferSprite("Avionics_Control", 24, 2f, 1f, 6f, fillMode: FillMode.Tinted));

        /// <summary>Tintable 1px outline with transparent fill.</summary>
        public static Sprite ControlFrame => controlFrameSprite != null ? controlFrameSprite : (controlFrameSprite = CreateChamferSprite("Avionics_ControlFrame", 24, 2f, 1f, 6f, fillMode: FillMode.None));

        /// <summary>Flat tintable slot for meters, sliders and inputs.</summary>
        public static Sprite Slot => slotSprite != null ? slotSprite : (slotSprite = CreateChamferSprite("Avionics_Slot", 24, 2f, 1f, 6f, fillMode: FillMode.Tinted));

        /// <summary>Radial diode indicator LED pip: hot luminous pure white core with steep photopic exponential bloom.</summary>
        public static Sprite Led => ledSprite != null ? ledSprite : (ledSprite = CreateLedSprite("Avionics_Led", 14));

        /// <summary>
        /// The same top-to-bottom fade as <see cref="Panel"/>, but flat and unframed — for
        /// grounds drawn behind content that already has its own frame (the game's own stock
        /// map panels), where a second chamfered border would double up.
        /// </summary>
        public static Sprite GroundGradient => groundGradientSprite != null ? groundGradientSprite : (groundGradientSprite = CreateGradientSprite("Avionics_GroundGradient", 64));

        /// <summary>One low-opacity glass finish for the entire MFD face. It is cached,
        /// so every screen shares the same tiny texture instead of baking a panel-sized one.</summary>
        public static Sprite DisplayGlass => displayGlassSprite != null ? displayGlassSprite :
            (displayGlassSprite = CreateDisplayGlassSprite());

        /// <summary>Fine, low-contrast finish for the complete maximized MFD canvas.</summary>
        public static Sprite DisplayScreen => displayScreenSprite != null ? displayScreenSprite :
            (displayScreenSprite = CreateDisplayScreenSprite());

        public static void Reset()
        {
            // whiteTexture belongs to Unity; only this wrapper sprite belongs to us.
            if (whiteSprite != null) { Object.Destroy(whiteSprite); whiteSprite = null; }
            if (panelSprite != null) { Object.Destroy(panelSprite.texture); Object.Destroy(panelSprite); panelSprite = null; }
            if (cardSprite != null) { Object.Destroy(cardSprite.texture); Object.Destroy(cardSprite); cardSprite = null; }
            if (controlSprite != null) { Object.Destroy(controlSprite.texture); Object.Destroy(controlSprite); controlSprite = null; }
            if (controlFrameSprite != null) { Object.Destroy(controlFrameSprite.texture); Object.Destroy(controlFrameSprite); controlFrameSprite = null; }
            if (slotSprite != null) { Object.Destroy(slotSprite.texture); Object.Destroy(slotSprite); slotSprite = null; }
            if (groundGradientSprite != null) { Object.Destroy(groundGradientSprite.texture); Object.Destroy(groundGradientSprite); groundGradientSprite = null; }
            if (displayGlassSprite != null) { Object.Destroy(displayGlassSprite.texture); Object.Destroy(displayGlassSprite); displayGlassSprite = null; }
            if (displayScreenSprite != null) { Object.Destroy(displayScreenSprite.texture); Object.Destroy(displayScreenSprite); displayScreenSprite = null; }
            if (ledSprite != null) { Object.Destroy(ledSprite.texture); Object.Destroy(ledSprite); ledSprite = null; }
        }

        private enum FillMode { None, Gradient, Tinted }

        /// <summary>Fraction of the way up the panel (0 bottom, 1 top) below which the fill
        /// stays at full alpha; the fade is spent entirely on the remaining bottom slice, so
        /// most of a panel reads as solid and only its lower edge admits what's behind it.</summary>
        private const float GradientOpaqueUntil = 0.20f;
        private const float GradientFloorFactor = 0.96f;

        private static Color FillAt(int y, int size, FillMode fillMode)
        {
            if (fillMode == FillMode.None) return Color.clear;
            if (fillMode == FillMode.Tinted) return Color.white;

            float t = size <= 1 ? 1f : y / (float)(size - 1);   // 0 at the bottom row, 1 at the top

            Color ground = AvTheme.Ground;

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

            Color defaultFrame = fillMode == FillMode.Gradient ? AvTheme.Unity(AvTokens.PanelEdge) : Color.white;
            Color shadowColor = fillMode == FillMode.Gradient ? AvTheme.Unity(AvTokens.PanelShadow) : Color.clear;

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
                        : innerDist <= -0.5f ? FillAt(y, size, fillMode) : defaultFrame;

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

        private static Sprite CreateDisplayGlassSprite()
        {
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "Avionics_DisplayGlass",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float edge = 1f - Mathf.SmoothStep(0f, 0.15f, edgeDistance);
                    float shade = 0.012f + edge * 0.036f + (1f - v) * 0.014f;

                    // A broad reflection and a softer diagonal lip, not scanlines over copy.
                    float dx = (u - 0.18f) / 0.58f;
                    float dy = (v - 0.94f) / 0.30f;
                    float reflection = 0.026f * Mathf.Exp(-2f * (dx * dx + dy * dy));
                    float diagonal = (v - (1.08f - u * 0.43f)) / 0.16f;
                    reflection += 0.018f * Mathf.Exp(-diagonal * diagonal);

                    float alpha = shade + reflection * (1f - shade);
                    float highlight = reflection / Mathf.Max(alpha, 0.001f);
                    texture.SetPixel(x, y, new Color(
                        highlight * 0.76f, highlight * 0.88f, highlight, alpha));
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateDisplayScreenSprite()
        {
            // One cached half-megabyte texture and one UI quad. There is no screen copy,
            // animated noise, postprocess camera, or per-frame texture upload.
            const int width = 256;
            const int height = 512;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "Avionics_DisplayScreen",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float edgeT = Mathf.Clamp01(edgeDistance / 0.15f);
                    float edge = 1f - edgeT * edgeT * (3f - 2f * edgeT);
                    float scan = (y & 1) == 0 ? 0.006f : 0f;
                    float grain = ((x * 73 + y * 151) % 17) / 17f * 0.002f;
                    float shade = 0.018f + edge * 0.060f + (1f - v) * 0.012f + scan + grain;

                    float dx = (u - 0.25f) / 0.56f;
                    float dy = (v - 0.96f) / 0.24f;
                    float glare = 0.018f + 0.060f * Mathf.Exp(-2f * (dx * dx + dy * dy));
                    float diagonal = (v - (1.12f - u * 0.36f)) / 0.11f;
                    glare += 0.025f * Mathf.Exp(-diagonal * diagonal);

                    // A very narrow red/cyan prism at opposite edges and split glare.
                    // The tint is in this transparent finish, not a displaced copy of UI pixels.
                    float redLip = 0.014f * Mathf.Exp(-u * u / 0.000045f);
                    float cyanLip = 0.014f * Mathf.Exp(-(1f - u) * (1f - u) / 0.000045f);
                    float alpha = shade + glare + redLip + cyanLip;
                    pixels[y * width + x] = (Color32)new Color(
                        (glare * 0.77f + redLip) / alpha,
                        (glare * 0.87f + cyanLip * 0.82f) / alpha,
                        (glare + cyanLip) / alpha,
                        alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateLedSprite(string name, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = (size - 1) * 0.5f;
            float maxR = centre;
            const float coreR = 2.2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > maxR)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float alpha;
                    if (dist <= coreR)
                    {
                        alpha = 1f;
                    }
                    else
                    {
                        float t = (dist - coreR) / (maxR - coreR);
                        alpha = Mathf.Exp(-t * 3.2f);
                    }

                    Color col = new Color(1f, 1f, 1f, alpha);
                    texture.SetPixel(x, y, col);
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
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
