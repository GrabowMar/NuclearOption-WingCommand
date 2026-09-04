using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// The left column every MFD screen is shown in.
    ///
    /// <para><b>Why a dock and not a position.</b> <c>VirtualMFD.showPos</c> is
    /// <c>Vector3.zero</c>, and <c>MFDScreen.ShowScreen</c> assigns it straight to
    /// <c>transform.localPosition</c>. A screen therefore has no remembered "home" — it is
    /// placed entirely by its <b>parent and its anchors</b>, and any <c>anchoredPosition</c>
    /// a mod writes is overwritten the next time the screen is opened. Reparenting the
    /// screens into a container the mod positions is the only placement vanilla will not
    /// undo.</para>
    ///
    /// <para>The dock's pivot is its top-left corner and each docked screen's pivot is set to
    /// match, so <c>localPosition = 0</c> lands a panel's top-left exactly on the column's
    /// top-left however tall that panel happens to be.</para>
    /// </summary>
    internal static class MfdPanelDock
    {
        public const string DockName = "NOAvionics.PanelDock";

        private sealed class Docked
        {
            public MFDScreen Screen;
            public Transform Parent;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 AnchoredPosition;
            public Vector3 LocalPosition;
            public Vector3 LocalScale;

            public void Restore()
            {
                if (Screen == null) return;

                var rt = Screen.transform as RectTransform;
                if (rt == null) return;

                rt.SetParent(Parent, worldPositionStays: false);
                rt.anchorMin = AnchorMin;
                rt.anchorMax = AnchorMax;
                rt.pivot = Pivot;
                rt.anchoredPosition = AnchoredPosition;
                rt.localPosition = LocalPosition;
                rt.localScale = LocalScale;
            }
        }

        private static RectTransform dock;
        private static readonly List<Docked> docked = new List<Docked>();
        private static readonly List<RectTransform> slots = new List<RectTransform>();


        /// <summary>Build or re-find the dock, sized and placed on the panel column.</summary>
        public static RectTransform Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return null;

            if (dock == null)
            {
                Transform existing = canvas.transform.Find(DockName);
                dock = existing as RectTransform;
            }

            if (dock == null)
            {
                var go = new GameObject(DockName, typeof(RectTransform));
                dock = go.GetComponent<RectTransform>();
                dock.SetParent(canvas.transform, worldPositionStays: false);
            }

            // Anchored to the canvas centre but pivoted on its own top-left corner, so a
            // docked screen's matching top-left pivot lands exactly there when vanilla
            // writes localPosition = 0. A centred pivot here would put every panel's corner
            // in the middle of the column.
            dock.anchorMin = dock.anchorMax = new Vector2(0.5f, 0.5f);
            dock.pivot = new Vector2(0f, 1f);
            dock.sizeDelta = new Vector2(columns.Panel.width, columns.Panel.height);
            dock.anchoredPosition = MfdLayout.TopLeftOf(columns.Panel);
            dock.localScale = Vector3.one;

            // A quiet backing so the gaps around a panel are column, not terrain. Each stock
            // panel draws its own ground on its slot (VanillaPanelSkin); the mod screens
            // bring their own.
            var bg = dock.GetComponent<Image>();
            if (bg == null) bg = dock.gameObject.AddComponent<Image>();
            bg.sprite = AvSprites.Panel;
            bg.type = Image.Type.Sliced;
            bg.color = AvTheme.Unity(AvTokens.Ground.WithAlpha(0.82f));
            bg.raycastTarget = true;
            bg.enabled = true;

            // Last, not first: the dock is a sibling of the map on the same canvas, and
            // sibling order is draw order. First would file every panel behind the map.
            dock.SetAsLastSibling();

            return dock;
        }

        /// <summary>
        /// Move a screen into the column.
        ///
        /// <para>Each screen gets its own <b>slot</b> — a stretched child of the dock whose
        /// only job is to carry a pivot. Vanilla writes <c>localPosition = 0</c> on the
        /// screen, which places the screen's pivot exactly on its parent's pivot, so the
        /// parent's pivot is the alignment. Encoding it in a slot means a screen's own
        /// pivot never has to be touched.</para>
        ///
        /// Idempotent: a screen already in a slot is left alone.
        /// </summary>
        public static void Dock(MFDScreen screen)
        {
            if (screen == null || dock == null) return;

            var rt = screen.transform as RectTransform;
            if (rt == null) return;
            if (rt.parent != null && rt.parent.parent == dock) return;

            bool mine = IsModPanel(rt);

            docked.Add(new Docked
            {
                Screen = screen,
                Parent = rt.parent,
                AnchorMin = rt.anchorMin,
                AnchorMax = rt.anchorMax,
                Pivot = rt.pivot,
                AnchoredPosition = rt.anchoredPosition,
                LocalPosition = rt.localPosition,
                LocalScale = rt.localScale,
            });

            RectTransform slot = MakeSlot(screen.name, mine);
            rt.SetParent(slot, worldPositionStays: false);

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;

            // A stock game panel has no ground of its own; draw one on its slot, behind it.
            if (IsStockScreen(screen)) VanillaPanelSkin.Apply(screen, slot);
        }

        public const float MaxColumnWidth = 520f;

        /// <summary>
        /// A slot child of the dock that carries top-left pinning for deterministic alignment.
        /// </summary>
        private static RectTransform MakeSlot(string screenName, bool topLeft)
        {
            var go = new GameObject("Slot_" + screenName, typeof(RectTransform));
            var slot = go.GetComponent<RectTransform>();
            slot.SetParent(dock, worldPositionStays: false);

            slot.anchorMin = new Vector2(0f, 1f);
            slot.anchorMax = new Vector2(0f, 1f);
            slot.pivot = new Vector2(0f, 1f);
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = dock != null ? dock.sizeDelta : new Vector2(AvTokens.PanelWidth, 1000f);
            slot.localScale = Vector3.one;

            slots.Add(slot);
            return slot;
        }

        /// <summary>
        /// Whether this screen is one the mods built to the column's width.
        /// </summary>
        private static bool IsModPanel(RectTransform rt) =>
            Mathf.Abs(rt.rect.width - AvTokens.PanelWidth) < 2f;

        /// <summary>
        /// A stock game panel, as opposed to one the mods built from scratch. Both mods name
        /// their content object "Content"; every stock screen's is "DisplayPanel". Width
        /// alone would misread a stock panel that happened to be the mod column's width.
        /// </summary>
        private static bool IsStockScreen(MFDScreen screen) =>
            screen != null && screen.displayPanel != null && screen.displayPanel.name != "Content";

        /// <summary>
        /// Align a shown screen deterministically to the dock's top-left cell.
        /// </summary>
        public static void OnScreenShown(MFDScreen screen)
        {
            if (screen == null || dock == null) return;

            var rt = screen.transform as RectTransform;
            if (rt == null) return;

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;

            var slot = rt.parent as RectTransform;
            if (slot == null || slot.parent != dock) return;

            slot.anchoredPosition = Vector2.zero;

            // A stock panel is nudged down so its top row clears the canvas edge, and its
            // ground comes up with it. Runs last so the drop wins over the zero above.
            if (VanillaPanelSkin.IsSkinned(screen)) VanillaPanelSkin.OnShown(screen);
        }

        /// <summary>Dock every screen the two mods own, on both vanilla bezel columns.</summary>
        public static void DockModScreens(VirtualMFD mfd)
        {
            if (mfd == null || dock == null) return;

            DockOwned(GameAccess.GetLeftScreens(mfd));
            DockOwned(GameAccess.GetRightScreens(mfd));
        }

        /// <summary>
        /// Dock every screen, stock ones included.
        ///
        /// Docking only the mod screens was the obvious conservative choice and it was wrong:
        /// a stock screen's position was chosen for a bezel column beside a 900px map, so with
        /// the map filling the centre the faction-info panel simply floated across the middle
        /// of the terrain and over the rail. Every screen the bezel can open has to live in
        /// the one column the layout reserves for it, or the column is not a layout — it is
        /// just where some of the panels happen to be.
        /// </summary>
        private static void DockOwned(List<MFDScreen> screens)
        {
            if (screens == null) return;

            for (int i = 0; i < screens.Count; i++)
            {
                MFDScreen screen = screens[i];
                if (screen == null) continue;

                Dock(screen);
            }
        }

        /// <summary>
        /// Close whichever mod screen is open other than <paramref name="opening"/>.
        ///
        /// The two vanilla bezel columns are radio-buttoned independently: opening a left
        /// screen closes the other left screens and does not touch the right. That was fine
        /// when the columns were on opposite sides of the map; now that every panel renders
        /// into the same dock, a left screen and a right screen open at once would sit on top
        /// of each other.
        /// </summary>
        public static void CloseOthers(VirtualMFD mfd, MFDScreen opening)
        {
            if (mfd == null) return;

            CloseOthersIn(GameAccess.GetLeftScreens(mfd), opening, left: true);
            CloseOthersIn(GameAccess.GetRightScreens(mfd), opening, left: false);
        }

        private static void CloseOthersIn(List<MFDScreen> screens, MFDScreen opening, bool left)
        {
            if (screens == null) return;

            for (int i = 0; i < screens.Count; i++)
            {
                MFDScreen screen = screens[i];
                if (screen == null || screen == opening) continue;
                if (!screen.isActive) continue;

                // Vanilla's own hide offset: one screen width, away from that column's side.
                screen.CloseScreen(Screen.width * (left ? Vector3.left : Vector3.right));
            }
        }

        /// <summary>Put every docked screen back where it came from.</summary>
        public static void Restore()
        {
            // Stock panel visuals first, while they are still parented in the dock; then the
            // screens, then the slots they lived in — destroying a slot first would undo the
            // reparent out from under its screen.
            VanillaPanelSkin.Restore();

            for (int i = 0; i < docked.Count; i++) docked[i].Restore();
            docked.Clear();

            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) Object.Destroy(slots[i].gameObject);
            slots.Clear();
        }

        /// <summary>Forget the dock at end of mission; the next scene builds its own.</summary>
        public static void Reset()
        {
            VanillaPanelSkin.Reset();
            docked.Clear();
            slots.Clear();
            dock = null;
        }
    }
}
