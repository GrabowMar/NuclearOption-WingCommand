using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Fit WMC in vanilla; defer layout to Boscali when present.</summary>
    internal static class MfdPresentation
    {
        private static MFDScreen screen;
        private static RectTransform surface, bezel;
        private static Canvas lift;
        private static Vector2 size;
        private static bool left;
        private static readonly Vector3[] corners = new Vector3[4];

        internal static bool BoscaliLoaded =>
            Chainloader.PluginInfos.TryGetValue("com.marci.boscalisummer", out var plugin) &&
            plugin.Instance != null;

        public static bool HasNativeLayout(MFDScreen template) =>
            template != null && template.displayPanel != null && template.transform is RectTransform;

        public static void Register(MFDScreen owner, RectTransform content, Vector2 contentSize,
                                    Button button, bool onLeft)
        {
            screen = owner;
            surface = content;
            size = contentSize;
            bezel = button.transform as RectTransform;
            left = onLeft;
        }

        public static void Tick()
        {
            // Boscali owns layout and canvas ordering when available.
            if (screen == null || surface == null) return;

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            bool overMaximizedMap = !BoscaliLoaded && screen.isActive && bezel != null &&
                                    DynamicMap.mapMaximized;
            if (!overMaximizedMap)
            {
                // Restore bezel canvas ordering on the cockpit MFD.
                if (lift != null) lift.overrideSorting = false;
                return;
            }

            var canvas = map?.maximizedMapCanvas?.transform as RectTransform;
            if (canvas == null) return;

            LiftAboveKillFeed(map);

            bezel.GetWorldCorners(corners);
            float bezelLeft = float.PositiveInfinity, bezelRight = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                float x = canvas.InverseTransformPoint(corner).x;
                bezelLeft = Mathf.Min(bezelLeft, x);
                bezelRight = Mathf.Max(bezelRight, x);
            }
            Rect viewport = canvas.rect;
            var mapRect = map.transform as RectTransform;
            float center = mapRect == null ? viewport.center.y :
                canvas.InverseTransformPoint(mapRect.TransformPoint(mapRect.rect.center)).y;
            // Use the same inset at the viewport and bezel. The tall panel can grow into
            // the previously reserved empty strip below the map.
            const float margin = 8f;
            var fit = MfdPresentationRules.FitBesideBezel(size.x, size.y, left,
                viewport.xMin + margin, viewport.xMax - margin,
                viewport.yMin + margin, viewport.yMax - margin, bezelLeft, bezelRight, margin, center);
            if (fit.Scale <= 0f) return;
            var root = (RectTransform)screen.transform;
            surface.anchorMin = surface.anchorMax = surface.pivot = new Vector2(0f, 1f);
            surface.sizeDelta = size;
            surface.localPosition = root.InverseTransformPoint(
                canvas.TransformPoint(new Vector3(fit.X, fit.Top, 0f)));
            surface.localScale = new Vector3(
                fit.Scale * root.InverseTransformVector(canvas.TransformVector(Vector3.right)).magnitude,
                fit.Scale * root.InverseTransformVector(canvas.TransformVector(Vector3.up)).magnitude, 1f);
        }

        /// <summary>Lift WMC above map and kill-feed canvases explicitly. Add a GraphicRaycaster to the
        /// nested canvas because controls register with their nearest canvas.</summary>
        private static void LiftAboveKillFeed(DynamicMap map)
        {
            if (lift == null)
            {
                lift = surface.gameObject.GetComponent<Canvas>()
                       ?? surface.gameObject.AddComponent<Canvas>();
                Canvas host = map != null ? map.maximizedMapCanvas : null;
                if (host != null) lift.additionalShaderChannels = host.additionalShaderChannels;
                if (surface.GetComponent<GraphicRaycaster>() == null)
                    surface.gameObject.AddComponent<GraphicRaycaster>();
            }

            var feed = SceneSingleton<MessageUI>.i;
            Canvas feedCanvas = feed != null ? feed.GetComponentInParent<Canvas>() : null;
            int feedOrder = feedCanvas != null ? feedCanvas.sortingOrder : 0;

            // Also exceed the map canvas order so its nested panel remains visible.
            Canvas mapCanvas = map != null ? map.maximizedMapCanvas : null;
            int mapOrder = mapCanvas != null ? mapCanvas.sortingOrder : 0;

            lift.overrideSorting = true;
            lift.sortingOrder = Mathf.Max(feedOrder, mapOrder) + 1;
        }

        public static void Reset()
        {
            if (lift != null) lift.overrideSorting = false;
            lift = null;
            screen = null;
            surface = bezel = null;
        }
    }
}
