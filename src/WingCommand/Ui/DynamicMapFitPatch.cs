using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Repositions the maximised tactical map's bezel buttons so they sit in rows above and
    /// below the map rather than flanking it on the left and right.
    ///
    /// The stock maximised map keeps the map at (or near) full screen and hangs a column of
    /// MFD bezel buttons (BDF / MAP / HUD / QRS / HMG, plus the mods' WMC / OPS / RAD) down
    /// each side of it. Turning the map controls to "side panels" in that layout pushes those
    /// panels over live terrain. Instead of shrinking the map, this lays the left bezel
    /// column out as a horizontal row above the map and the right column as a row below it,
    /// leaving the map at its own (original) dimensions.
    /// </summary>
    [HarmonyPatch]
    internal static class DynamicMapFitPatch
    {
        private sealed class ButtonSnapshot
        {
            public Button Button;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        /// <summary>Horizontal gap between buttons in a row, in canvas units.</summary>
        private const float RowGap = 8f;

        /// <summary>Distance from the screen edge to the near edge of a button row.</summary>
        private const float RowMargin = 12f;

        /// <summary>Nominal per-button width, used only when a button's rect reports zero.</summary>
        private const float ButtonWidthFallback = 48f;

        private static readonly List<ButtonSnapshot> snapshots = new List<ButtonSnapshot>();
        private static bool moved;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
        public static void MaximizePostfix(DynamicMap __instance)
        {
            if (__instance == null) return;

            if (!Plugin.Settings.FitMapToPanels.Value || !GameAccess.MfdAvailable)
            {
                Restore(__instance);
                return;
            }

            if (!moved)
            {
                CaptureAndMove(__instance);
            }
            else
            {
                // Re-lay out against the current canvas size (e.g. after a resolution change).
                Layout(__instance);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
        public static void MinimizePostfix(DynamicMap __instance)
        {
            if (__instance == null) return;
            Restore(__instance);
        }

        private static void CaptureAndMove(DynamicMap map)
        {
            VirtualMFD mfd = Object.FindObjectOfType<VirtualMFD>();
            if (mfd == null) return;

            List<Button> left = GameAccess.GetLeftButtons(mfd);
            List<Button> right = GameAccess.GetRightButtons(mfd);

            snapshots.Clear();
            if (left != null)
            {
                for (int i = 0; i < left.Count; i++)
                    if (left[i] != null) Snapshot(left[i]);
            }
            if (right != null)
            {
                for (int i = 0; i < right.Count; i++)
                    if (right[i] != null) Snapshot(right[i]);
            }

            if (snapshots.Count == 0) return;

            // Captured fresh on the first maximise of the mission, so a following scene's
            // buttons are snapshotted at their own stock layout rather than a stale one.
            moved = true;
            Layout(map);
        }

        private static void Snapshot(Button button)
        {
            Transform t = button.transform;
            snapshots.Add(new ButtonSnapshot
            {
                Button = button,
                Parent = t.parent,
                LocalPosition = t.localPosition,
                LocalRotation = t.localRotation,
                LocalScale = t.localScale,
            });
        }

        private static void Layout(DynamicMap map)
        {
            Canvas canvas = map.maximizedMapCanvas;
            if (canvas == null || snapshots.Count == 0)
            {
                Restore(map);
                return;
            }

            RectTransform canvasRt = canvas.GetComponent<RectTransform>();
            float halfH = canvasRt != null ? canvasRt.rect.height * 0.5f : Screen.height * 0.5f;

            VirtualMFD mfd = Object.FindObjectOfType<VirtualMFD>();
            List<Button> left = mfd != null ? GameAccess.GetLeftButtons(mfd) : null;
            List<Button> right = mfd != null ? GameAccess.GetRightButtons(mfd) : null;

            // Left column becomes the row above the map, right column the row below it.
            LayoutRow(left, canvas, halfH - RowMargin);
            LayoutRow(right, canvas, -(halfH - RowMargin));
        }

        private static void LayoutRow(List<Button> buttons, Canvas canvas, float centerY)
        {
            if (buttons == null) return;

            float total = 0f;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == null) continue;
                total += Width(buttons[i]) + RowGap;
            }
            if (total > 0f) total -= RowGap;

            float x = -total * 0.5f;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (button == null) continue;

                float w = Width(button);
                Reparent(button, canvas);

                RectTransform rt = button.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x + w * 0.5f, centerY);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;

                x += w + RowGap;
            }
        }

        private static float Width(Button button)
        {
            RectTransform rt = button.GetComponent<RectTransform>();
            float w = rt != null ? rt.rect.width : 0f;
            return w > 0.1f ? w : ButtonWidthFallback;
        }

        /// <summary>Reparent the button to the map canvas so it is measured in canvas units.</summary>
        private static void Reparent(Button button, Canvas canvas)
        {
            Transform t = button.transform;
            if (t.parent == canvas.transform) return;
            t.SetParent(canvas.transform, worldPositionStays: false);
            t.SetAsLastSibling();
        }

        private static void Restore(DynamicMap map)
        {
            if (!moved) return;

            for (int i = 0; i < snapshots.Count; i++)
            {
                ButtonSnapshot s = snapshots[i];
                if (s.Button == null) continue;

                Transform t = s.Button.transform;
                t.SetParent(s.Parent, worldPositionStays: false);
                t.localPosition = s.LocalPosition;
                t.localRotation = s.LocalRotation;
                t.localScale = s.LocalScale;
            }

            snapshots.Clear();
            moved = false;
        }

        /// <summary>Forget captured state at the end of a mission so the next scene re-snapshots.</summary>
        public static void Reset()
        {
            snapshots.Clear();
            moved = false;
        }
    }
}
