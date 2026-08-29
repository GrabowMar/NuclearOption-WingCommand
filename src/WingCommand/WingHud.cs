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

        private static readonly Color Panel = new Color(0.04f, 0.06f, 0.05f, 0.78f);
        private static readonly Color Accent = new Color(0.45f, 0.95f, 0.55f);
        private static readonly Color Hot = new Color(0.10f, 0.35f, 0.16f, 0.95f);
        private static readonly Color Cold = new Color(0.05f, 0.09f, 0.07f, 0.85f);

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
            sliceStyle.normal.textColor = new Color(0.75f, 0.85f, 0.78f);

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

        private const float StatusHeaderHeight = 34f;
        private const float StatusRowHeight = 44f;
        private static float statusWidth = 600f;

        private static RectTransform statusRoot;
        private static TMP_Text statusTitle;
        private static Canvas statusCanvas;
        private static TMP_FontAsset statusFont;
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
            bool visible = Plugin.Config2.ShowHud.Value && wing.Count > 0 &&
                           !DynamicMap.mapMaximized && hud != null && hud.isActiveAndEnabled &&
                           map != null && map.gameObject.activeInHierarchy;
            Canvas canvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
            if (!visible || canvas == null)
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
            statusCanvas = null;
            statusFont = null;
            statusRows.Clear();
            nextStatusRefresh = 0f;
            lastStatusCount = -1;
        }

        private static void BuildStatusPanel(CombatHUD hud, Canvas canvas, DynamicMap map)
        {
            TMP_Text template = hud.GetComponentInChildren<TMP_Text>(includeInactive: true);
            statusFont = template != null ? template.font : null;
            statusCanvas = canvas;
            RectTransform mapBounds = map.mapBackground != null
                ? map.mapBackground.rectTransform
                : map.mapTransform;
            statusWidth = Mathf.Max(400f, mapBounds.rect.width);

            var root = new GameObject("WingCommand_Status", typeof(RectTransform));
            statusRoot = root.GetComponent<RectTransform>();
            // The minimized map already has a dedicated HUD anchor. Sharing that parent
            // avoids translating between the map canvas and the flight-HUD canvas, which
            // placed the panel off-screen on screen-space-camera HUDs.
            statusRoot.SetParent(map.hudMapAnchor, worldPositionStays: false);
            statusRoot.SetAsLastSibling();

            statusTitle = StatusLabel(statusRoot, "", new Rect(4f, -1f, statusWidth - 8f, 29f),
                                      20f, UiTheme.Green, TextAlignmentOptions.Left);
            PositionStatusPanel(map);
        }

        private static void RefreshStatusPanel(WingRegistry wing)
        {
            statusTitle.text = "WING " + wing.Count + "  |  " +
                               FormationShapes.Pretty(Plugin.Config2.Shape.Value).ToUpperInvariant() +
                               "  |  ROE " + wing.Roe.ToString().ToUpperInvariant();

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

            int lines = Mathf.CeilToInt(wing.Count / 4f);
            statusRoot.sizeDelta = new Vector2(
                statusWidth,
                StatusHeaderHeight + lines * StatusRowHeight);
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

            Vector3 worldTopLeft = mapRect.TransformPoint(
                new Vector3(mapRect.rect.xMin, mapRect.rect.yMax, 0f));
            statusRoot.localPosition = map.hudMapAnchor.InverseTransformPoint(worldTopLeft);
            statusRoot.localRotation = Quaternion.identity;
            statusRoot.localScale = Vector3.one;
        }

        private sealed class StatusRow
        {
            private readonly GameObject go;
            private readonly RectTransform rect;
            private readonly Image icon;
            private readonly TMP_Text identity;
            private readonly TMP_Text distance;
            private readonly Image rangeCue;
            private float cueWidth;

            public StatusRow(RectTransform parent, int index)
            {
                go = new GameObject("Member_" + (index + 1), typeof(RectTransform));
                rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, worldPositionStays: false);

                icon = StatusIcon(rect, new Rect(5f, -5f, 34f, 34f));
                identity = StatusLabel(rect, "", new Rect(45f, -1f, 100f, 24f), 20f,
                                       UiTheme.Friendly, TextAlignmentOptions.Left);
                distance = StatusLabel(rect, "", new Rect(45f, -21f, 100f, 18f), 14f,
                                       UiTheme.Friendly.WithAlpha(0.72f), TextAlignmentOptions.Left);
                rangeCue = StatusIcon(rect, new Rect(45f, -40f, 80f, 2f));
            }

            public void Place(int index, int count)
            {
                int columns = Mathf.Min(4, count);
                int column = index % 4;
                int line = index / 4;
                float width = statusWidth / columns;
                StatusPlace(rect, new Rect(
                    column * width,
                    -StatusHeaderHeight - line * StatusRowHeight,
                    width,
                    StatusRowHeight));
                identity.rectTransform.sizeDelta = new Vector2(width - 49f, 24f);
                distance.rectTransform.sizeDelta = new Vector2(width - 49f, 18f);
                cueWidth = Mathf.Max(12f, width - 54f);
            }

            public void Bind(WingMember member, Aircraft leader)
            {
                if (!go.activeSelf) go.SetActive(true);

                Aircraft aircraft = member.Aircraft;
                icon.sprite = aircraft != null && aircraft.definition != null
                    ? aircraft.definition.friendlyIcon
                    : null;
                identity.text = member.Slot + "  " +
                                (aircraft != null && aircraft.definition != null
                                    ? aircraft.definition.code
                                    : "AIRCRAFT");

                float range = aircraft != null && leader != null
                    ? Mathf.Sqrt(FastMath.SquareDistance(
                        aircraft.GlobalPosition(), leader.GlobalPosition()))
                    : 0f;
                distance.text = UnitConverter.DistanceReading(range);
                float proximity = 1f - Mathf.Clamp01(range / Plugin.Config2.LeashRadius.Value);
                rangeCue.rectTransform.sizeDelta = new Vector2(
                    Mathf.Lerp(3f, cueWidth, proximity), 2f);

                bool damaged = IsDamaged(aircraft);
                bool lowStores = member.Fuel <= Plugin.Config2.BingoFuel.Value || member.Ammo <= 0;
                Color color = !member.Alive || damaged
                    ? UiTheme.Alert
                    : lowStores ? UiTheme.Warning : UiTheme.Friendly;
                icon.color = color;
                identity.color = color;
                distance.color = UiTheme.Friendly.WithAlpha(0.72f);
                rangeCue.color = UiTheme.Friendly.WithAlpha(0.62f);
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
