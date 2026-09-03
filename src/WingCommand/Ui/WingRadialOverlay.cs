using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Modern tactical HUD command wheel for whole-wing flight control.
    ///
    /// Replaces the legacy IMGUI radial menu with an authentic, screen-space uGUI
    /// military avionics overlay. Features anti-aliased procedural vector icons,
    /// 10 non-overlapping ergonomically placed cards, real-time deflection reticle needle,
    /// central telemetry hub, live target inspection, and smooth alpha fading.
    /// </summary>
    internal static class WingRadialOverlay
    {
        private const float CanvasWidth = 1920f;
        private const float CanvasHeight = 1080f;

        private const float WheelRadius = 240f;
        private const float CardWidth = 136f;
        private const float CardHeight = 52f;
        private const float CenterHubRadius = 96f;

        private static GameObject canvasRoot;
        private static CanvasGroup canvasGroup;
        private static RectTransform centerRoot;
        private static RectTransform pointerRoot;
        private static Image pointerNeedle;
        private static Image reticleRing;
        private static Image backdropVignette;

        // Central hub telemetry
        private static TMP_Text hubTitle;
        private static TMP_Text hubStatus;
        private static TMP_Text hubPosture;
        private static TMP_Text orderTitle;
        private static TMP_Text orderBriefing;
        private static TMP_Text orderTarget;

        // Procedural sprites
        private static Sprite ringSprite;
        private static Sprite pointerSprite;
        private static Sprite vignetteSprite;

        // 10 radial card widgets
        private static readonly RadialCardWidget[] cards = new RadialCardWidget[10];
        private static int lastHoveredIndex = -2;

        private sealed class RadialCardWidget
        {
            public RectTransform Root;
            public Image Background;
            public Image Outline;
            public Image Icon;
            public TMP_Text Title;
            public TMP_Text Subtitle;
            public Vector2 BasePos;
            public float TargetScale = 1f;
            public float CurrentScale = 1f;
        }

        // ------------------------------------------------------------------ Lifecycle

        public static void Show(RadialSlice[] slices, int hoveredIndex, Vector2 delta, WingRegistry wing)
        {
            EnsureBuilt();
            if (canvasRoot == null) return;

            if (!canvasRoot.activeSelf)
            {
                canvasRoot.SetActive(true);
                lastHoveredIndex = -2;
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;

            UpdatePointer(delta, hoveredIndex);
            UpdateHub(slices, hoveredIndex, wing);
            UpdateCards(slices, hoveredIndex, wing);
        }

        public static void Hide()
        {
            lastHoveredIndex = -2;

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            if (canvasRoot != null && canvasRoot.activeSelf)
                canvasRoot.SetActive(false);
        }

        public static void Reset()
        {
            if (canvasRoot != null) UnityEngine.Object.Destroy(canvasRoot);
            canvasRoot = null;
            canvasGroup = null;
            centerRoot = null;
            pointerRoot = null;
            pointerNeedle = null;
            reticleRing = null;
            backdropVignette = null;

            for (int i = 0; i < cards.Length; i++) cards[i] = null;

            DestroySprite(ref ringSprite);
            DestroySprite(ref pointerSprite);
            DestroySprite(ref vignetteSprite);

            lastHoveredIndex = -2;
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite != null)
            {
                Texture2D tex = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                sprite = null;
            }
        }

        // ---------------------------------------------------------------- Build UI

        private static void EnsureBuilt()
        {
            if (canvasRoot != null) return;

            canvasRoot = new GameObject("WingCommand_RadialOverlay", typeof(RectTransform),
                                        typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(canvasRoot);

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2800; // Above cockpit HUD, below WMC map

            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGroup = canvasRoot.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            EnsureSprites();

            // Center container
            var centerGo = new GameObject("WheelCenter", typeof(RectTransform));
            centerRoot = centerGo.GetComponent<RectTransform>();
            centerRoot.SetParent(canvasRoot.transform, worldPositionStays: false);
            centerRoot.anchorMin = centerRoot.anchorMax = centerRoot.pivot = new Vector2(0.5f, 0.5f);
            centerRoot.anchoredPosition = Vector2.zero;
            centerRoot.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            centerRoot.localScale = Vector3.one;

            // Background soft vignette
            var vignGo = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            var vignRt = vignGo.GetComponent<RectTransform>();
            vignRt.SetParent(centerRoot, worldPositionStays: false);
            vignRt.anchorMin = vignRt.anchorMax = vignRt.pivot = new Vector2(0.5f, 0.5f);
            vignRt.anchoredPosition = Vector2.zero;
            vignRt.sizeDelta = new Vector2(760f, 760f);
            backdropVignette = vignGo.GetComponent<Image>();
            backdropVignette.sprite = vignetteSprite;
            backdropVignette.color = new Color(0.02f, 0.04f, 0.03f, 0.72f);
            backdropVignette.raycastTarget = false;

            // Reticle ring
            var ringGo = new GameObject("ReticleRing", typeof(RectTransform), typeof(Image));
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.SetParent(centerRoot, worldPositionStays: false);
            ringRt.anchorMin = ringRt.anchorMax = ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = Vector2.zero;
            ringRt.sizeDelta = new Vector2(CenterHubRadius * 2.2f, CenterHubRadius * 2.2f);
            reticleRing = ringGo.GetComponent<Image>();
            reticleRing.sprite = ringSprite;
            reticleRing.color = UiTheme.Green.WithAlpha(0.45f);
            reticleRing.raycastTarget = false;

            // Directional pointer needle
            var pointerGo = new GameObject("PointerNeedle", typeof(RectTransform), typeof(Image));
            pointerRoot = pointerGo.GetComponent<RectTransform>();
            pointerRoot.SetParent(centerRoot, worldPositionStays: false);
            pointerRoot.anchorMin = pointerRoot.anchorMax = pointerRoot.pivot = new Vector2(0.5f, 0.5f);
            pointerRoot.anchoredPosition = Vector2.zero;
            pointerRoot.sizeDelta = new Vector2(28f, 150f);
            pointerNeedle = pointerGo.GetComponent<Image>();
            pointerNeedle.sprite = pointerSprite;
            pointerNeedle.color = UiTheme.Green.WithAlpha(0.85f);
            pointerNeedle.raycastTarget = false;

            // Center hub telemetry
            BuildCenterHub(centerRoot);

            // 10 radial cards
            BuildCards(centerRoot);

            canvasRoot.SetActive(false);
        }

        private static void BuildCenterHub(RectTransform parent)
        {
            var hubGo = new GameObject("HubTelemetry", typeof(RectTransform));
            var hubRt = hubGo.GetComponent<RectTransform>();
            hubRt.SetParent(parent, worldPositionStays: false);
            hubRt.anchorMin = hubRt.anchorMax = hubRt.pivot = new Vector2(0.5f, 0.5f);
            hubRt.anchoredPosition = Vector2.zero;
            hubRt.sizeDelta = new Vector2(230f, 184f);

            // Hub plate background
            var hubPlateGo = new GameObject("HubPlate", typeof(RectTransform), typeof(Image));
            var hubPlateRt = hubPlateGo.GetComponent<RectTransform>();
            hubPlateRt.SetParent(hubRt, worldPositionStays: false);
            WingUi.Stretch(hubPlateRt);
            Image hubPlate = hubPlateGo.GetComponent<Image>();
            hubPlate.sprite = WingUi.PanelSprite();
            hubPlate.type = Image.Type.Sliced;
            hubPlate.color = new Color(0.02f, 0.05f, 0.03f, 0.90f);
            hubPlate.raycastTarget = false;

            // Hub outline
            var hubOutGo = new GameObject("HubOutline", typeof(RectTransform), typeof(Image));
            var hubOutRt = hubOutGo.GetComponent<RectTransform>();
            hubOutRt.SetParent(hubRt, worldPositionStays: false);
            WingUi.Stretch(hubOutRt);
            Image hubOut = hubOutGo.GetComponent<Image>();
            hubOut.sprite = WingUi.PanelSprite();
            hubOut.type = Image.Type.Sliced;
            hubOut.color = WingUi.BorderSubtle;
            hubOut.raycastTarget = false;

            hubTitle = CreateLabel(hubRt, "WING COMMAND", new Vector2(0f, 48f), 13f, FontStyles.Bold,
                                   UiTheme.Green, TextAlignmentOptions.Center);
            hubTitle.characterSpacing = 2.5f;

            hubStatus = CreateLabel(hubRt, "FLIGHT LEAD", new Vector2(0f, 28f), 10.5f, FontStyles.Normal,
                                    UiTheme.Friendly, TextAlignmentOptions.Center);

            hubPosture = CreateLabel(hubRt, "ROE: HOLD  ·  FORM: VIC", new Vector2(0f, 12f), 10f, FontStyles.Normal,
                                     WingUi.TextSecondary, TextAlignmentOptions.Center);

            // Hovered order briefing block (lower center)
            orderTitle = CreateLabel(hubRt, "SELECT ORDER", new Vector2(0f, -12f), 14.5f, FontStyles.Bold,
                                     Color.white, TextAlignmentOptions.Center);

            orderBriefing = CreateLabel(hubRt, "Hover an order to preview  ·  Center to cancel",
                                        new Vector2(0f, -34f), 10.5f, FontStyles.Italic,
                                        WingUi.TextSecondary, TextAlignmentOptions.Center);
            orderBriefing.enableWordWrapping = true;
            orderBriefing.rectTransform.sizeDelta = new Vector2(250f, 32f);

            orderTarget = CreateLabel(hubRt, "", new Vector2(0f, -56f), 10f, FontStyles.Bold,
                                      UiTheme.Warning, TextAlignmentOptions.Center);
        }

        private static void BuildCards(RectTransform parent)
        {
            float step = 360f / cards.Length;

            for (int i = 0; i < cards.Length; i++)
            {
                // Angle: 0 deg = Top (12 o'clock), clockwise
                float angleDeg = i * step;
                float angleRad = angleDeg * Mathf.Deg2Rad;

                float x = Mathf.Sin(angleRad) * WheelRadius;
                float y = Mathf.Cos(angleRad) * WheelRadius;

                var cardGo = new GameObject("Card_" + i, typeof(RectTransform));
                var cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.SetParent(parent, worldPositionStays: false);
                cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.anchoredPosition = new Vector2(x, y);
                cardRt.sizeDelta = new Vector2(CardWidth, CardHeight);
                cardRt.localScale = Vector3.one;

                // Background (uses native 9-slice panel sprite)
                var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
                var bgRt = bgGo.GetComponent<RectTransform>();
                bgRt.SetParent(cardRt, worldPositionStays: false);
                WingUi.Stretch(bgRt);
                Image bg = bgGo.GetComponent<Image>();
                bg.sprite = WingUi.PanelSprite();
                bg.type = Image.Type.Sliced;
                bg.color = WingUi.CardFill;
                bg.raycastTarget = false;

                // Outline frame
                var outGo = new GameObject("Outline", typeof(RectTransform), typeof(Image));
                var outRt = outGo.GetComponent<RectTransform>();
                outRt.SetParent(cardRt, worldPositionStays: false);
                WingUi.Stretch(outRt);
                Image outline = outGo.GetComponent<Image>();
                outline.sprite = WingUi.PanelSprite();
                outline.type = Image.Type.Sliced;
                outline.color = WingUi.BorderSubtle;
                outline.raycastTarget = false;

                // Icon (Left side)
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.SetParent(cardRt, worldPositionStays: false);
                iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(10f, 0f);
                iconRt.sizeDelta = new Vector2(32f, 32f);
                Image icon = iconGo.GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.color = Color.white;

                // Text block (Right side)
                float textLeft = 46f;
                float textWidth = CardWidth - textLeft - 6f;

                TMP_Text title = CreateLabel(cardRt, "", new Vector2(textLeft + textWidth * 0.5f - CardWidth * 0.5f, 8f),
                                             12f, FontStyles.Bold, WingUi.TextPrimary, TextAlignmentOptions.Left);
                title.rectTransform.sizeDelta = new Vector2(textWidth, 18f);

                TMP_Text subtitle = CreateLabel(cardRt, "", new Vector2(textLeft + textWidth * 0.5f - CardWidth * 0.5f, -9f),
                                                9.5f, FontStyles.Normal, WingUi.TextSecondary, TextAlignmentOptions.Left);
                subtitle.rectTransform.sizeDelta = new Vector2(textWidth, 16f);

                cards[i] = new RadialCardWidget
                {
                    Root = cardRt,
                    Background = bg,
                    Outline = outline,
                    Icon = icon,
                    Title = title,
                    Subtitle = subtitle,
                    BasePos = new Vector2(x, y),
                    TargetScale = 1f,
                    CurrentScale = 1f,
                };
            }
        }

        private static TMP_Text CreateLabel(RectTransform parent, string text, Vector2 pos, float size,
                                            FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(200f, 24f);

            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset resolved = WingUi.Font;
            if (resolved != null) t.font = resolved;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // ---------------------------------------------------------------- Dynamic Updates

        private static void UpdatePointer(Vector2 delta, int hoveredIndex)
        {
            if (pointerRoot == null || pointerNeedle == null) return;

            float mag = delta.magnitude;
            bool active = hoveredIndex >= 0 && mag > 0.15f;

            if (active)
            {
                float angle = -Vector2.SignedAngle(Vector2.up, delta.normalized);
                pointerRoot.localRotation = Quaternion.Euler(0f, 0f, -angle);
                float needleLen = Mathf.Clamp(CenterHubRadius + mag * 14f, CenterHubRadius, WheelRadius - 30f);
                pointerRoot.sizeDelta = new Vector2(24f, needleLen);
                pointerNeedle.color = UiTheme.Green.WithAlpha(Mathf.Clamp01(0.4f + mag * 0.4f));
            }
            else
            {
                pointerRoot.sizeDelta = new Vector2(18f, CenterHubRadius * 0.7f);
                pointerNeedle.color = UiTheme.Friendly.WithAlpha(0.20f);
            }
        }

        private static void UpdateHub(RadialSlice[] slices, int hoveredIndex, WingRegistry wing)
        {
            if (hubTitle == null) return;

            // Wing count & posture
            int count = wing?.Count ?? 0;
            hubStatus.text = count > 0
                ? $"[ {count} {(count == 1 ? "WINGMAN" : "WINGMEN")} AIRBORNE ]"
                : "[ NO WINGMEN ASSIGNED ]";

            string roeName = wing != null ? RoeRules.Label(wing.Roe).ToUpperInvariant() : "HOLD";
            string formName = FormationShapes.Pretty(WingFormation.Shape).ToUpperInvariant();
            hubPosture.text = $"ROE: {roeName}  ·  FORM: {formName}";

            // Hover details
            if (hoveredIndex >= 0 && hoveredIndex < slices.Length)
            {
                RadialSlice slice = slices[hoveredIndex];
                orderTitle.text = slice.Title;
                orderTitle.color = UiTheme.Green;
                orderBriefing.text = slice.Description;

                // Inspect locked target for combat actions
                if (slice.Action == WingAction.AttackMyTarget || slice.Action == WingAction.FireForEffect ||
                    slice.Action == WingAction.JamMyTarget)
                {
                    Unit target = GetPrimaryLockedTarget();
                    if (target != null && !target.disabled)
                    {
                        float dist = wing?.Leader != null
                            ? Vector3.Distance(wing.Leader.transform.position, target.transform.position) * 0.001f
                            : 0f;
                        orderTarget.text = $"LOCKED: {target.unitName.ToUpperInvariant()} · {dist:F1} KM";
                        orderTarget.color = UiTheme.Friendly;
                    }
                    else
                    {
                        orderTarget.text = "[ NO TARGET DESIGNATED ]";
                        orderTarget.color = UiTheme.Warning.WithAlpha(0.85f);
                    }
                }
                else
                {
                    orderTarget.text = "";
                }
            }
            else
            {
                orderTitle.text = "TACTICAL MENU";
                orderTitle.color = Color.white;
                orderBriefing.text = "Hover an order to select  ·  Center to cancel";
                orderTarget.text = "";
            }

            // Play audio tick on slice change
            if (hoveredIndex != lastHoveredIndex)
            {
                if (hoveredIndex >= 0 && lastHoveredIndex >= -1)
                {
                    PlayHoverCue();
                }
                lastHoveredIndex = hoveredIndex;
            }
        }

        private static void UpdateCards(RadialSlice[] slices, int hoveredIndex, WingRegistry wing)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                RadialCardWidget card = cards[i];
                if (card == null || i >= slices.Length) continue;

                RadialSlice slice = slices[i];
                bool isHovered = (i == hoveredIndex);

                // Contextual adjustments
                string title = slice.Title;
                string subtitle = slice.Subtitle;
                string iconKey = slice.IconKey;

                if (slice.Action == WingAction.CycleRoe && wing != null)
                {
                    WingRoe nextRoe = (WingRoe)(((int)wing.Roe + 1) % 3);
                    subtitle = "NEXT: " + RoeRules.Label(nextRoe).ToUpperInvariant();
                }
                else if (slice.Action == WingAction.CycleShape)
                {
                    FormationShape nextShape = FormationShapes.CycleCore(WingFormation.Shape, 1);
                    subtitle = "NEXT: " + FormationShapes.Pretty(nextShape).ToUpperInvariant();
                    iconKey = "shape_" + nextShape;
                }
                else if (slice.Action == WingAction.JamMyTarget)
                {
                    Unit target = GetPrimaryLockedTarget();
                    subtitle = (target != null && !target.disabled) ? "LOCK JAM" : "ELECTRONIC WARFARE";
                }

                card.Title.text = title;
                card.Subtitle.text = subtitle;

                Sprite iconSprite = IconFactory.Get(iconKey);
                if (iconSprite != null && card.Icon.sprite != iconSprite)
                    card.Icon.sprite = iconSprite;

                // Visual styling based on hover
                card.TargetScale = isHovered ? 1.08f : 1.0f;
                card.CurrentScale = Mathf.MoveTowards(card.CurrentScale, card.TargetScale, Time.unscaledDeltaTime * 10f);
                card.Root.localScale = Vector3.one * card.CurrentScale;

                if (isHovered)
                {
                    card.Background.color = WingUi.CardFillHover;
                    card.Outline.color = UiTheme.Green;
                    card.Title.color = Color.white;
                    card.Subtitle.color = UiTheme.Green;
                    card.Icon.color = Color.white;
                }
                else
                {
                    card.Background.color = WingUi.CardFill;
                    card.Outline.color = WingUi.BorderSubtle;
                    card.Title.color = WingUi.TextPrimary;
                    card.Subtitle.color = WingUi.TextSecondary;
                    card.Icon.color = UiTheme.Friendly.WithAlpha(0.85f);
                }
            }
        }

        private static Unit GetPrimaryLockedTarget()
        {
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud == null) return null;
                List<Unit> targets = hud.GetTargetList();
                if (targets != null && targets.Count > 0)
                {
                    foreach (Unit u in targets)
                    {
                        if (u != null && !u.disabled) return u;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void PlayHoverCue()
        {
            try
            {
                WingRadioAudio.Transmission();
            }
            catch { }
        }

        // ---------------------------------------------------------------- Procedural Sprites

        private static void EnsureSprites()
        {
            if (ringSprite == null)
                ringSprite = CreateReticleRingSprite("RadialReticleRing", 256, 110f, 2.0f);

            if (pointerSprite == null)
                pointerSprite = CreatePointerSprite("RadialPointer", 32, 128);

            if (vignetteSprite == null)
                vignetteSprite = CreateVignetteSprite("RadialVignette", 128);
        }

        private static Sprite CreateReticleRingSprite(string name, int size, float radius, float stroke)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float half = size * 0.5f;
            Color solid = Color.white;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Circular ring stroke
                    float distFromRing = Mathf.Abs(r - radius);
                    float ringAlpha = Mathf.Clamp01(stroke * 0.5f - distFromRing + 0.5f);

                    // Cardinal tick marks at 0, 90, 180, 270 degrees
                    float angleDeg = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg + 360f, 360f);
                    float tickDist = Mathf.Min(Mathf.Abs(angleDeg - 0f), Mathf.Abs(angleDeg - 90f),
                                               Mathf.Abs(angleDeg - 180f), Mathf.Abs(angleDeg - 270f),
                                               Mathf.Abs(angleDeg - 360f));

                    if (tickDist < 0.8f && r >= radius - 8f && r <= radius + 12f)
                    {
                        ringAlpha = Mathf.Max(ringAlpha, 1f);
                    }

                    Color c = solid;
                    c.a = ringAlpha;
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreatePointerSprite(string name, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float halfW = width * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float t = (float)y / height; // 0 at bottom, 1 at top tip
                float tipWidth = Mathf.Lerp(1.5f, halfW - 2f, Mathf.Sin(t * Mathf.PI));

                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - halfW);
                    if (dx <= tipWidth && y > 10)
                    {
                        float alpha = Mathf.Clamp01(tipWidth - dx + 0.5f) * t;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateVignetteSprite(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Smoothstep fade from center out
                    float alpha = Mathf.SmoothStep(1f, 0f, d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
