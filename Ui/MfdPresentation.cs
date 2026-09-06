using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Fits only WMC in vanilla. Boscali owns layout when present.</summary>
    internal static class MfdPresentation
    {
        private static MFDScreen screen;
        private static RectTransform surface, bezel;
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
            // Never write another screen's transforms, and never compete with Boscali.
            if (BoscaliLoaded || screen == null || !screen.isActive ||
                surface == null || bezel == null || !DynamicMap.mapMaximized) return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            var canvas = map?.maximizedMapCanvas?.transform as RectTransform;
            if (canvas == null) return;

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
            // Reserve the native clock/feed and, while spectating, the spawn controls.
            float bottom = SceneSingleton<CombatHUD>.i?.aircraft != null ? 26f : 120f;
            var fit = MfdPresentationRules.FitBesideBezel(size.x, size.y, left,
                viewport.xMin + 8f, viewport.xMax - 8f,
                viewport.yMin + bottom, viewport.yMax - 26f, bezelLeft, bezelRight, 8f, center);
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

        public static void Reset()
        {
            screen = null;
            surface = bezel = null;
        }
    }
}
