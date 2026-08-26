using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Wing control panel drawn on the maximised map, in the spirit of the MFD's TGT list.
    ///
    /// The map is the one place in the game where the cursor is free, so this is where
    /// clickable controls actually work — in the cockpit the pointer is captured for
    /// mouse-look, which is why orders in flight go through the radial instead.
    ///
    /// It doubles as the formation debugging view: each row shows live slot error in
    /// metres, so a wingman that is hunting rather than settling is visible at a glance.
    /// </summary>
    internal static class WingMapPanel
    {
        private const float PanelWidth = 330f;
        private const float RowHeight = 22f;

        /// <summary>
        /// True while the cursor is over the panel. The map command layer checks this so a
        /// click on a button does not also drop a waypoint on the map underneath.
        /// </summary>
        public static bool MouseOverPanel { get; private set; }

        private static Rect panelRect;
        private static bool stylesReady;

        private static GUIStyle panel, header, row, rowDim, button, smallButton, groupButton;
        private static Texture2D panelBg, buttonBg, buttonHotBg, accentBg;

        // Set by the panel, consumed by the manager, so button handlers never mutate the
        // wing while the registry is being enumerated for drawing.
        private static readonly List<Action> pending = new List<Action>();

        public static void Draw(WingRegistry wing, MapCommandLayer map)
        {
            if (!Plugin.Config2.ShowMapPanel.Value || !DynamicMap.mapMaximized)
            {
                MouseOverPanel = false;
                return;
            }

            EnsureStyles();

            int rows = Mathf.Max(wing.Count, 1);
            float height = 128f + rows * RowHeight + 84f;
            panelRect = new Rect(Screen.width - PanelWidth - 18f, 90f, PanelWidth, height);

            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            MouseOverPanel = panelRect.Contains(mouse);

            GUI.Box(panelRect, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 10f,
                                         panelRect.width - 24f, panelRect.height - 20f));

            DrawHeader(wing);
            DrawRoster(wing);
            DrawOrders(wing);
            DrawGroups(map);

            GUILayout.EndArea();

            Flush();
        }

        // -------------------------------------------------------------------- sections

        private static void DrawHeader(WingRegistry wing)
        {
            GUILayout.Label("WING COMMAND", header);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", smallButton, GUILayout.Width(26f))) CycleShape(-1);
            GUILayout.Label(Pretty(Plugin.Config2.Shape.Value), row, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", smallButton, GUILayout.Width(26f))) CycleShape(1);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label(
                string.Format("{0} of {1} assigned", wing.Count, Plugin.Config2.MaxWingSize.Value),
                rowDim);
            GUILayout.Space(4f);
        }

        private static void DrawRoster(WingRegistry wing)
        {
            if (wing.Count == 0)
            {
                GUILayout.Label("No wingmen. Select friendly AI aircraft", rowDim);
                GUILayout.Label("on the map, then Add Selected.", rowDim);
                GUILayout.Space(RowHeight - 16f);
                return;
            }

            foreach (WingMember m in wing.Members)
            {
                WingMember captured = m;

                GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));

                GUILayout.Label(m.Slot.ToString(), rowDim, GUILayout.Width(14f));
                GUILayout.Label(Truncate(m.Name, 13), row, GUILayout.Width(104f));
                GUILayout.Label(ShortOrder(m.Order), rowDim, GUILayout.Width(58f));
                GUILayout.Label(SlotErrorText(m), SlotErrorStyle(m), GUILayout.Width(62f));

                if (GUILayout.Button("X", smallButton, GUILayout.Width(24f)))
                    pending.Add(() => Manager()?.RemoveMember(captured));

                GUILayout.EndHorizontal();
            }
        }

        private static void DrawOrders(WingRegistry wing)
        {
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected", button))
                pending.Add(() => Manager()?.AddSelectedFromMap());
            if (GUILayout.Button("Recruit Near", button))
                pending.Add(() => Manager()?.Execute(WingAction.RecruitNearest));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rejoin", button))
                pending.Add(() => Manager()?.Execute(WingAction.Rejoin));
            if (GUILayout.Button("Engage", button))
                pending.Add(() => Manager()?.Execute(WingAction.Engage));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Return To Base", button))
                pending.Add(() => Manager()?.Execute(WingAction.ReturnToBase));
            if (GUILayout.Button("Disband", button))
                pending.Add(() => Manager()?.Execute(WingAction.Disband));
            GUILayout.EndHorizontal();
        }

        private static void DrawGroups(MapCommandLayer map)
        {
            GUILayout.Space(8f);
            GUILayout.Label("SQUAD GROUPS", rowDim);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < MapCommandLayer.GroupCount; i++)
            {
                int captured = i;
                int count = map?.GroupSize(i) ?? 0;
                string label = (i + 1) + (count > 0 ? " (" + count + ")" : "");

                if (GUILayout.Button(label, groupButton))
                    pending.Add(() => map?.RecallGroupExternal(captured));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int i = 0; i < MapCommandLayer.GroupCount; i++)
            {
                int captured = i;
                if (GUILayout.Button("Set", smallButton))
                    pending.Add(() => map?.StoreGroupExternal(captured));
            }
            GUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------------- helpers

        private static void Flush()
        {
            if (pending.Count == 0) return;

            // Copy first: an action may add wingmen, which changes the list being drawn.
            var actions = pending.ToArray();
            pending.Clear();

            foreach (Action a in actions)
            {
                try { a(); }
                catch (Exception e) { Plugin.Logger.LogError("Map panel action failed: " + e); }
            }
        }

        private static WingCommandManager Manager() => WingCommandManager.Instance;

        private static void CycleShape(int direction)
        {
            var values = (FormationShape[])Enum.GetValues(typeof(FormationShape));
            int index = Array.IndexOf(values, Plugin.Config2.Shape.Value);
            index = (index + direction + values.Length) % values.Length;
            Plugin.Config2.Shape.Value = values[index];
        }

        private static string SlotErrorText(WingMember m)
        {
            if (m.Order != WingOrder.Formation) return "-";
            if (m.SlotError <= 0f) return "...";
            return m.SlotError < 10000f
                ? m.SlotError.ToString("F0") + " m"
                : (m.SlotError / 1000f).ToString("F1") + " km";
        }

        /// <summary>
        /// Green once the wingman is settled in the slot, amber while closing, dim when
        /// the order is not formation. Makes an oscillating controller obvious.
        /// </summary>
        private static GUIStyle SlotErrorStyle(WingMember m)
        {
            if (m.Order != WingOrder.Formation) return rowDim;
            return m.SlotError > 0f && m.SlotError < 250f ? row : rowDim;
        }

        private static string ShortOrder(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation: return "FORM";
                case WingOrder.Engage: return "ENGAGE";
                case WingOrder.ReturnToBase: return "RTB";
                default: return order.ToString();
            }
        }

        private static string Pretty(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "Echelon Right";
                case FormationShape.EchelonLeft: return "Echelon Left";
                case FormationShape.LineAbreast: return "Line Abreast";
                case FormationShape.Trail: return "Trail";
                case FormationShape.CombatSpread: return "Combat Spread";
                default: return shape.ToString();
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ----------------------------------------------------------------------- styles

        private static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            Color accent = WingMapTint.WingColor;
            Color friendly = WingMapTint.FriendlyThemeColor;

            panelBg = Solid(new Color(0.03f, 0.06f, 0.06f, 0.90f));
            buttonBg = Solid(new Color(0.07f, 0.13f, 0.13f, 0.95f));
            buttonHotBg = Solid(new Color(0.12f, 0.26f, 0.26f, 1f));
            accentBg = Solid(new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 1f));

            panel = new GUIStyle(GUI.skin.box);
            panel.normal.background = panelBg;
            panel.border = new RectOffset(2, 2, 2, 2);

            header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            header.normal.textColor = accent;

            row = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            row.normal.textColor = friendly;

            rowDim = new GUIStyle(row);
            rowDim.normal.textColor = new Color(friendly.r * 0.62f, friendly.g * 0.62f, friendly.b * 0.62f);

            button = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            button.normal.background = buttonBg;
            button.hover.background = buttonHotBg;
            button.active.background = accentBg;
            button.normal.textColor = friendly;
            button.hover.textColor = Color.white;
            button.padding = new RectOffset(6, 6, 4, 4);

            smallButton = new GUIStyle(button) { fontSize = 10 };
            groupButton = new GUIStyle(button) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Rebuild styles after a theme or colour change.</summary>
        public static void InvalidateStyles() => stylesReady = false;
    }
}
