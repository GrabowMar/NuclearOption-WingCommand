using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>Native compact wing HUD plus fallback debug IMGUI notices; radial rendering lives in its
    /// own overlay/integration classes.</summary>
    internal static class WingHud
    {
        private static bool stylesReady;
        private static GUIStyle toastStyle;

        // Read shared theme tokens so mission theme changes reach the HUD.
        private static Color Panel => AvTheme.Unity(AvTokens.HudPanel);
        private static Color Accent => AvTheme.Friendly;

        private static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            Color toastBg = Panel;
            Color toastBorder = Accent.WithAlpha(0.35f);
            Color toastText = Color.white;

            var bg = Solid(toastBg);

            toastStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = toastText, background = bg },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(12, 12, 6, 6),
            };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private const float StatusHeaderHeight = 42f;
        private const float StatusRowHeight = 36f;
        private const float StatusPanelWidth = 230f;
        private const float StatusMapGap = 0f;
        private const float StatusBackdropOutsideFeather = 18f;
        private const float StatusBackdropInnerFeather = 12f;
        private const float StatusBackdropSeamOverlap = 18f;
        private const int StatusBackdropTextureSize = 80;

        // Use the game type scale for identity and supporting status text.

        /// <summary>Wing-strip heading size.</summary>
        private const float HeaderText = AvTokens.FontSmall;

        /// <summary>Primary callsign and range size.</summary>
        private const float PrimaryText = AvTokens.FontLead;

        /// <summary>Supporting order and status size.</summary>
        private const float SecondaryText = AvTokens.FontSmall;

        private static float statusWidth = StatusPanelWidth;

        private static RectTransform statusRoot;
        private static TMP_Text statusTitle;
        private static TMP_Text statusSummary;
        private static CombatHUD statusHud;
        private static Canvas statusCanvas;
        private static Sprite statusBackdropSprite;
        private static float nextStatusRefresh;
        private static int lastStatusCount = -1;
        private static readonly List<StatusRow> statusRows = new List<StatusRow>();

        /// <summary>Build inside the native HUD canvas to inherit font, scale, theme, and resolution
        /// behaviour.</summary>
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

            // Resolve scene canvas hierarchy only at creation or when the HUD instance changes.
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
            statusSummary = null;
            statusHud = null;
            statusCanvas = null;
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
            if (template != null) WingUi.Font = template.font;
            statusHud = hud;
            statusCanvas = canvas;
            statusWidth = StatusPanelWidth;

            var root = new GameObject("WingCommand_Status", typeof(RectTransform));
            statusRoot = root.GetComponent<RectTransform>();
            // Share the minimap HUD anchor to avoid cross-canvas coordinate errors on camera-space
            // HUDs.
            statusRoot.SetParent(map.hudMapAnchor, worldPositionStays: false);
            statusRoot.SetAsLastSibling();

            CreateStatusBackdrop(statusRoot, map);

            statusTitle = WingUi.Label(statusRoot, "", new Rect(7f, -2f, statusWidth - 14f, 20f),
                                       AvTheme.Accent, HeaderText, FontStyles.Bold,
                                       TextAlignmentOptions.Left);
            statusSummary = WingUi.Label(statusRoot, "", new Rect(7f, -21f, statusWidth - 14f, 16f),
                                         WingUi.TextPrimary, SecondaryText, FontStyles.Normal,
                                         TextAlignmentOptions.Left);
            PositionStatusPanel(map);
        }

        /// <summary>Extend the minimap vignette behind the roster with a one-sided fade beyond its bounds,
        /// avoiding a second hard-edged panel.</summary>
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

        /// <summary>Sample the minimap fill by combining sprite colour and Image tint instead of
        /// maintaining a separate palette.</summary>
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

            // Use the measured native fallback when white masks or custom materials make sprite
            // sampling unrepresentative.
            float luminance = fill.r * 0.2126f + fill.g * 0.7152f + fill.b * 0.0722f;
            return fill.a > 0.05f && luminance < 0.40f ? fill : fallback;
        }

        /// <summary>Sample a centre texel, using GPU readback for non-readable atlases.</summary>
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
                // Zero UV scale samples the same atlas coordinate into the 1 px target.
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

                    // Keep the join opaque while fading other edges beyond the roster to avoid a
                    // visible box.
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
            statusTitle.text = "WING  /  " + wing.Count;
            statusTitle.color = AvTheme.Accent;
            statusSummary.text = FormationShapes.Pretty(WingFormation.Shape).ToUpperInvariant() +
                                 "  ·  " + wing.Roe.ToString().ToUpperInvariant();
            statusSummary.color = WingUi.TextPrimary;

            while (statusRows.Count < wing.Count)
                statusRows.Add(new StatusRow(statusRoot, statusRows.Count));

            for (int i = 0; i < statusRows.Count; i++)
            {
                if (i < wing.Count)
                {
                    statusRows[i].Place(i);
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

            // Dock beside the minimap by default; read offsets each tick to apply live F1 changes.
            Vector3 worldBottomRight = mapRect.TransformPoint(
                new Vector3(mapRect.rect.xMax, mapRect.rect.yMin, 0f));
            Vector3 position = map.hudMapAnchor.InverseTransformPoint(worldBottomRight)
                             + Vector3.right * StatusMapGap;
            if (Plugin.Settings != null)
                position += new Vector3(Plugin.Settings.WingHudX.Value, Plugin.Settings.WingHudY.Value, 0f);

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
                identity = WingUi.Label(rect, "", new Rect(32f, -2f, 104f, 17f),
                                        WingMarkers.MemberColor, PrimaryText, FontStyles.Normal,
                                        TextAlignmentOptions.Left);
                state = WingUi.Label(rect, "", new Rect(32f, -19f, 190f, 16f),
                                     WingUi.TextPrimary, SecondaryText,
                                     FontStyles.Normal, TextAlignmentOptions.Left);
                distance = WingUi.Label(rect, "", new Rect(142f, -3f, 80f, 16f),
                                        WingUi.TextPrimary, PrimaryText,
                                        FontStyles.Normal, TextAlignmentOptions.Right);
                rangeCue = StatusIcon(rect, new Rect(32f, -34f, 190f, 1f));
            }

            public void Place(int index)
            {
                WingUi.Place(rect, new Rect(
                    0f,
                    -StatusHeaderHeight - index * StatusRowHeight,
                    statusWidth,
                    StatusRowHeight));
                cueWidth = 190f;
            }

            public void Bind(WingMember member, Aircraft leader)
            {
                if (!go.activeSelf) go.SetActive(true);

                Aircraft aircraft = member.Aircraft;
                icon.sprite = IconFactory.Aircraft(aircraft != null ? aircraft.definition : null);
                if (bound != member)
                {
                    bound = member;
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
                float leash = Plugin.Settings != null ? Plugin.Settings.LeashDistance.Value : WingTuning.LeashRadius;
                float proximity = 1f - Mathf.Clamp01(range / leash);
                rangeCue.rectTransform.sizeDelta = new Vector2(
                    Mathf.Lerp(3f, cueWidth, proximity), 2f);

                bool damaged = IsDamaged(aircraft);
                float bingo = Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel;
                bool lowStores = member.Fuel <= bingo || member.Ammo <= 0;
                // Use theme-derived status colours for consistent HUD changes.
                Color color = !member.Alive || damaged || member.IsPanicking
                    ? AvTheme.Alert
                    : lowStores ? AvTheme.Warning : WingMarkers.MemberColor;
                icon.color = color;
                identity.color = color;
                state.color = lowStores || damaged || !member.Alive || member.IsPanicking
                    ? color : WingUi.TextPrimary;
                distance.color = WingUi.TextPrimary;
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

            /// <summary>Order abbreviation with non-default weapon preference; omit AUTO to keep status concise.</summary>
            private static string StateText(WingMember member)
            {
                if (member == null) return string.Empty;

                string delivery = WingDeliveryTracker.GetDeliveryTag(member.Aircraft);
                if (delivery != null) return delivery;

                string order = OrderCode(member);

                if (IsDamaged(member.Aircraft))
                {
                    return order + " · DMG";
                }

                if (member.Fuel <= (Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel))
                {
                    return order + " · BINGO";
                }

                if (member.Ammo > 0 && CombatFacade.Weapons.GetGuidedAmmo(member.Aircraft) == 0)
                {
                    return order + " · WINC";
                }

                return member.WeaponPreference == WingWeaponPreference.Auto
                    ? order
                    : order + " · " + WingWeaponPreferences.ShortLabel(member.WeaponPreference);
            }

            private static string OrderCode(WingMember member)
            {
                // Display active override behaviour first; null falls back to the standing order.
                string behaviour = WingBehaviourLabels.ShortCode(member.Behaviour.BehaviourId);
                if (behaviour != null) return behaviour;

                // Use host short-code overrides directly; HUD space is narrower than catalogue labels.
                string host = WingHost.Current.ShortLabelFor(member.Order);
                if (host != null) return host;

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
                    case WingOrder.SeekAndDestroy: return "S&D";
                    case WingOrder.JamTarget:   return "JAM";
                    case WingOrder.Maneuver:    return "MNVR";
                    case WingOrder.StandDown:   return "WAIT";
                    default:                    return "FORM";
                }
            }

        }

        private static Image StatusIcon(RectTransform parent, Rect rect)
        {
            var go = new GameObject("AircraftIcon", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            WingUi.Place(rt, rect);
            Image image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public static void DrawToast(string message)
        {
            EnsureStyles();
            var rect = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.78f, 380f, 30f);
            GUI.Box(rect, message, toastStyle);
        }

    }
}
