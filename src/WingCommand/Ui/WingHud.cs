using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Native HUD rendering for the compact wing strip, plus IMGUI fallbacks for the
    /// standalone radial menu and transient messages.
    ///
    /// The stock <c>RadialMenuMain</c> is a SceneSingleton driven by a fixed
    /// <c>RadialMenuAction.ActionType</c> enum and weapon-bound ScriptableObjects, so it
    /// cannot carry wing orders without invasive patching. IMGUI is used instead.
    /// </summary>
    internal static class WingHud
    {
        private const float RadialRadius = 150f;
        private const float SliceWidth = 108f;
        private const float SliceHeight = 54f;

        private static bool stylesReady;
        private static GUIStyle sliceStyle;
        private static GUIStyle sliceHotStyle;
        private static GUIStyle toastStyle;

        // Read through UiPalette rather than restated here. The accent in particular was a
        // hand-copied duplicate of UiTheme.Friendly's fallback, so the HUD kept the stock
        // green even in a mission whose theme had moved off it.
        private static Color Panel => WingUi.Unity(UiPalette.HudPanel);
        private static Color Accent => UiTheme.Friendly;
        private static Color Hot => WingUi.Unity(UiPalette.HudSliceHot);
        private static Color Cold => WingUi.Unity(UiPalette.HudSliceCold);

        private static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            sliceStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
            };
            sliceStyle.normal.background = Solid(Cold);
            sliceStyle.normal.textColor = WingUi.Unity(UiPalette.HudSliceText);

            sliceHotStyle = new GUIStyle(sliceStyle);
            sliceHotStyle.normal.background = Solid(Hot);
            sliceHotStyle.normal.textColor = Color.white;
            sliceHotStyle.fontStyle = FontStyle.Bold;

            toastStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
            };
            toastStyle.normal.background = Solid(Panel);
            toastStyle.normal.textColor = Accent;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private const float StatusHeaderHeight = 24f;
        private const float StatusRowHeight = 30f;
        private const float StatusPanelWidth = 210f;
        private const float StatusMapGap = 0f;
        private const float StatusBackdropOutsideFeather = 18f;
        private const float StatusBackdropInnerFeather = 12f;
        private const float StatusBackdropSeamOverlap = 18f;
        private const int StatusBackdropTextureSize = 80;

        // Three type sizes, named and reused everywhere, rather than a different number per
        // label. The strip previously ran a 14 px callsign against a 9 px state code, a
        // contrast wider than anything in the game's own symbology, which is a large part of
        // why it read as a separate overlay rather than as part of the HUD.

        /// <summary>The strip's own heading.</summary>
        private const float HeaderText = 11f;

        /// <summary>Callsign and range: what the strip is actually read for.</summary>
        private const float PrimaryText = 13f;

        /// <summary>Order code and other supporting detail.</summary>
        private const float SecondaryText = 10f;

        private static float statusWidth = StatusPanelWidth;

        private static RectTransform statusRoot;
        private static TMP_Text statusTitle;
        private static CombatHUD statusHud;
        private static Canvas statusCanvas;
        private static TMP_FontAsset statusFont;
        private static Sprite statusBackdropSprite;
        private static float nextStatusRefresh;
        private static int lastStatusCount = -1;
        private static readonly List<StatusRow> statusRows = new List<StatusRow>();

        /// <summary>
        /// Build the roster inside the game's HUD canvas. This gives it the same scaling,
        /// font rendering, theme changes and resolution handling as stock symbology.
        /// </summary>
        public static void TickStatusPanel(WingRegistry wing)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            bool visible = Plugin.Settings.ShowHud.Value && wing.Count > 0 &&
                           !DynamicMap.mapMaximized && hud != null && hud.isActiveAndEnabled &&
                           map != null && map.gameObject.activeInHierarchy;
            if (!visible)
            {
                if (statusRoot != null) statusRoot.gameObject.SetActive(false);
                return;
            }

            // The HUD and its canvas are scene objects. Resolve the hierarchy only when the
            // panel is first built or the scene supplies a different HUD instance.
            Canvas canvas = statusRoot != null && statusHud == hud
                ? statusCanvas
                : hud.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                if (statusRoot != null) statusRoot.gameObject.SetActive(false);
                return;
            }

            if (statusRoot == null || statusCanvas != canvas)
            {
                ResetStatusPanel();
                BuildStatusPanel(hud, canvas, map);
            }

            if (statusRoot == null) return;
            if (!statusRoot.gameObject.activeSelf) statusRoot.gameObject.SetActive(true);

            PositionStatusPanel(map);

            if (Time.unscaledTime < nextStatusRefresh && lastStatusCount == wing.Count) return;
            nextStatusRefresh = Time.unscaledTime + 0.2f;
            lastStatusCount = wing.Count;
            RefreshStatusPanel(wing);
        }

        public static void ResetStatusPanel()
        {
            if (statusRoot != null) Object.Destroy(statusRoot.gameObject);
            statusRoot = null;
            statusTitle = null;
            statusHud = null;
            statusCanvas = null;
            statusFont = null;
            statusRows.Clear();
            if (statusBackdropSprite != null)
            {
                Texture2D texture = statusBackdropSprite.texture;
                Object.Destroy(statusBackdropSprite);
                if (texture != null) Object.Destroy(texture);
                statusBackdropSprite = null;
            }
            nextStatusRefresh = 0f;
            lastStatusCount = -1;
        }

        private static void BuildStatusPanel(CombatHUD hud, Canvas canvas, DynamicMap map)
        {
            TMP_Text template = hud.GetComponentInChildren<TMP_Text>(includeInactive: true);
            statusFont = template != null ? template.font : null;
            statusHud = hud;
            statusCanvas = canvas;
            statusWidth = StatusPanelWidth;

            var root = new GameObject("WingCommand_Status", typeof(RectTransform));
            statusRoot = root.GetComponent<RectTransform>();
            // The minimized map already has a dedicated HUD anchor. Sharing that parent
            // avoids translating between the map canvas and the flight-HUD canvas, which
            // placed the panel off-screen on screen-space-camera HUDs.
            statusRoot.SetParent(map.hudMapAnchor, worldPositionStays: false);
            statusRoot.SetAsLastSibling();

            CreateStatusBackdrop(statusRoot, map);

            statusTitle = StatusLabel(statusRoot, "", new Rect(7f, -2f, statusWidth - 14f, 20f),
                                      HeaderText, UiTheme.Green.WithAlpha(0.90f),
                                      TextAlignmentOptions.Left);
            PositionStatusPanel(map);
        }

        /// <summary>
        /// The minimized map is vignetted into the cockpit rather than framed by a hard
        /// rectangle. Continue that vignette behind the roster with a soft, one-sided alpha
        /// haze. Unlike a panel sprite, the fade crosses the roster bounds: there is no hard
        /// top or right silhouette for the eye to read as a second card.
        /// </summary>
        private static void CreateStatusBackdrop(RectTransform parent, DynamicMap map)
        {
            var go = new GameObject("MapHaze", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(
                -StatusBackdropSeamOverlap, -StatusBackdropOutsideFeather);
            rect.offsetMax = new Vector2(
                StatusBackdropOutsideFeather, StatusBackdropOutsideFeather);
            rect.localScale = Vector3.one;
            rect.SetAsFirstSibling();

            Image backdrop = go.GetComponent<Image>();
            backdrop.sprite = StatusBackdropSprite(MinimapFill(map));
            backdrop.type = Image.Type.Sliced;
            backdrop.color = Color.white;
            backdrop.raycastTarget = false;
        }

        /// <summary>
        /// Resolve the minimap's real interior colour rather than maintaining a second,
        /// almost-but-not-quite matching HUD palette. Most map backgrounds are a white mask
        /// tinted by the Image; others bake the dark fill into their sprite, so combine both.
        /// </summary>
        private static Color MinimapFill(DynamicMap map)
        {
            var fallback = new Color(0.012f, 0.030f, 0.034f, 0.80f);
            Image source = map != null ? map.mapBackground : null;
            if (source == null) return fallback;

            Color fill = source.color;
            if (source.sprite != null &&
                TrySampleSpriteCentre(source.sprite, out Color spriteFill))
            {
                fill = new Color(
                    fill.r * spriteFill.r,
                    fill.g * spriteFill.g,
                    fill.b * spriteFill.b,
                    fill.a * spriteFill.a);
            }

            // An untextured white mask or a custom material does not expose its fill through
            // the sprite. In that case the measured value is not useful; use the stock-map
            // fallback observed before its shader instead of drawing a bright HUD cloud.
            float luminance = fill.r * 0.2126f + fill.g * 0.7152f + fill.b * 0.0722f;
            return fill.a > 0.05f && luminance < 0.40f ? fill : fallback;
        }

        /// <summary>Read one centre texel, with a GPU fallback for non-readable atlases.</summary>
        private static bool TrySampleSpriteCentre(Sprite sprite, out Color color)
        {
            color = Color.white;
            Texture2D source = sprite != null ? sprite.texture : null;
            if (source == null) return false;

            Rect region;
            try { region = sprite.textureRect; }
            catch { return false; }

            float u = (region.x + region.width * 0.5f) / source.width;
            float v = (region.y + region.height * 0.5f) / source.height;
            if (source.isReadable)
            {
                color = source.GetPixelBilinear(u, v);
                return true;
            }

            RenderTexture target = RenderTexture.GetTemporary(
                1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            Texture2D sample = null;
            try
            {
                // A zero scale maps the entire 1 px target to this atlas coordinate.
                Graphics.Blit(source, target, Vector2.zero, new Vector2(u, v));
                RenderTexture.active = target;
                sample = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
                sample.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, recalculateMipMaps: false);
                sample.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                color = sample.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (sample != null) Object.Destroy(sample);
            }
        }

        private static Sprite StatusBackdropSprite(Color fill)
        {
            if (statusBackdropSprite != null) return statusBackdropSprite;

            const int size = StatusBackdropTextureSize;
            const float freeEdgeFade =
                StatusBackdropOutsideFeather + StatusBackdropInnerFeather;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_MapDock",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    // The straight seam reaches full opacity where the roster begins. The
                    // other edges start fading inside the roster and finish outside it, so
                    // neither the top nor the right side can form a visible box outline.
                    float seamAlpha = Mathf.SmoothStep(
                        0f, StatusBackdropSeamOverlap, px);
                    float rightAlpha = 1f - Mathf.SmoothStep(
                        size - freeEdgeFade, size, px);
                    float verticalAlpha = Mathf.SmoothStep(
                        0f, freeEdgeFade, Mathf.Min(py, size - py));
                    float alpha = seamAlpha * rightAlpha * verticalAlpha;
                    texture.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, fill.a * alpha));
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            float freeEdgeBorder = freeEdgeFade + 1f;
            statusBackdropSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(StatusBackdropSeamOverlap, freeEdgeBorder,
                            freeEdgeBorder, freeEdgeBorder));
            statusBackdropSprite.name = "WingCommand_MapHazeSprite";
            statusBackdropSprite.hideFlags = HideFlags.HideAndDontSave;
            return statusBackdropSprite;
        }

        private static void RefreshStatusPanel(WingRegistry wing)
        {
            statusTitle.text = "WING " + wing.Count + "  ·  " +
                               FormationShapes.Pretty(WingFormation.Shape).ToUpperInvariant() +
                               "  ·  " + wing.Roe.ToString().ToUpperInvariant();

            while (statusRows.Count < wing.Count)
                statusRows.Add(new StatusRow(statusRoot, statusRows.Count));

            for (int i = 0; i < statusRows.Count; i++)
            {
                if (i < wing.Count)
                {
                    statusRows[i].Place(i, wing.Count);
                    statusRows[i].Bind(wing.Members[i], wing.Leader);
                }
                else statusRows[i].Hide();
            }

            statusRoot.sizeDelta = new Vector2(
                statusWidth,
                StatusHeaderHeight + wing.Count * StatusRowHeight + 3f);
        }

        private static void PositionStatusPanel(DynamicMap map)
        {
            if (statusRoot == null || map == null || map.mapTransform == null ||
                map.hudMapAnchor == null) return;

            RectTransform mapRect = map.mapBackground != null
                ? map.mapBackground.rectTransform
                : map.mapTransform;
            statusRoot.anchorMin = statusRoot.anchorMax = new Vector2(0.5f, 0.5f);
            statusRoot.pivot = Vector2.zero;

            // This is the roster's only placement: docked to the minimap's right edge and
            // sharing its baseline. It grows upward, so membership changes never move the
            // seam or detach the two surfaces.
            Vector3 worldBottomRight = mapRect.TransformPoint(
                new Vector3(mapRect.rect.xMax, mapRect.rect.yMin, 0f));
            Vector3 position = map.hudMapAnchor.InverseTransformPoint(worldBottomRight)
                             + Vector3.right * StatusMapGap;

            statusRoot.localPosition = position;
            statusRoot.localRotation = Quaternion.identity;
            statusRoot.localScale = Vector3.one;
        }

        private sealed class StatusRow
        {
            private readonly GameObject go;
            private readonly RectTransform rect;
            private readonly Image icon;
            private readonly TMP_Text identity;
            private readonly TMP_Text state;
            private readonly TMP_Text distance;
            private readonly Image rangeCue;
            private float cueWidth;
            private WingMember bound;

            public StatusRow(RectTransform parent, int index)
            {
                go = new GameObject("Member_" + (index + 1), typeof(RectTransform));
                rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, worldPositionStays: false);

                icon = StatusIcon(rect, new Rect(7f, -6f, 18f, 18f));
                identity = StatusLabel(rect, "", new Rect(32f, -2f, 92f, 17f), PrimaryText,
                                       WingMarkers.MemberColor, TextAlignmentOptions.Left);
                state = StatusLabel(rect, "", new Rect(32f, -17f, 118f, 13f), SecondaryText,
                                    WingMarkers.MemberColor.WithAlpha(0.62f), TextAlignmentOptions.Left);
                distance = StatusLabel(rect, "", new Rect(126f, -3f, 76f, 16f), PrimaryText,
                                       WingMarkers.MemberColor.WithAlpha(0.78f), TextAlignmentOptions.Right);
                rangeCue = StatusIcon(rect, new Rect(32f, -27f, 168f, 1f));
            }

            public void Place(int index, int count)
            {
                StatusPlace(rect, new Rect(
                    0f,
                    -StatusHeaderHeight - index * StatusRowHeight,
                    statusWidth,
                    StatusRowHeight));
                cueWidth = 168f;
            }

            public void Bind(WingMember member, Aircraft leader)
            {
                if (!go.activeSelf) go.SetActive(true);

                Aircraft aircraft = member.Aircraft;
                if (bound != member)
                {
                    bound = member;
                    icon.sprite = aircraft != null && aircraft.definition != null
                        ? aircraft.definition.friendlyIcon
                        : null;
                    identity.text = member.Slot + "  " +
                                    (aircraft != null && aircraft.definition != null
                                        ? aircraft.definition.code
                                        : "AIRCRAFT");
                }

                float range = aircraft != null && leader != null
                    ? Mathf.Sqrt(FastMath.SquareDistance(
                        aircraft.GlobalPosition(), leader.GlobalPosition()))
                    : 0f;
                state.text = StateText(member);
                distance.text = UnitConverter.DistanceReading(range);
                float proximity = 1f - Mathf.Clamp01(range / WingTuning.LeashRadius);
                rangeCue.rectTransform.sizeDelta = new Vector2(
                    Mathf.Lerp(3f, cueWidth, proximity), 2f);

                bool damaged = IsDamaged(aircraft);
                bool lowStores = member.Fuel <= WingTuning.BingoFuel || member.Ammo <= 0;
                // Colours come from the game's theme, so the strip follows a theme change the
                // way the rest of the HUD does.
                Color color = !member.Alive || damaged || member.IsPanicking
                    ? UiTheme.Alert
                    : lowStores ? UiTheme.Warning : WingMarkers.MemberColor;
                icon.color = color;
                identity.color = color;
                state.color = color.WithAlpha(0.62f);
                distance.color = color.WithAlpha(0.78f);
                rangeCue.color = color.WithAlpha(0.34f);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }

            private static bool IsDamaged(Aircraft aircraft)
            {
                if (aircraft == null || aircraft.partLookup == null) return false;
                foreach (UnitPart part in aircraft.partLookup)
                {
                    if (part != null && (part.IsDetached() || part.hitPoints < 99.5f))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// The order abbreviation, plus the weapon preference when it is not the
            /// default.
            ///
            /// Appended rather than given a column of its own: the strip is docked against
            /// the minimap and cannot grow sideways, and AUTO is both the default and the
            /// common case — so an ordinary flight reads exactly as it did before.
            /// </summary>
            private static string StateText(WingMember member)
            {
                string order = OrderCode(member);
                return member.WeaponPreference == WingWeaponPreference.Auto
                    ? order
                    : order + " · " + WingWeaponPreferences.ShortLabel(member.WeaponPreference);
            }

            private static string OrderCode(WingMember member)
            {
                // What it is actually doing outranks what it was told to do. Null means it
                // is flying the order, so the order is what to name.
                string behaviour = WingBehaviourLabels.ShortCode(member.Behaviour.BehaviourId);
                if (behaviour != null) return behaviour;

                switch (member.Order)
                {
                    case WingOrder.Engage:       return "ENG";
                    case WingOrder.ReturnToBase: return "RTB";
                    case WingOrder.FallBack:    return "FALL";
                    case WingOrder.OrbitHere:   return "CAP";
                    case WingOrder.DeliverCargo: return "CARGO";
                    case WingOrder.LandHere:    return "LAND";
                    case WingOrder.Attack:      return "ATK";
                    case WingOrder.FireForEffect: return "SPLASH";
                    case WingOrder.JamTarget:   return "JAM";
                    case WingOrder.Maneuver:    return "MNVR";
                    default:                    return "FORM";
                }
            }

        }

        private static TMP_Text StatusLabel(RectTransform parent, string text, Rect rect,
                                            float size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            StatusPlace(rt, rect);

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (statusFont != null) label.font = statusFont;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static Image StatusIcon(RectTransform parent, Rect rect)
        {
            var go = new GameObject("AircraftIcon", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            StatusPlace(rt, rect);
            Image image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static void StatusPlace(RectTransform rt, Rect rect)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.localScale = Vector3.one;
        }

        public static void DrawRadial(RadialSlice[] slices, Vector2 centre, int hovered)
        {
            EnsureStyles();

            // IMGUI has an inverted Y axis relative to Input.mousePosition.
            float cx = centre.x;
            float cy = Screen.height - centre.y;

            for (int i = 0; i < slices.Length; i++)
            {
                float angle = i * (360f / slices.Length) * Mathf.Deg2Rad;
                float x = cx + Mathf.Sin(angle) * RadialRadius - SliceWidth * 0.5f;
                float y = cy - Mathf.Cos(angle) * RadialRadius - SliceHeight * 0.5f;

                GUI.Box(new Rect(x, y, SliceWidth, SliceHeight),
                        slices[i].Label,
                        i == hovered ? sliceHotStyle : sliceStyle);
            }

            GUI.Box(new Rect(cx - 46f, cy - 14f, 92f, 28f), "WING", sliceStyle);
        }

        public static void DrawToast(string message)
        {
            EnsureStyles();
            var rect = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.78f, 380f, 30f);
            GUI.Box(rect, message, toastStyle);
        }

    }
}
