using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>Six visual sectors; command selection and input stay in the manager.</summary>
    internal static class WingRadialOverlay
    {
        private const int SectorCount = 6;
        private static GameObject canvasRoot;
        private static TMP_Text title, subtitle;
        private static readonly Image[] fills = new Image[SectorCount];
        private static readonly Image[] icons = new Image[SectorCount];
        private static int lastHovered = -2;

        public static void Show(RadialSlice[] slices, int hoveredIndex, WingRegistry wing)
        {
            EnsureBuilt();
            canvasRoot.SetActive(true);
            bool selected = hoveredIndex >= 0 && hoveredIndex < slices.Length;
            title.text = selected ? slices[hoveredIndex].Title : "WING COMMAND";
            subtitle.text = selected ? slices[hoveredIndex].Subtitle : $"{wing?.Count ?? 0} WINGMEN ACTIVE";
            if (selected && slices[hoveredIndex].Action == WingAction.CycleRoe && wing != null)
                subtitle.text = "NEXT: " + RoeRules.Label(RoeRules.Next(wing.Roe));
            for (int i = 0; i < SectorCount; i++)
            {
                bool available = i < slices.Length;
                fills[i].gameObject.SetActive(available);
                icons[i].gameObject.SetActive(available);
                if (!available) continue;
                fills[i].color = i == hoveredIndex ? WingUi.CardFillHover : WingUi.SurfaceCard;
                icons[i].sprite = IconFactory.Get(slices[i].IconKey);
                icons[i].color = i == hoveredIndex ? Color.white : WingUi.Dim;
            }
            if (hoveredIndex != lastHovered && hoveredIndex >= 0 && lastHovered >= -1)
                WingRadioAudio.Transmission();
            lastHovered = hoveredIndex;
        }

        public static void Hide()
        {
            if (canvasRoot != null) canvasRoot.SetActive(false);
            lastHovered = -2;
        }

        public static void Reset()
        {
            if (canvasRoot != null) Object.Destroy(canvasRoot);
            canvasRoot = null;
            title = subtitle = null;
            System.Array.Clear(fills, 0, fills.Length);
            System.Array.Clear(icons, 0, icons.Length);
            lastHovered = -2;
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
            scaler.matchWidthOrHeight = 0.5f;
            var group = canvasRoot.GetComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            var parent = (RectTransform)canvasRoot.transform;
            for (int i = 0; i < SectorCount; i++)
            {
                Image fill = MakeImage(parent, "Sector", 400f, Vector2.zero, "disc");
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.fillAmount = 1f / SectorCount;
                fill.rectTransform.localRotation = Quaternion.Euler(0, 0, 30f - i * 60f);
                fills[i] = fill;
            }
            for (int i = 0; i < SectorCount; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                icons[i] = MakeImage(parent, "Icon", 36f,
                    new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 142f, "root");
            }
            MakeImage(parent, "Hub", 164f, Vector2.zero, "disc").color = AvTheme.Ground;
            title = MakeLabel(parent, 9f, 15f, Color.white);
            title.fontStyle = FontStyles.Bold;
            subtitle = MakeLabel(parent, -16f, 10.5f, WingUi.Dim);
        }

        private static Image MakeImage(RectTransform parent, string name, float size, Vector2 position, string icon)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.sprite = IconFactory.Get(icon);
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text MakeLabel(RectTransform parent, float y, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(152f, 28f);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (WingUi.Font != null) text.font = WingUi.Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
    }
}
