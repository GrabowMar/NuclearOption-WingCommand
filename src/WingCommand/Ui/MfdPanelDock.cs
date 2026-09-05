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
    /// <para>Each slot's origin is raised from the bottom by the rendered display panel's
    /// height, and the screen keeps a top-left pivot. This matters because some stock
    /// screen roots report zero height even though their display panel is 596px tall.
    /// Vanilla can still write <c>localPosition = 0</c>, while every visible surface ends
    /// at the foot of the column and leaves its true remaining space above.</para>
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
            // The map viewport stops above its footer, but the left instrument column
            // should share the footer's lower baseline. Keep the same top and extend the
            // dock through the reserved bottom band to the canvas margin.
            float footerBottom = -columns.Canvas.y * 0.5f + MfdLayout.Margin;
            float dockHeight = Mathf.Max(columns.Panel.height, columns.Panel.y - footerBottom);
            dock.sizeDelta = new Vector2(columns.Panel.width, dockHeight);
            dock.anchoredPosition = MfdLayout.TopLeftOf(columns.Panel);
            dock.localScale = Vector3.one;

            // Do not paint a full-height dock backing. It was the source of the isolated
            // blue-green tint below a 596px MFD: the dock is deliberately taller so it can
            // coexist with the game's bottom spawn strip. MfdMapDeck now supplies one
            // coherent passive surface behind the entire maximised-map UI instead.
            var bg = dock.GetComponent<Image>();
            if (bg != null)
            {
                bg.raycastTarget = false;
                bg.enabled = false;
            }

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
        /// Idempotent: a screen already in a slot is left in place, but an adapter that
        /// deferred while the native controller initialized gets another chance to attach.
        /// </summary>
        public static void Dock(MFDScreen screen)
        {
            if (screen == null || dock == null) return;

            var rt = screen.transform as RectTransform;
            if (rt == null) return;
            if (IsDocked(screen))
            {
                if (IsStockScreen(screen) && !VanillaMfdRebuild.IsHosted(screen))
                    VanillaMfdRebuild.TryApply(screen);
                return;
            }

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

            RectTransform slot = MakeSlot(screen.name);
            rt.SetParent(slot, worldPositionStays: false);

            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;

            // Own the presentation surface outright. The native controller remains alive
            // behind it as the data/action authority, without leaving its prefab layout in
            // the rendering or input path.
            if (IsStockScreen(screen)) VanillaMfdRebuild.TryApply(screen);

            // CloseScreen may have run while the screen still belonged to the vanilla
            // bezel, when the chrome patch intentionally does nothing. Reconcile the root
            // graphics now that the screen is docked so inactive backplates cannot follow
            // it into the visible column.
            MfdScreenChromePatch.SyncDockedState(screen);
            MfdPresentation.Apply(screen);
            AlignToBottom(screen, slot);
        }

        public const float MaxColumnWidth = 520f;

        /// <summary>
        /// A slot child of the dock that carries bottom-left pinning for deterministic alignment.
        /// </summary>
        private static RectTransform MakeSlot(string screenName)
        {
            var go = new GameObject("Slot_" + screenName, typeof(RectTransform));
            var slot = go.GetComponent<RectTransform>();
            slot.SetParent(dock, worldPositionStays: false);

            slot.anchorMin = Vector2.zero;
            slot.anchorMax = Vector2.zero;
            slot.pivot = Vector2.zero;
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = dock != null ? dock.sizeDelta : new Vector2(AvTokens.PanelWidth, 1000f);
            slot.localScale = Vector3.one;

            slots.Add(slot);
            return slot;
        }

        /// <summary>
        /// A stock game panel, as opposed to one the mods built from scratch. Both mods name
        /// their content object "Content"; every stock screen's is "DisplayPanel". Width
        /// alone would misread a stock panel that happened to be the mod column's width.
        /// </summary>
        private static bool IsStockScreen(MFDScreen screen) =>
            screen != null && screen.displayPanel != null && screen.displayPanel.name != "Content";

        /// <summary>Whether this exact screen currently belongs to one of our dock slots.</summary>
        public static bool IsDocked(MFDScreen screen)
        {
            if (screen == null || dock == null) return false;
            RectTransform slot = screen.transform.parent as RectTransform;
            return slot != null && slot.parent == dock;
        }

        /// <summary>The height of the surface the player actually sees.</summary>
        public static float VisibleHeight(MFDScreen screen)
        {
            if (screen == null) return 0f;

            var display = screen.displayPanel == null
                ? null
                : screen.displayPanel.transform as RectTransform;
            float height = display == null ? 0f : display.rect.height;

            if (height <= 1f)
            {
                var root = screen.transform as RectTransform;
                height = root == null ? 0f : root.rect.height;
            }

            if (height <= 1f) height = AvTokens.PanelHeight;
            return dock == null ? height : Mathf.Min(height, dock.rect.height);
        }

        /// <summary>The full left bay, including the map footer's vertical band.</summary>
        public static float AvailableHeight(float fallback) =>
            dock == null || dock.rect.height <= 1f ? fallback : dock.rect.height;

        private static void AlignToBottom(MFDScreen screen, RectTransform slot)
        {
            if (screen == null || slot == null) return;

            var rt = screen.transform as RectTransform;
            if (rt == null) return;

            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            slot.anchoredPosition = new Vector2(0f, VisibleHeight(screen));
        }

        /// <summary>
        /// Align a shown screen deterministically to the dock's bottom-left cell.
        /// </summary>
        public static void OnScreenShown(MFDScreen screen)
        {
            if (screen == null || dock == null) return;
            if (!IsDocked(screen)) return;

            var rt = screen.transform as RectTransform;
            if (rt == null) return;

            var slot = rt.parent as RectTransform;
            if (slot == null) return;

            AlignToBottom(screen, slot);

            if (IsStockScreen(screen) && !VanillaMfdRebuild.IsHosted(screen))
                VanillaMfdRebuild.TryApply(screen);
            VanillaMfdRebuild.OnShown(screen);
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
            // Restore each native displayPanel pointer before moving the screen out of the
            // dock; a late CloseScreen/ShowScreen callback will then always see native UI.
            VanillaMfdRebuild.Restore();

            for (int i = 0; i < docked.Count; i++) docked[i].Restore();
            docked.Clear();
            MfdPresentation.ApplyAll();

            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) Object.Destroy(slots[i].gameObject);
            slots.Clear();
        }

        /// <summary>Forget the dock at end of mission; the next scene builds its own.</summary>
        public static void Reset()
        {
            VanillaMfdRebuild.Reset();
            MfdScreenChromePatch.Reset();
            MfdPresentation.Reset();
            docked.Clear();
            slots.Clear();
            dock = null;
        }
    }
}
