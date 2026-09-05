using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Native MFDScreen owns show/hide and its root transform. Only the child surface
    /// is fitted, so ShowScreen and the game's startup AdaptScale cannot undo the fit.
    /// </summary>
    internal static class MfdPresentation
    {
        private const string BoscaliGuid = "com.marci.boscalisummer";

        private sealed class NativeLayout
        {
            public Transform Parent;
            public Vector2 AnchorMin, AnchorMax, Pivot, Size;
            public Vector3 Scale;
        }

        private sealed class Panel
        {
            public MFDScreen Screen, Template;
            public RectTransform Bezel;
            public bool Left;
            public RectTransform Surface;
            public Vector2 Size;
            public readonly List<RectTransform> PopupLists = new List<RectTransform>();
        }

        private static readonly Dictionary<MFDScreen, NativeLayout> native =
            new Dictionary<MFDScreen, NativeLayout>();
        private static readonly List<Panel> panels = new List<Panel>();
        private static readonly Vector3[] corners = new Vector3[4];

        public static bool Expanded => MfdPresentationRules.UseExpanded(
            Chainloader.PluginInfos.TryGetValue(BoscaliGuid, out var plugin) && plugin.Instance != null,
            Plugin.Settings != null && Plugin.Settings.FitMapToPanels.Value, GameAccess.MfdAvailable);

        // Called before the dock replaces native displayPanel references and parents.
        public static void Capture(VirtualMFD mfd)
        {
            if (mfd == null || !GameAccess.MfdAvailable) return;
            Capture(GameAccess.GetLeftScreens(mfd));
            Capture(GameAccess.GetRightScreens(mfd));
        }

        private static void Capture(List<MFDScreen> screens)
        {
            if (screens == null) return;
            foreach (MFDScreen screen in screens) Capture(screen);
        }

        private static void Capture(MFDScreen screen)
        {
            if (screen == null || screen.displayPanel == null ||
                VanillaMfdPanelCatalog.FromShortName(screen.shortName) == VanillaMfdPanelId.Unknown ||
                MfdPanelDock.IsDocked(screen) || VanillaMfdRebuild.IsHosted(screen)) return;
            var root = screen.transform as RectTransform;
            if (root == null) return;
            if (!native.TryGetValue(screen, out NativeLayout layout))
                native.Add(screen, layout = new NativeLayout());
            layout.AnchorMin = root.anchorMin;
            layout.Parent = root.parent;
            layout.AnchorMax = root.anchorMax;
            layout.Pivot = root.pivot;
            layout.Size = root.sizeDelta;
            layout.Scale = root.localScale;
        }

        public static bool HasNativeLayout(MFDScreen screen)
        {
            Capture(screen);
            return screen != null && native.ContainsKey(screen);
        }

        public static Transform NativeParent(MFDScreen template) => native[template].Parent;

        public static void Register(MFDScreen screen, MFDScreen template, RectTransform surface,
            Vector2 size, Button bezel, bool left)
        {
            var panel = new Panel
            {
                Screen = screen, Template = template, Surface = surface, Size = size,
                Bezel = bezel.transform as RectTransform, Left = left,
            };
            // AvKit's generic popup dismiss target extends 4000 units below its page.
            // These pages have a known height: keep that invisible target in the bezel.
            foreach (RectTransform child in surface.GetComponentsInChildren<RectTransform>(true))
            {
                if (child.name != "AvPopup") continue;
                var hit = child.Find("HitTarget") as RectTransform;
                if (hit != null) hit.sizeDelta = size;
                var list = child.Find("PopupList") as RectTransform;
                if (list != null) panel.PopupLists.Add(list);
            }
            panels.Add(panel);
            Apply(screen);
            // Lazy installation can happen after Maximize already built the dock.
            if (Expanded && DynamicMap.mapMaximized) MfdPanelDock.Dock(screen);
        }

        public static void Tick()
        {
            MfdRailPatch.Reconcile();
            ApplyAll();
        }

        public static void ApplyAll()
        {
            for (int i = 0; i < panels.Count; i++) Apply(panels[i]);
        }

        public static void Apply(MFDScreen screen)
        {
            for (int i = 0; i < panels.Count; i++)
                if (panels[i].Screen == screen) Apply(panels[i]);
        }

        private static void Apply(Panel panel)
        {
            if (panel.Screen == null || panel.Surface == null || panel.Template == null) return;
            var root = (RectTransform)panel.Screen.transform;
            RectTransform surface = panel.Surface;
            surface.pivot = new Vector2(0f, 1f);
            surface.sizeDelta = panel.Size;
            // A store picker opened on the last pylon row can extend below the page.
            // Keep all of its rows reachable inside the same fitted surface.
            foreach (RectTransform list in panel.PopupLists)
            {
                if (list == null || !list.gameObject.activeInHierarchy) continue;
                Vector2 popupPosition = list.anchoredPosition;
                popupPosition.x = Mathf.Clamp(popupPosition.x, 0f, Mathf.Max(0f, panel.Size.x - list.rect.width));
                popupPosition.y = Mathf.Clamp(popupPosition.y, Mathf.Min(0f, list.rect.height - panel.Size.y), 0f);
                list.anchoredPosition = popupPosition;
            }
            if (MfdPanelDock.IsDocked(panel.Screen))
            {
                root.sizeDelta = panel.Size;
                root.localScale = Vector3.one;
                surface.anchorMin = surface.anchorMax = new Vector2(0f, 1f);
                surface.anchoredPosition = Vector2.zero;
                surface.localScale = Vector3.one;
                return;
            }

            Capture(panel.Template);
            if (!native.TryGetValue(panel.Template, out NativeLayout layout)) return;
            Vector3 position = root.localPosition;
            root.anchorMin = layout.AnchorMin;
            root.anchorMax = layout.AnchorMax;
            root.pivot = layout.Pivot;
            root.sizeDelta = layout.Size;
            root.localScale = layout.Scale;
            root.localPosition = position;
            // Closed roots are deliberately off-screen. ShowScreen reapplies the fit
            // after the game puts the root back, so do not cancel its hidden offset here.
            if (!panel.Screen.isActive || panel.Bezel == null) return;
            DynamicMap dynamicMap = SceneSingleton<DynamicMap>.i;
            Canvas canvas = dynamicMap?.maximizedMapCanvas;
            var canvasRect = canvas == null ? null : canvas.transform as RectTransform;
            if (canvasRect == null) return;

            panel.Bezel.GetWorldCorners(corners);
            float bezelLeft = float.PositiveInfinity;
            float bezelRight = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                float x = canvasRect.InverseTransformPoint(corner).x;
                bezelLeft = Mathf.Min(bezelLeft, x);
                bezelRight = Mathf.Max(bezelRight, x);
            }
            Rect viewport = canvasRect.rect;
            var mapRect = dynamicMap.transform as RectTransform;
            float mapCenterY = mapRect == null ? viewport.center.y :
                canvasRect.InverseTransformPoint(mapRect.TransformPoint(mapRect.rect.center)).y;
            // The expanded dock reserves a spawn strip even in flight. Beside the
            // vanilla map that space is free while piloting: use symmetric margins
            // for a larger centered panel, retaining spawn clearance when spectating.
            float bottomReserve = SceneSingleton<CombatHUD>.i?.aircraft != null
                ? MfdLayout.TopReserve : MfdLayout.BottomReserve;
            MfdPresentationRules.Placement fit = MfdPresentationRules.FitBesideBezel(
                panel.Size.x, panel.Size.y, panel.Left,
                viewport.xMin + MfdLayout.Margin, viewport.xMax - MfdLayout.Margin,
                viewport.yMin + bottomReserve, viewport.yMax - MfdLayout.TopReserve,
                bezelLeft, bezelRight, MfdLayout.Gutter, mapCenterY);
            if (fit.Scale <= 0f) return;

            // Work in the maximized canvas, then convert to the native root. Prefab
            // offsets and MFDScreen.AdaptScale must not clip or shrink the side bay twice.
            surface.localPosition = root.InverseTransformPoint(
                canvasRect.TransformPoint(new Vector3(fit.X, fit.Top, 0f)));
            float xScale = root.InverseTransformVector(canvasRect.TransformVector(Vector3.right)).magnitude;
            float yScale = root.InverseTransformVector(canvasRect.TransformVector(Vector3.up)).magnitude;
            surface.localScale = new Vector3(fit.Scale * xScale, fit.Scale * yScale, 1f);
        }

        public static void Reset()
        {
            panels.Clear();
            native.Clear();
        }
    }
}
