using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// IMGUI rendering for the wing panel, the radial menu and transient messages.
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

        private static Texture2D pixel;
        private static GUIStyle panelStyle;
        private static GUIStyle labelStyle;
        private static GUIStyle sliceStyle;
        private static GUIStyle sliceHotStyle;
        private static GUIStyle toastStyle;

        private static readonly Color Panel = new Color(0.04f, 0.06f, 0.05f, 0.78f);
        private static readonly Color Accent = new Color(0.45f, 0.95f, 0.55f);
        private static readonly Color Hot = new Color(0.10f, 0.35f, 0.16f, 0.95f);
        private static readonly Color Cold = new Color(0.05f, 0.09f, 0.07f, 0.85f);

        private static void EnsureStyles()
        {
            if (pixel != null) return;

            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();

            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            panelStyle.normal.background = Solid(Panel);

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = false,
                wordWrap = false,
            };
            labelStyle.normal.textColor = Accent;

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

        // Unity calls OnGUI several times per frame (layout, repaint, each input event),
        // so formatting these rows inline produced a fresh set of strings several times a
        // frame. They are rebuilt on a timer instead and simply drawn in between.
        private static readonly List<string> cachedRows = new List<string>();
        private static string cachedHeader = "";
        private static float nextRowRebuild;

        public static void DrawStatusPanel(WingRegistry wing)
        {
            EnsureStyles();
            RebuildRows(wing);

            const float w = 250f;
            float h = 34f + cachedRows.Count * 18f;
            var rect = new Rect(14f, Screen.height * 0.5f - h * 0.5f, w, h);

            GUI.Box(rect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));

            GUILayout.Label(cachedHeader, labelStyle);
            for (int i = 0; i < cachedRows.Count; i++)
                GUILayout.Label(cachedRows[i], labelStyle);

            GUILayout.EndArea();
        }

        private static void RebuildRows(WingRegistry wing)
        {
            if (Time.unscaledTime < nextRowRebuild && cachedRows.Count == wing.Count) return;
            nextRowRebuild = Time.unscaledTime + 0.2f;

            cachedHeader = "WING  -  " + Plugin.Config2.Shape.Value;

            cachedRows.Clear();
            foreach (WingMember m in wing.Members)
            {
                cachedRows.Add(string.Format(
                    "{0}  {1,-14} {2,-9} {3:F0} m",
                    m.Slot, Truncate(m.Name, 14), m.Order, m.SlotError));
            }
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

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
