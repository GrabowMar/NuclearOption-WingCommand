using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Compact command ring; selection and pointer motion belong to the manager.</summary>
    internal static class WingRadialOverlay
    {
        private const int SectorCount = 6;
        private static readonly Color SurfaceNormal = new Color(0.045f, 0.065f, 0.095f, 0.88f);
        private static readonly Color SurfaceSelected = new Color(0.76f, 0.50f, 0.14f, 0.94f);
        private static readonly Color SurfaceDisabled = new Color(0.035f, 0.045f, 0.065f, 0.55f);
        private static readonly Color SurfaceDisabledHovered = new Color(0.40f, 0.14f, 0.14f, 0.88f);

        private static readonly Color RimSelected = new Color(1.0f, 0.74f, 0.26f, 1.0f);
        private static readonly Color RimDisabledHovered = new Color(0.95f, 0.35f, 0.35f, 0.98f);

        private static readonly Color TextSelected = Color.white;
        private static readonly Color TextNormal = new Color(0.82f, 0.86f, 0.92f, 1f);
        private static readonly Color TextDisabled = new Color(0.42f, 0.46f, 0.52f, 0.65f);

        private static readonly Color AccentRed = new Color(0.95f, 0.35f, 0.35f, 1f);
        private static readonly Color DeadzoneLine = new Color(1f, 1f, 1f, 0.12f);

        private static GameObject canvasRoot;
        private static CanvasGroup canvasGroup;
        private static TMP_Text title, subtitle, hint, help;
        private static RectTransform pointerRing, pointerCore;
        private static WingRadialArc pointerRingArc, pointerCoreArc, needle;
        private static readonly WingRadialArc[] fills = new WingRadialArc[SectorCount];
        private static readonly WingRadialArc[] rims = new WingRadialArc[SectorCount];
        private static readonly Image[] icons = new Image[SectorCount];
        private static readonly TMP_Text[] labels = new TMP_Text[SectorCount];

        private static float currentAlpha = 0f;
        private static float currentScale = 0.95f;

        public static void Show(RadialSlice[] slices, int hoveredIndex, WingRegistry wing, Vector2 delta)
        {
            EnsureBuilt();
            if (!canvasRoot.activeSelf)
            {
                canvasRoot.SetActive(true);
                currentAlpha = 0.2f;
                currentScale = 0.94f;
            }

            if (canvasGroup != null)
            {
                currentAlpha = Mathf.MoveTowards(currentAlpha, 1f, Time.unscaledDeltaTime * 16f);
                canvasGroup.alpha = currentAlpha;
            }
            currentScale = Mathf.MoveTowards(currentScale, 1f, Time.unscaledDeltaTime * 12f);
            canvasRoot.transform.localScale = Vector3.one * currentScale;

            bool inDeadzone = RadialSelection.IsInDeadzone(delta.x, delta.y);
            bool selected = !inDeadzone && hoveredIndex >= 0 && hoveredIndex < slices.Length;
            bool isAvailable = selected && slices[hoveredIndex].Available;
            bool requiresTarget = selected && slices[hoveredIndex].RequiresTarget;

            // Center text
            if (selected)
            {
                title.text = slices[hoveredIndex].Title;
                subtitle.text = slices[hoveredIndex].Subtitle;
            }
            else
            {
                title.text = "WING COMMAND";
                subtitle.text = wing != null
                    ? RadialSelection.FormatSquadronSubtitle(wing.Count, CombatFacade.Roe.Label(wing.Roe), FormationShapes.Pretty(WingFormation.Shape))
                    : "WHOLE WING";
            }

            hint.text = RadialSelection.FormatHint(inDeadzone, isAvailable, requiresTarget);

            // Reticle and needle updates
            pointerRing.anchoredPosition = delta;
            pointerCore.anchoredPosition = delta;

            Color reticleColor = inDeadzone
                ? new Color(1f, 1f, 1f, 0.45f)
                : (selected && !isAvailable ? AccentRed : (selected ? RimSelected : Color.white));
            pointerRingArc.color = reticleColor;
            pointerCoreArc.color = reticleColor;

            if (needle != null)
            {
                if (inDeadzone)
                {
                    needle.gameObject.SetActive(false);
                }
                else
                {
                    needle.gameObject.SetActive(true);
                    float mag = delta.magnitude;
                    needle.Inner = RadialSelection.Deadzone;
                    needle.Outer = Mathf.Min(mag, RadialSelection.PointerRadius);
                    float angle = (Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + 360f) % 360f;
                    needle.StartAngle = angle - 0.75f;
                    needle.Sweep = 1.5f;
                    Color nColor = selected && !isAvailable ? AccentRed : (selected ? RimSelected : new Color(1f, 1f, 1f, 0.35f));
                    nColor.a = 0.45f;
                    needle.color = nColor;
                    needle.SetVerticesDirty();
                }
            }

            // Sectors
            for (int i = 0; i < SectorCount; i++)
            {
                bool present = i < slices.Length;
                fills[i].gameObject.SetActive(present);
                icons[i].gameObject.SetActive(present);
                labels[i].gameObject.SetActive(present);
                if (!present)
                {
                    rims[i].gameObject.SetActive(false);
                    continue;
                }

                bool hovered = i == hoveredIndex && !inDeadzone;
                bool itemAvailable = slices[i].Available;

                rims[i].gameObject.SetActive(hovered);

                if (hovered)
                {
                    fills[i].color = itemAvailable ? SurfaceSelected : SurfaceDisabledHovered;
                    rims[i].color = itemAvailable ? RimSelected : RimDisabledHovered;
                    icons[i].color = itemAvailable ? TextSelected : AccentRed;
                    labels[i].color = itemAvailable ? TextSelected : AccentRed;
                }
                else if (itemAvailable)
                {
                    fills[i].color = SurfaceNormal;
                    icons[i].color = TextNormal;
                    labels[i].color = TextNormal;
                }
                else
                {
                    fills[i].color = SurfaceDisabled;
                    icons[i].color = TextDisabled;
                    labels[i].color = TextDisabled;
                }

                icons[i].sprite = IconFactory.Get(slices[i].IconKey);
                labels[i].text = slices[i].Title;
            }
        }

        public static void Hide()
        {
            if (canvasRoot != null && canvasRoot.activeSelf)
            {
                canvasRoot.SetActive(false);
                currentAlpha = 0f;
                currentScale = 0.94f;
            }
        }

        public static void Reset()
        {
            if (canvasRoot != null) Object.Destroy(canvasRoot);
            canvasRoot = null;
            canvasGroup = null;
            title = subtitle = hint = help = null;
            pointerRing = pointerCore = null;
            pointerRingArc = pointerCoreArc = needle = null;
            currentAlpha = 0f;
            currentScale = 0.95f;
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
            canvasGroup = canvasRoot.GetComponent<CanvasGroup>();
            canvasGroup.interactable = canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;
            var parent = (RectTransform)canvasRoot.transform;

            // Center plate and tactical bezels
            Arc(parent, "Centre", 0f, 91f, 0f, 360f, new Color(0.025f, 0.035f, 0.055f, 0.78f));
            Arc(parent, "InnerRule", 94f, 95.5f, 0f, 360f, new Color(1f, 1f, 1f, 0.28f));
            Arc(parent, "OuterRule", 191.5f, 193f, 0f, 360f, new Color(1f, 1f, 1f, 0.28f));
            Arc(parent, "DeadzoneRing", 63.5f, 64.5f, 0f, 360f, DeadzoneLine);

            // Boundary tick notches between sectors
            for (int i = 0; i < SectorCount; i++)
            {
                float tickAngle = i * 60f + 30f;
                Arc(parent, "Tick", 193f, 198f, tickAngle - 0.75f, 1.5f, new Color(1f, 1f, 1f, 0.35f));
            }

            // Sectors
            for (int i = 0; i < SectorCount; i++)
            {
                float angle = i * 60f;
                fills[i] = Arc(parent, "Order", 98f, 189f, angle - 29.4f, 58.8f, SurfaceNormal);
                rims[i] = Arc(parent, "SelectedEdge", 189.5f, 193.5f, angle - 29.4f, 58.8f, RimSelected);
                Vector2 position = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * 143f;
                var rt = Rect(parent, "Icon", position + Vector2.up * 13f, new Vector2(26f, 26f));
                icons[i] = rt.gameObject.AddComponent<Image>();
                icons[i].raycastTarget = false;
                labels[i] = Label(parent, position - Vector2.up * 19f, new Vector2(108f, 36f), 13.5f, TextNormal);
            }

            // Pointer needle (radial line from deadzone outward)
            needle = Arc(parent, "PointerNeedle", 64f, 164f, 0f, 1.5f, new Color(1f, 1f, 1f, 0f));
            needle.gameObject.SetActive(false);

            // Pointer reticle: ring + core bead
            pointerRingArc = Arc(parent, "PointerRing", 4.5f, 6.5f, 0f, 360f, Color.white);
            pointerRing = pointerRingArc.rectTransform;
            pointerCoreArc = Arc(parent, "PointerCore", 0f, 2.5f, 0f, 360f, Color.white);
            pointerCore = pointerCoreArc.rectTransform;

            // Center information hierarchy
            title = Label(parent, new Vector2(0f, 28f), new Vector2(146f, 36f), 15f, Color.white);
            title.fontStyle = FontStyles.Bold;
            subtitle = Label(parent, new Vector2(0f, -8f), new Vector2(144f, 44f), 12f, TextNormal);
            hint = Label(parent, new Vector2(0f, -50f), new Vector2(144f, 20f), 9.5f, TextNormal);
            help = Label(parent, new Vector2(0f, -220f), new Vector2(450f, 24f), 11.5f, new Color(1f, 1f, 1f, 0.75f));
            help.text = "RELEASE TO CONFIRM    /    RIGHT-CLICK TO CANCEL";
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
