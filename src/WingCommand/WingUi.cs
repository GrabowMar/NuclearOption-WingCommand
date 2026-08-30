using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// The widget vocabulary the mod's panels are drawn with: framed boxes, hairline rules,
    /// outlined buttons, and the sliced sprite that reproduces the stock panel card.
    ///
    /// All of this used to live as private statics inside <see cref="WmcScreen"/>, which is
    /// why the WMC page looked native and the aircraft-recovery prompt did not — the second
    /// surface could not reach the first one's drawing code, so it was written in IMGUI with
    /// a palette of its own invention. Nothing here is WMC-specific; it is simply where the
    /// stock look is defined.
    ///
    /// Colours come from <see cref="UiTheme"/>, which reads the game's live theme and falls
    /// back safely before the UI has initialised.
    /// </summary>
    internal static class WingUi
    {
        /// <summary>Standard control height, matching the stock panel rows.</summary>
        public const float RowHeight = 30f;

        /// <summary>Standard gap between adjacent controls.</summary>
        public const float Gap = 4f;

        /// <summary>Standard inset from a panel edge.</summary>
        public const float Pad = 12f;

        private static TMP_FontAsset font;
        private static Sprite panelSprite;

        /// <summary>
        /// The font every label is built with.
        ///
        /// Resolved from whatever TextMeshPro text the game already has on screen, so panels
        /// inherit the game's typeface rather than Unity's fallback. A caller that has a
        /// better template — the WMC page has an actual MFD screen to copy — assigns it.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;

                TMP_Text any = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
                font = any != null ? any.font : null;
                return font;
            }
            set { if (value != null) font = value; }
        }

        /// <summary>Drop the cached font when a mission ends; the next scene resolves its own.</summary>
        public static void Reset() => font = null;

        // ---------------------------------------------------------------------- colours

        /// <summary>The stock "on" colour: what HUD OPTIONS uses for an active control.</summary>
        public static Color Green => UiTheme.Green;

        /// <summary>The stock "off" colour.</summary>
        public static Color Grey => Color.grey;

        public static Color Friendly => UiTheme.Friendly;

        public static Color Warning => UiTheme.Warning;

        public static Color Alert => UiTheme.Alert;

        /// <summary>Secondary text: present, but not asking to be read.</summary>
        public static Color Dim => Grey;

        /// <summary>A frame that should recede behind its contents.</summary>
        public static Color FrameColor => new Color(Grey.r, Grey.g, Grey.b, 0.75f);

        private static Color PanelBackground => new Color(0.075f, 0.12f, 0.16f, 0.84f);
        private static Color PanelEdge => new Color(0.33f, 0.33f, 0.33f, 1f);
        private static Color PanelShadow => new Color(0.24f, 0.24f, 0.24f, 1f);

        // ---------------------------------------------------------------------- widgets

        public static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                     Color color, float size, FontStyles style,
                                     TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset resolved = Font;
            if (resolved != null) t.font = resolved;
            t.text = text;
            t.color = color;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>
        /// A framed box: a near-transparent interior with the colour carried on the edges,
        /// because the stock panels are outlined boxes rather than filled blocks.
        /// </summary>
        public static Image Panel(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);
            rt.SetAsFirstSibling();

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0f, 0f, 0f, 0.25f);

            Outline(parent, rect, color);
            return img;
        }

        /// <summary>Four hairline edges forming a box.</summary>
        public static Image[] Outline(RectTransform parent, Rect rect, Color color)
        {
            const float t = 1f;
            return new[]
            {
                Rule(parent, new Rect(rect.x, rect.y, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y - rect.height + t, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y, t, rect.height), color),
                Rule(parent, new Rect(rect.x + rect.width - t, rect.y, t, rect.height), color),
            };
        }

        public static Image Rule(RectTransform parent, Rect rect) => Rule(parent, rect, Green);

        public static Image Rule(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// An outlined button in the stock idiom: grey frame and text at rest, green when
        /// hovered — the same on/off treatment HUD OPTIONS gives its MAXIMIZE buttons.
        /// </summary>
        public static WingButton Button(RectTransform parent, string text, Rect rect, Action onClick) =>
            Button(parent, text, rect, 12f, onClick);

        public static WingButton Button(RectTransform parent, string text, Rect rect,
                                        float fontSize, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            // A near-transparent fill keeps the whole rect clickable while the frame does the
            // drawing, so the button reads as an outline like the stock controls.
            Image img = go.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0f, 0f, 0f, 0.30f);

            Image[] frame = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), Grey);

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                                   Grey, fontSize, FontStyles.Normal,
                                   TextAlignmentOptions.Center);

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.Initialise(frame, label, onClick);
            return behaviour;
        }

        /// <summary>An invisible click target over an area that draws itself.</summary>
        public static WingButton HitButton(RectTransform parent, Rect rect, Action onClick)
        {
            var go = new GameObject("HitTarget", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image hit = go.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            WingButton behaviour = go.AddComponent<WingButton>();
            behaviour.Initialise(null, null, onClick);
            return behaviour;
        }

        /// <summary>
        /// Section heading with a rule running out to the right of it, which is how the stock
        /// panels separate their groups.
        /// </summary>
        public static float Heading(RectTransform parent, float y, string text, float width)
        {
            float labelWidth = 8f * text.Length + 10f;

            Label(parent, text, new Rect(Pad, y, labelWidth, 16f),
                  Green, 11f, FontStyles.Normal, TextAlignmentOptions.Left);

            float ruleX = Pad + labelWidth + 6f;
            Rule(parent, new Rect(ruleX, y - 8f, width - Pad - ruleX, 1f), FrameColor);

            return y - 20f;
        }

        /// <summary>Anchor a rect to the parent's top-left and place it in pixels.</summary>
        public static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.localScale = Vector3.one;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // ----------------------------------------------------------------- panel sprite

        /// <summary>
        /// Reproduce the stock mission/menu card: a translucent slate fill, a soft grey
        /// two-pixel edge, and small rounded corners. A sliced sprite keeps the treatment
        /// consistent at any panel size or resolution.
        /// </summary>
        public static Sprite PanelSprite()
        {
            if (panelSprite != null) return panelSprite;

            const int size = 32;
            const float radius = 5f;
            const float edge = 3f;
            const float shadow = 1f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_VanillaPanel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = size * 0.5f;
            float half = size * 0.5f;
            Color fill = PanelBackground;
            Color frame = PanelEdge;
            Color shadowColor = PanelShadow;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, half + shadow, radius + shadow);
                    float coverage = Mathf.Clamp01(0.5f - outer);
                    if (coverage <= 0f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float actual = RoundedDistance(x + 0.5f, y + 0.5f, centre, half, radius);
                    float innerHalf = half - edge;
                    float inner = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, innerHalf, Mathf.Max(1f, radius - edge));
                    Color pixel = actual > 0.5f
                        ? shadowColor
                        : inner <= -0.5f ? fill : frame;
                    pixel.a *= coverage;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            panelSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));
            panelSprite.name = "WingCommand_VanillaPanelSprite";
            panelSprite.hideFlags = HideFlags.HideAndDontSave;
            return panelSprite;
        }

        private static float RoundedDistance(float x, float y, float centre,
                                             float half, float radius)
        {
            float qx = Mathf.Abs(x - centre) - (half - radius);
            float qy = Mathf.Abs(y - centre) - (half - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }
    }

    /// <summary>Minimal clickable button with a hover tint, so no stock UI script is reused.</summary>
    internal class WingButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image[] frame;
        private TMP_Text label;
        private Action onClick;
        private bool latched;
        private bool interactable = true;

        public void Initialise(Image[] frame, TMP_Text label, Action onClick)
        {
            this.frame = frame;
            this.label = label;
            this.onClick = onClick;
            Tint(WingUi.Green);
        }

        /// <summary>Hold this button lit, for a selected option in a group.</summary>
        public void SetLatched(bool on)
        {
            if (latched == on) return;
            latched = on;
            Tint(on ? Color.white : WingUi.Green);
        }

        public void SetEnabled(bool on)
        {
            if (interactable == on) return;
            interactable = on;
            Tint(Resting());
        }

        /// <summary>Set the label without rebuilding the button.</summary>
        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        private Color Resting()
        {
            if (!interactable) return new Color(0.3f, 0.3f, 0.3f);
            return latched ? Color.white : WingUi.Green;
        }

        private void Tint(Color color)
        {
            if (frame != null)
            {
                foreach (Image edge in frame)
                {
                    if (edge != null) edge.color = color;
                }
            }
            if (label != null) label.color = color;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!interactable) return;

            try { onClick?.Invoke(); }
            catch (Exception e) { Plugin.Logger.LogError("Wing UI button failed: " + e); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (interactable) Tint(Color.white);
        }

        public void OnPointerExit(PointerEventData eventData) => Tint(Resting());
    }
}
