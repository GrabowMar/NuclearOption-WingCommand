using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Compact command ring; selection and pointer motion belong to the manager.</summary>
    internal static class WingRadialOverlay
    {
        private const int SectorCount = 6;
        private static readonly Color Surface = new Color(0.055f, 0.065f, 0.085f, 0.86f);
        private static readonly Color Selected = new Color(0.72f, 0.49f, 0.16f, 0.96f);
        private static readonly Color Muted = new Color(0.78f, 0.80f, 0.84f, 1f);
        private static GameObject canvasRoot;
        private static TMP_Text title, subtitle, hint;
        private static RectTransform pointer;
        private static readonly WingRadialArc[] fills = new WingRadialArc[SectorCount];
        private static readonly WingRadialArc[] rims = new WingRadialArc[SectorCount];
        private static readonly Image[] icons = new Image[SectorCount];
        private static readonly TMP_Text[] labels = new TMP_Text[SectorCount];

        public static void Show(RadialSlice[] slices, int hoveredIndex, WingRegistry wing, Vector2 delta)
        {
            EnsureBuilt();
            canvasRoot.SetActive(true);
            bool selected = hoveredIndex >= 0 && hoveredIndex < slices.Length;
            title.text = selected ? slices[hoveredIndex].Title : "WING COMMAND";
            subtitle.text = selected ? slices[hoveredIndex].Subtitle : $"{wing?.Count ?? 0} WINGMEN\nWHOLE WING";
            if (selected && slices[hoveredIndex].Action == WingAction.CycleRoe && wing != null)
                subtitle.text = RoeRules.Label(wing.Roe) + " > " + RoeRules.Label(RoeRules.Next(wing.Roe));
            hint.text = selected ? "RELEASE TO ORDER" : "MOVE TO SELECT";
            pointer.anchoredPosition = delta;
            for (int i = 0; i < SectorCount; i++)
            {
                bool available = i < slices.Length;
                fills[i].gameObject.SetActive(available);
                icons[i].gameObject.SetActive(available);
                labels[i].gameObject.SetActive(available);
                rims[i].gameObject.SetActive(available && i == hoveredIndex);
                if (!available) continue;
                fills[i].color = i == hoveredIndex ? Selected : Surface;
                icons[i].sprite = IconFactory.Get(slices[i].IconKey);
                icons[i].color = i == hoveredIndex ? Color.white : Muted;
                labels[i].text = slices[i].Title;
                labels[i].color = i == hoveredIndex ? Color.white : Muted;
            }
        }

        public static void Hide()
        {
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }

        public static void Reset()
        {
            if (canvasRoot != null) Object.Destroy(canvasRoot);
            canvasRoot = null;
            title = subtitle = hint = null;
            pointer = null;
            System.Array.Clear(fills, 0, fills.Length);
            System.Array.Clear(rims, 0, rims.Length);
            System.Array.Clear(icons, 0, icons.Length);
            System.Array.Clear(labels, 0, labels.Length);
        }

        private static void EnsureBuilt()
        {
            if (canvasRoot != null) return;
            canvasRoot = new GameObject("WingCommandWheel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2800;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            var group = canvasRoot.GetComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            var parent = (RectTransform)canvasRoot.transform;
            Arc(parent, "Centre", 0f, 91f, 0f, 360f, new Color(0.03f, 0.04f, 0.06f, 0.58f));
            Arc(parent, "InnerRule", 94f, 95f, 0f, 360f, new Color(1f, 1f, 1f, 0.28f));
            Arc(parent, "OuterRule", 192f, 193f, 0f, 360f, new Color(1f, 1f, 1f, 0.28f));
            for (int i = 0; i < SectorCount; i++)
            {
                float angle = i * 60f;
                fills[i] = Arc(parent, "Order", 98f, 189f, angle - 29.4f, 58.8f, Surface);
                rims[i] = Arc(parent, "SelectedEdge", 190f, 194f, angle - 29.4f, 58.8f, Selected);
                Vector2 position = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * 143f;
                var rt = Rect(parent, "Icon", position + Vector2.up * 13f, new Vector2(26f, 26f));
                icons[i] = rt.gameObject.AddComponent<Image>();
                icons[i].raycastTarget = false;
                labels[i] = Label(parent, position - Vector2.up * 19f, new Vector2(108f, 36f), 14f, Muted);
            }
            title = Label(parent, new Vector2(0f, 28f), new Vector2(146f, 40f), 15f, Color.white);
            title.fontStyle = FontStyles.Bold;
            subtitle = Label(parent, new Vector2(0f, -9f), new Vector2(140f, 48f), 12f, Muted);
            hint = Label(parent, new Vector2(0f, -52f), new Vector2(142f, 20f), 9f, Muted);
            var help = Label(parent, new Vector2(0f, -220f), new Vector2(430f, 24f), 12f, Color.white);
            help.text = "RELEASE TO CONFIRM    /    RIGHT-CLICK TO CANCEL";
            pointer = Arc(parent, "Pointer", 0f, 3.5f, 0f, 360f, Color.white).rectTransform;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static WingRadialArc Arc(RectTransform parent, string name, float inner, float outer, float start, float sweep, Color color)
        {
            var arc = Rect(parent, name, Vector2.zero, Vector2.one * outer * 2f).gameObject.AddComponent<WingRadialArc>();
            arc.Inner = inner;
            arc.Outer = outer;
            arc.StartAngle = start;
            arc.Sweep = sweep;
            arc.color = color;
            arc.raycastTarget = false;
            return arc;
        }

        private static TMP_Text Label(RectTransform parent, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var text = Rect(parent, "Label", position, size).gameObject.AddComponent<TextMeshProUGUI>();
            // The wheel uses a readable UI face, not the condensed cockpit instrument font.
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
    }

    /// <summary>Texture-free annular sectors with a narrow transparent fringe for smooth edges.</summary>
    internal sealed class WingRadialArc : MaskableGraphic
    {
        internal float Inner, Outer, StartAngle, Sweep;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            int steps = Mathf.Max(2, Mathf.CeilToInt(Sweep / 0.5f));
            float feather = Mathf.Min(0.75f, (Outer - Inner) * 0.25f);
            float[] radii = { Mathf.Max(0f, Inner - feather), Inner + feather, Outer - feather, Outer + feather };
            for (int band = 0; band < 4; band++)
            {
                for (int step = 0; step <= steps; step++)
                {
                    float angle = (StartAngle + Sweep * step / steps) * Mathf.Deg2Rad;
                    Color tint = color;
                    if (band == 3 || (band == 0 && Inner > 0f) || (Sweep < 360f && (step == 0 || step == steps))) tint.a = 0f;
                    mesh.AddVert(new Vector3(Mathf.Sin(angle) * radii[band], Mathf.Cos(angle) * radii[band]), tint, Vector2.zero);
                    if (band == 0 || step == 0) continue;
                    int v = band * (steps + 1) + step;
                    mesh.AddTriangle(v, v - 1, v - steps - 2);
                    mesh.AddTriangle(v, v - steps - 2, v - steps - 1);
                }
            }
        }
    }
}
