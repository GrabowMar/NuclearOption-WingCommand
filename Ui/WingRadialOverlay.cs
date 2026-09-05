using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>
    /// Standalone uGUI command wheel. The stock <c>RadialMenuMain</c> cannot carry
    /// wing orders without invasive patching, so this draws the six whole-wing
    /// slices on its own canvas.
    /// </summary>
    internal static class WingRadialOverlay
    {
        private const float CanvasWidth = 1920f;
        private const float CanvasHeight = 1080f;

        private const float WheelRadius = 200f;
        private const float CenterHubRadius = 82f;
        private const float IconRadius = 142f;

        private const int SectorCount = 6;
        private const float DegreesPerSector = 360f / SectorCount; // 60 deg

        private static Color SectorRestingColor => WingUi.SurfaceCard;
        private static Color SectorHoveredColor => WingUi.CardFillHover;
        private static Color SectorDividerColor => WingUi.BorderSubtle;
        private static Color OuterBorderColor => WingUi.BorderSubtle;
        private static Color OuterArcColor => WingUi.RailEmerald;
        private static Color HubBackgroundColor => AvTheme.Ground;
        private static Color HubBorderColor => WingUi.BorderSubtle;
        private static Color IconRestingColor => WingUi.Dim;
        private static Color HubSubtitleColor => WingUi.Dim;

        private static GameObject canvasRoot;
        private static CanvasGroup canvasGroup;
        private static RectTransform centerRoot;

        // Visual layers
        private static Image backdropVignette;
        private static Image outerRingBorder;
        private static Image outerArcHighlight;
        private static Image hubBackground;
        private static Image hubBorder;

        // Central hub telemetry text
        private static TMP_Text hubTitle;
        private static TMP_Text hubSubtitle;

        // Procedural sprites
        private static Sprite discSprite;
        private static Sprite ringSprite;
        private static Sprite thickRingSprite;
        private static Sprite vignetteSprite;
        private static Sprite solidSprite;

        // 6 radial sector widgets
        private static readonly SectorWidget[] sectors = new SectorWidget[SectorCount];
        private static int lastHoveredIndex = -2;

        private sealed class SectorWidget
        {
            public RectTransform Root;
            public Image Fill;
            public Image Icon;
            public float TargetScale = 1f;
            public float CurrentScale = 1f;
        }

        // ------------------------------------------------------------------ Lifecycle

        public static void Show(RadialSlice[] slices, int hoveredIndex, WingRegistry wing)
        {
            EnsureBuilt();
            if (canvasRoot == null) return;

            if (!canvasRoot.activeSelf)
            {
                canvasRoot.SetActive(true);
                lastHoveredIndex = -2;
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;

            UpdateHub(slices, hoveredIndex, wing);
            UpdateSectors(slices, hoveredIndex);
            UpdateOuterArc(hoveredIndex);
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
            backdropVignette = null;
            outerRingBorder = null;
            outerArcHighlight = null;
            hubBackground = null;
            hubBorder = null;
            hubTitle = null;
            hubSubtitle = null;

            for (int i = 0; i < sectors.Length; i++) sectors[i] = null;

            DestroySprite(ref discSprite);
            DestroySprite(ref ringSprite);
            DestroySprite(ref thickRingSprite);
            DestroySprite(ref vignetteSprite);
            DestroySprite(ref solidSprite);

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
            centerRoot.sizeDelta = new Vector2(WheelRadius * 2.5f, WheelRadius * 2.5f);
            centerRoot.localScale = Vector3.one;

            // Background soft vignette
            var vignGo = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            var vignRt = vignGo.GetComponent<RectTransform>();
            vignRt.SetParent(centerRoot, worldPositionStays: false);
            vignRt.anchorMin = vignRt.anchorMax = vignRt.pivot = new Vector2(0.5f, 0.5f);
            vignRt.anchoredPosition = Vector2.zero;
            vignRt.sizeDelta = new Vector2(WheelRadius * 2.6f, WheelRadius * 2.6f);
            backdropVignette = vignGo.GetComponent<Image>();
            backdropVignette.sprite = vignetteSprite;
            backdropVignette.color = new Color(0f, 0f, 0f, 0.40f);
            backdropVignette.raycastTarget = false;

            // Build circular sectors and dividers
            BuildSectors(centerRoot);

            // Outer ring border
            var borderGo = new GameObject("OuterBorder", typeof(RectTransform), typeof(Image));
            var borderRt = borderGo.GetComponent<RectTransform>();
            borderRt.SetParent(centerRoot, worldPositionStays: false);
            borderRt.anchorMin = borderRt.anchorMax = borderRt.pivot = new Vector2(0.5f, 0.5f);
            borderRt.anchoredPosition = Vector2.zero;
            borderRt.sizeDelta = new Vector2(WheelRadius * 2f, WheelRadius * 2f);
            outerRingBorder = borderGo.GetComponent<Image>();
            outerRingBorder.sprite = ringSprite;
            outerRingBorder.color = OuterBorderColor;
            outerRingBorder.raycastTarget = false;

            // Outer highlight arc indicator (hovered sector)
            var arcGo = new GameObject("OuterArcHighlight", typeof(RectTransform), typeof(Image));
            var arcRt = arcGo.GetComponent<RectTransform>();
            arcRt.SetParent(centerRoot, worldPositionStays: false);
            arcRt.anchorMin = arcRt.anchorMax = arcRt.pivot = new Vector2(0.5f, 0.5f);
            arcRt.anchoredPosition = Vector2.zero;
            arcRt.sizeDelta = new Vector2(WheelRadius * 2f, WheelRadius * 2f);
            outerArcHighlight = arcGo.GetComponent<Image>();
            outerArcHighlight.sprite = thickRingSprite;
            outerArcHighlight.type = Image.Type.Filled;
            outerArcHighlight.fillMethod = Image.FillMethod.Radial360;
            outerArcHighlight.fillOrigin = (int)Image.Origin360.Top;
            outerArcHighlight.fillClockwise = true;
            outerArcHighlight.fillAmount = (DegreesPerSector - 2f) / 360f; // Gap across dividers
            outerArcHighlight.color = OuterArcColor;
            outerArcHighlight.raycastTarget = false;
            outerArcHighlight.gameObject.SetActive(false);

            // Center hub (draws on top of sectors, creating the donut shape)
            BuildCenterHub(centerRoot);

            canvasRoot.SetActive(false);
        }

        private static void BuildSectors(RectTransform parent)
        {
            var sectorsContainer = new GameObject("Sectors", typeof(RectTransform));
            var containerRt = sectorsContainer.GetComponent<RectTransform>();
            containerRt.SetParent(parent, worldPositionStays: false);
            containerRt.anchorMin = containerRt.anchorMax = containerRt.pivot = new Vector2(0.5f, 0.5f);
            containerRt.anchoredPosition = Vector2.zero;
            containerRt.sizeDelta = new Vector2(WheelRadius * 2f, WheelRadius * 2f);

            // 1. Sector fill wedges
            for (int i = 0; i < SectorCount; i++)
            {
                var sectorGo = new GameObject("Sector_" + i, typeof(RectTransform), typeof(Image));
                var sectorRt = sectorGo.GetComponent<RectTransform>();
                sectorRt.SetParent(containerRt, worldPositionStays: false);
                sectorRt.anchorMin = sectorRt.anchorMax = sectorRt.pivot = new Vector2(0.5f, 0.5f);
                sectorRt.anchoredPosition = Vector2.zero;
                sectorRt.sizeDelta = new Vector2(WheelRadius * 2f, WheelRadius * 2f);

                // Sector 0 is centered at 0 deg (12 o'clock, spanning -30 to +30)
                sectorRt.localRotation = Quaternion.Euler(0f, 0f, 30f - i * DegreesPerSector);

                Image fill = sectorGo.GetComponent<Image>();
                fill.sprite = discSprite;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.fillAmount = DegreesPerSector / 360f;
                fill.color = SectorRestingColor;
                fill.raycastTarget = false;

                sectors[i] = new SectorWidget
                {
                    Root = sectorRt,
                    Fill = fill,
                };
            }

            // 2. Radial divider lines
            for (int i = 0; i < SectorCount; i++)
            {
                float angleDeg = i * DegreesPerSector - 30f;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                float dividerLength = WheelRadius - CenterHubRadius;

                var divGo = new GameObject("Divider_" + i, typeof(RectTransform), typeof(Image));
                var divRt = divGo.GetComponent<RectTransform>();
                divRt.SetParent(containerRt, worldPositionStays: false);
                divRt.anchorMin = divRt.anchorMax = new Vector2(0.5f, 0.5f);
                divRt.pivot = new Vector2(0.5f, 0f);
                divRt.anchoredPosition = new Vector2(Mathf.Sin(angleRad) * CenterHubRadius,
                                                     Mathf.Cos(angleRad) * CenterHubRadius);
                divRt.sizeDelta = new Vector2(1.5f, dividerLength);
                divRt.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);

                Image divImg = divGo.GetComponent<Image>();
                divImg.sprite = solidSprite;
                divImg.color = SectorDividerColor;
                divImg.raycastTarget = false;
            }

            // 3. Sector icons (centered inside each sector)
            for (int i = 0; i < SectorCount; i++)
            {
                float iconAngleDeg = i * DegreesPerSector;
                float iconAngleRad = iconAngleDeg * Mathf.Deg2Rad;
                Vector2 iconPos = new Vector2(Mathf.Sin(iconAngleRad) * IconRadius,
                                              Mathf.Cos(iconAngleRad) * IconRadius);

                var iconGo = new GameObject("Icon_" + i, typeof(RectTransform), typeof(Image));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.SetParent(containerRt, worldPositionStays: false);
                iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
                iconRt.anchoredPosition = iconPos;
                iconRt.sizeDelta = new Vector2(36f, 36f);

                Image iconImg = iconGo.GetComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.color = IconRestingColor;
                iconImg.raycastTarget = false;

                sectors[i].Icon = iconImg;
            }
        }

        private static void BuildCenterHub(RectTransform parent)
        {
            float hubDiameter = CenterHubRadius * 2f;

            var hubGo = new GameObject("CenterHub", typeof(RectTransform));
            var hubRt = hubGo.GetComponent<RectTransform>();
            hubRt.SetParent(parent, worldPositionStays: false);
            hubRt.anchorMin = hubRt.anchorMax = hubRt.pivot = new Vector2(0.5f, 0.5f);
            hubRt.anchoredPosition = Vector2.zero;
            hubRt.sizeDelta = new Vector2(hubDiameter, hubDiameter);

            // Hub plate background
            var plateGo = new GameObject("HubPlate", typeof(RectTransform), typeof(Image));
            var plateRt = plateGo.GetComponent<RectTransform>();
            plateRt.SetParent(hubRt, worldPositionStays: false);
            WingUi.Stretch(plateRt);
            hubBackground = plateGo.GetComponent<Image>();
            hubBackground.sprite = discSprite;
            hubBackground.color = HubBackgroundColor;
            hubBackground.raycastTarget = false;

            // Hub outline border
            var outGo = new GameObject("HubBorder", typeof(RectTransform), typeof(Image));
            var outRt = outGo.GetComponent<RectTransform>();
            outRt.SetParent(hubRt, worldPositionStays: false);
            WingUi.Stretch(outRt);
            hubBorder = outGo.GetComponent<Image>();
            hubBorder.sprite = ringSprite;
            hubBorder.color = HubBorderColor;
            hubBorder.raycastTarget = false;

            // Centered order title (e.g. "TOGGLE OVERLAYS" style)
            hubTitle = CreateLabel(hubRt, "WING COMMAND", new Vector2(0f, 9f), 15f, FontStyles.Bold,
                                   Color.white, TextAlignmentOptions.Center);
            hubTitle.characterSpacing = 1.8f;

            // Subtitle hint / status
            hubSubtitle = CreateLabel(hubRt, "FLIGHT LEAD", new Vector2(0f, -11f), 10.5f, FontStyles.Normal,
                                      HubSubtitleColor, TextAlignmentOptions.Center);
            hubSubtitle.characterSpacing = 1.0f;
        }

        private static TMP_Text CreateLabel(RectTransform parent, string text, Vector2 pos, float size,
                                            FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(CenterHubRadius * 1.85f, 24f);

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

        private static void UpdateHub(RadialSlice[] slices, int hoveredIndex, WingRegistry wing)
        {
            if (hubTitle == null || hubSubtitle == null) return;

            int count = wing?.Count ?? 0;

            if (hoveredIndex >= 0 && hoveredIndex < slices.Length)
            {
                RadialSlice slice = slices[hoveredIndex];
                hubTitle.text = slice.Title;
                hubTitle.color = Color.white;

                // Subtitle context
                if (slice.Action == WingAction.CycleRoe && wing != null)
                {
                    WingRoe nextRoe = RoeRules.Next(wing.Roe);
                    hubSubtitle.text = "NEXT: " + RoeRules.Label(nextRoe);
                    hubSubtitle.color = AvTheme.Accent;
                }
                else if (slice.Action == WingAction.AttackMyTarget || slice.Action == WingAction.Engage)
                {
                    Unit target = GetPrimaryLockedTarget();
                    if (target != null && !target.disabled)
                    {
                        float dist = wing?.Leader != null
                            ? Vector3.Distance(wing.Leader.transform.position, target.transform.position) * 0.001f
                            : 0f;
                        hubSubtitle.text = $"LOCK: {target.unitName.ToUpperInvariant()} · {dist:F1} KM";
                        hubSubtitle.color = AvTheme.Friendly;
                    }
                    else
                    {
                        hubSubtitle.text = slice.Subtitle;
                        hubSubtitle.color = HubSubtitleColor;
                    }
                }
                else
                {
                    hubSubtitle.text = slice.Subtitle;
                    hubSubtitle.color = HubSubtitleColor;
                }
            }
            else
            {
                hubTitle.text = "WING COMMAND";
                hubTitle.color = Color.white;
                hubSubtitle.text = count > 0
                    ? $"{count} {(count == 1 ? "WINGMAN" : "WINGMEN")} ACTIVE"
                    : "TACTICAL MENU";
                hubSubtitle.color = HubSubtitleColor;
            }

            // Audio click cue on slice transition
            if (hoveredIndex != lastHoveredIndex)
            {
                if (hoveredIndex >= 0 && lastHoveredIndex >= -1)
                {
                    PlayHoverCue();
                }
                lastHoveredIndex = hoveredIndex;
            }
        }

        private static void UpdateSectors(RadialSlice[] slices, int hoveredIndex)
        {
            for (int i = 0; i < SectorCount; i++)
            {
                SectorWidget sec = sectors[i];
                if (sec == null) continue;

                bool isHovered = (i == hoveredIndex);

                // Set icon sprite from slice
                if (i < slices.Length)
                {
                    RadialSlice slice = slices[i];
                    Sprite iconSprite = IconFactory.Get(slice.IconKey);
                    if (iconSprite != null && sec.Icon.sprite != iconSprite)
                        sec.Icon.sprite = iconSprite;
                }

                // Sector wedge fill highlight
                Color targetFill = isHovered ? SectorHoveredColor : SectorRestingColor;
                sec.Fill.color = Color.Lerp(sec.Fill.color, targetFill, Time.unscaledDeltaTime * 14f);

                // Icon scale & color
                sec.TargetScale = isHovered ? 1.14f : 1.0f;
                sec.CurrentScale = Mathf.MoveTowards(sec.CurrentScale, sec.TargetScale, Time.unscaledDeltaTime * 8f);
                sec.Icon.rectTransform.localScale = Vector3.one * sec.CurrentScale;

                Color targetIconColor = isHovered ? Color.white : IconRestingColor;
                sec.Icon.color = Color.Lerp(sec.Icon.color, targetIconColor, Time.unscaledDeltaTime * 14f);
            }
        }

        private static void UpdateOuterArc(int hoveredIndex)
        {
            if (outerArcHighlight == null) return;

            if (hoveredIndex >= 0 && hoveredIndex < SectorCount)
            {
                if (!outerArcHighlight.gameObject.activeSelf)
                    outerArcHighlight.gameObject.SetActive(true);

                // Align arc to the hovered 60 deg sector with a subtle 1 deg margin
                outerArcHighlight.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, 30f - hoveredIndex * DegreesPerSector + 1.0f);
            }
            else
            {
                if (outerArcHighlight.gameObject.activeSelf)
                    outerArcHighlight.gameObject.SetActive(false);
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
            if (discSprite == null)
                discSprite = CreateFilledCircleSprite("RadialDisc", 256);

            if (ringSprite == null)
                ringSprite = CreateRingSprite("RadialRing", 256, 126f, 1.5f);

            if (thickRingSprite == null)
                thickRingSprite = CreateRingSprite("RadialThickRing", 256, 125f, 4.0f);

            if (vignetteSprite == null)
                vignetteSprite = CreateVignetteSprite("RadialVignette", 128);

            if (solidSprite == null)
                solidSprite = CreateSolidSprite("RadialSolid", 4, 4);
        }

        private static Sprite CreateFilledCircleSprite(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float half = size * 0.5f;
            float rMax = half - 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(rMax - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateRingSprite(string name, int size, float radius, float stroke)
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
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float distFromRing = Mathf.Abs(d - radius);
                    float alpha = Mathf.Clamp01(stroke * 0.5f - distFromRing + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
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

        private static Sprite CreateSolidSprite(string name, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    tex.SetPixel(x, y, Color.white);

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
