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

        private static bool stylesReady;
        private static GUIStyle labelStyle;
        private static GUIStyle sliceStyle;
        private static GUIStyle sliceHotStyle;
        private static GUIStyle toastStyle;
        private static GUIStyle headerStyle;

        private static readonly Color Panel = new Color(0.04f, 0.06f, 0.05f, 0.78f);
        private static readonly Color Accent = new Color(0.45f, 0.95f, 0.55f);
        private static readonly Color Hot = new Color(0.10f, 0.35f, 0.16f, 0.95f);
        private static readonly Color Cold = new Color(0.05f, 0.09f, 0.07f, 0.85f);

        private static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = false,
                wordWrap = false,
            };
            labelStyle.normal.textColor = UiTheme.Green;

            headerStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = UiTheme.Green;

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

        /// <summary>
        /// In-flight wing readout. Drawn as bare green text with no panel behind it, so it
        /// reads as part of the aircraft's own symbology rather than as a mod overlay
        /// sitting on top of the canopy.
        /// </summary>
        public static void DrawStatusPanel(WingRegistry wing)
        {
            EnsureStyles();
            RebuildRows(wing);

            const float w = 260f;
            const float lineHeight = 18f;
            float h = lineHeight * (cachedRows.Count + 1) + 8f;

            Vector2 origin = PanelOrigin(w, h);

            // No GUI.Box: the background plate was the main thing making this look bolted on.
            var line = new Rect(origin.x, origin.y, w, lineHeight);

            GUI.Label(line, cachedHeader, headerStyle);
            for (int i = 0; i < cachedRows.Count; i++)
            {
                line.y += lineHeight;
                GUI.Label(line, cachedRows[i], labelStyle);
            }
        }

        /// <summary>Corner placement, so the readout can be moved clear of the HUD.</summary>
        private static Vector2 PanelOrigin(float w, float h)
        {
            const float margin = 24f;
            switch (Plugin.Config2.HudCorner.Value)
            {
                case HudCorner.TopLeft:
                    return new Vector2(margin, margin);
                case HudCorner.TopRight:
                    return new Vector2(Screen.width - w - margin, margin);
                case HudCorner.BottomLeft:
                    return new Vector2(margin, Screen.height - h - margin);
                case HudCorner.BottomRight:
                    return new Vector2(Screen.width - w - margin, Screen.height - h - margin);
                case HudCorner.MiddleRight:
                default:
                    return new Vector2(Screen.width - w - margin, Screen.height * 0.34f);
            }
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
                    m.Slot, UiTheme.Truncate(m.Name, 14), OrderText(m), m.SlotError));
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

        /// <summary>
        /// Name the target when a member has one, matching the WMC roster. Four rows all
        /// reading "Engage" tell the player nothing once targets are spread across the
        /// wing.
        /// </summary>
        private static string OrderText(WingMember m)
        {
            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
            {
                string code = assigned.definition != null ? assigned.definition.code : assigned.unitName;
                return UiTheme.Truncate(code, 9);
            }
            switch (m.Order)
            {
                case WingOrder.ReturnToBase: return "RTB";
                case WingOrder.FallBack:     return "Fall Back";
                case WingOrder.CoverMe:      return "Cover";
                case WingOrder.OrbitHere:    return "Orbit";
                case WingOrder.DeliverCargo: return "Cargo";
                case WingOrder.LandHere:     return "Landing";
                default:                     return m.Order.ToString();
            }
        }

    }
}
