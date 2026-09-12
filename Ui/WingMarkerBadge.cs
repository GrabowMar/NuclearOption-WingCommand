using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WingCommand
{
    /// <summary>Separate membership ring and screen-aligned command brackets without recolouring the
    /// aircraft silhouette.</summary>
    internal static class WingMarkerBadge
    {
        private const string RingObjectName = "WingCommand_MembershipRing";
        private const string SelectionObjectName = "WingCommand_SelectionBadge";
        private const string StatusObjectName = "WingCommand_StatusLabel";

        public static void Apply(Image host, WingMapPresentation presentation, string status = null)
        {
            if (host == null) return;
            Transform child = host.transform.Find(RingObjectName);
            WingMapRingGraphic ring = child != null ? child.GetComponent<WingMapRingGraphic>() : null;
            bool outlined = presentation.Outline != WingMapPresentation.OutlineKind.None;
            if (!outlined && !presentation.CommandBrackets)
            {
                if (ring != null) ring.gameObject.SetActive(false);
                Transform selection = host.transform.Find(SelectionObjectName);
                if (selection != null) selection.gameObject.SetActive(false);
                Transform oldStatus = host.transform.Find(StatusObjectName);
                if (oldStatus != null) oldStatus.gameObject.SetActive(false);
                return;
            }

            // Measure rendered icon dimensions because native local scale includes zoom, icon size, and
            // heading rotation.
            Canvas canvas = host.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Rect rect = host.rectTransform.rect;
            Vector2 bottomLeft = ScreenPoint(host.rectTransform, new Vector2(rect.xMin, rect.yMin), camera);
            Vector2 bottomRight = ScreenPoint(host.rectTransform, new Vector2(rect.xMax, rect.yMin), camera);
            Vector2 topLeft = ScreenPoint(host.rectTransform, new Vector2(rect.xMin, rect.yMax), camera);
            WingMapBadgeGeometry geometry = WingMapBadgeGeometry.ForIcon(
                Vector2.Distance(bottomLeft, bottomRight), Vector2.Distance(bottomLeft, topLeft));

            if (outlined)
            {
                if (ring == null)
                {
                    var go = new GameObject(RingObjectName, typeof(RectTransform), typeof(WingMapRingGraphic));
                    ring = go.GetComponent<WingMapRingGraphic>();
                    ring.rectTransform.SetParent(host.rectTransform, worldPositionStays: false);
                }
                ring.gameObject.SetActive(true);
                ring.raycastTarget = false;
                AlignToScreen(ring.rectTransform, canvas, camera);
                ring.SetGeometry(geometry);
                ring.color = presentation.Outline == WingMapPresentation.OutlineKind.Member
                    ? WingMarkers.MemberColor
                    : presentation.Outline == WingMapPresentation.OutlineKind.Downed
                        ? WingMarkers.DownedColor : WingMarkers.TargetColor;
            }
            else if (ring != null)
            {
                ring.gameObject.SetActive(false);
            }

            ApplyCommandSelection(host, presentation.CommandBrackets, geometry, canvas, camera);
            ApplyStatus(host, status, geometry, canvas, camera);
        }

        private static void ApplyStatus(Image host, string text, WingMapBadgeGeometry geometry,
            Canvas canvas, Camera camera)
        {
            Transform child = host.transform.Find(StatusObjectName);
            TMP_Text label = child != null ? child.GetComponent<TMP_Text>() : null;
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null)
            {
                var go = new GameObject(StatusObjectName, typeof(RectTransform), typeof(TextMeshProUGUI));
                label = go.GetComponent<TMP_Text>();
                label.rectTransform.SetParent(host.rectTransform, worldPositionStays: false);
                label.fontSize = 11f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }

            label.gameObject.SetActive(true);
            AlignToScreen(label.rectTransform, canvas, camera);
            label.rectTransform.anchoredPosition = new Vector2(0f, geometry.OuterRadiusPixels + 5f);
            label.rectTransform.sizeDelta = new Vector2(120f, 18f);
            label.color = WingMarkers.DownedColor;
            label.text = text;
        }

        private static Vector2 ScreenPoint(RectTransform transform, Vector2 point, Camera camera) =>
            RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(point));

        /// <summary>Centre badge geometry on the icon in screen-pixel coordinates.</summary>
        private static void AlignToScreen(RectTransform badge, Canvas canvas, Camera camera)
        {
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = Vector2.zero;
            badge.localScale = Vector3.one;
            badge.rotation = canvas != null ? canvas.transform.rotation : Quaternion.identity;

            Vector2 origin = ScreenPoint(badge, Vector2.zero, camera);
            float scaleX = Vector2.Distance(origin, ScreenPoint(badge, Vector2.right, camera));
            float scaleY = Vector2.Distance(origin, ScreenPoint(badge, Vector2.up, camera));
            badge.localScale = new Vector3(1f / Mathf.Max(0.0001f, scaleX),
                                          1f / Mathf.Max(0.0001f, scaleY), 1f);
        }

        private static void ApplyCommandSelection(Image host, bool selected,
            WingMapBadgeGeometry geometry, Canvas canvas, Camera camera)
        {
            Transform child = host.transform.Find(SelectionObjectName);
            Image badge = child != null ? child.GetComponent<Image>() : null;
            if (!selected)
            {
                if (badge != null) badge.gameObject.SetActive(false);
                return;
            }

            if (badge == null)
            {
                var go = new GameObject(SelectionObjectName, typeof(RectTransform), typeof(Image));
                badge = go.GetComponent<Image>();
                badge.rectTransform.SetParent(host.rectTransform, worldPositionStays: false);
                badge.sprite = IconFactory.Get("selection");
                badge.preserveAspect = true;
            }

            AlignToScreen(badge.rectTransform, canvas, camera);
            badge.rectTransform.sizeDelta = Vector2.one * (geometry.CommandHalfExtentPixels * 2f);
            badge.raycastTarget = false;
            badge.gameObject.SetActive(true);

            Color color = WingMarkers.MemberColor;
            badge.color = new Color(Mathf.Clamp01(color.r + 0.65f),
                Mathf.Clamp01(color.g + 0.65f), Mathf.Clamp01(color.b + 0.65f), 1f);
        }
    }

    /// <summary>Unfilled UI ring with feathered edges, avoiding overlapping silhouette copies from Unity
    /// Outline.</summary>
    internal sealed class WingMapRingGraphic : MaskableGraphic
    {
        private WingMapBadgeGeometry geometry;

        public void SetGeometry(WingMapBadgeGeometry next)
        {
            if (Mathf.Abs(geometry.InnerRadiusPixels - next.InnerRadiusPixels) < 0.05f) return;
            geometry = next;
            rectTransform.sizeDelta = Vector2.one *
                ((geometry.OuterRadiusPixels + WingMapBadgeGeometry.FeatherPixels) * 2f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (geometry.InnerRadiusPixels <= 0f) return;
            const int segments = 64;
            Color opaque = color;
            Color transparent = new Color(color.r, color.g, color.b, 0f);
            Vector2 center = rectTransform.rect.center;

            // Build transparent-to-solid edge bands around a 1 px stroke; leave the aircraft interior
            // unfilled.
            for (int row = 0; row < 4; row++)
            {
                float radius = row == 0 ? geometry.InnerRadiusPixels - WingMapBadgeGeometry.FeatherPixels
                    : row == 1 ? geometry.InnerRadiusPixels
                    : row == 2 ? geometry.OuterRadiusPixels
                    : geometry.OuterRadiusPixels + WingMapBadgeGeometry.FeatherPixels;
                Color vertexColor = row == 0 || row == 3 ? transparent : opaque;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                        vertexColor, Vector2.zero);
                    if (row == 0 || i == 0) continue;
                    int outer = row * (segments + 1) + i;
                    int inner = outer - segments - 1;
                    vh.AddTriangle(inner - 1, inner, outer);
                    vh.AddTriangle(inner - 1, outer, outer - 1);
                }
            }
        }
    }
}
