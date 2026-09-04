using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Gives the game's own docked map panels — BDF, PALA, TGT, MIS, MAP, HUD — a ground so
    /// their rows read as an instrument rather than green text over the terrain.
    ///
    /// <para>The stock panels have complex, panel-specific internal layouts, so this touches
    /// them as little as possible: the ground is an opaque filled rectangle drawn on the dock
    /// slot (always the full column size), <em>behind</em> the stock content; the stock root
    /// image is only cleared so it does not double up; and the whole stock content is nudged
    /// — never re-anchored or resized — just far enough to sit inside the canvas. Every value
    /// touched is snapshotted and put back on <see cref="Restore"/>.</para>
    /// </summary>
    internal static class VanillaPanelSkin
    {
        /// <summary>How far below the canvas top the content's first row is pulled — clear
        /// of the edge and the kill feed.</summary>
        private const float TopMargin = 34f;

        private sealed class Skin
        {
            public MFDScreen Screen;

            public Image Root;
            public Color RootColor;
            public bool RootRaycast;

            public RectTransform Rt;

            /// <summary>When the panel was last shown; the drop is re-measured for a few
            /// seconds after because the faction panels populate their rows a frame or two
            /// late and grow off the top as they do.</summary>
            public float ShownAt;

            public GameObject Ground;

            /// <summary>One closure per element the restyle touched, each putting it back.</summary>
            public readonly List<Action> Undo = new List<Action>();

            /// <summary>Instance ids already restyled, so the repeated pass in <see cref="Tick"/>
            /// only has to look at rows the game has added since — most panels build their
            /// item rows lazily, after the panel is first docked.</summary>
            public readonly HashSet<int> Styled = new HashSet<int>();

            public void Restore()
            {
                if (Root != null)
                {
                    Root.color = RootColor;
                    Root.raycastTarget = RootRaycast;
                }

                for (int i = Undo.Count - 1; i >= 0; i--)
                {
                    try { Undo[i](); } catch { /* an element was destroyed; nothing to undo */ }
                }
                Undo.Clear();

                // The root's anchored position is re-zeroed by MfdPanelDock.OnScreenShown on
                // every show, so there is nothing of ours to put back there.
                if (Ground != null) UnityEngine.Object.Destroy(Ground);
                Ground = null;
            }
        }

        private static readonly Dictionary<MFDScreen, Skin> skins = new Dictionary<MFDScreen, Skin>();

        /// <summary>
        /// Build the ground for a stock panel. <paramref name="slot"/> is the dock slot the
        /// screen was reparented into — full column size — and the ground is drawn on it, so
        /// it never depends on the stock panel's own (often tiny) rect.
        /// </summary>
        public static void Apply(MFDScreen screen, RectTransform slot)
        {
            if (screen == null || slot == null || skins.ContainsKey(screen)) return;

            var skin = new Skin { Screen = screen, Rt = screen.transform as RectTransform };

            // The stock root graphic, cleared so it does not sit as a second fill over the
            // ground. MfdScreenChromePatch toggles its enabled flag; the colour is ours.
            Image root = screen.GetComponent<Image>();
            if (root != null)
            {
                skin.Root = root;
                skin.RootColor = root.color;
                skin.RootRaycast = root.raycastTarget;
                root.color = Color.clear;
            }

            // The ground: an opaque filled rectangle plus a hairline edge, on the slot,
            // behind the screen. Flat — AvSprites.Panel's scaled edge reads as a heavy black
            // border, which is not wanted here.
            var ground = new GameObject("AvGround", typeof(RectTransform), typeof(Image));
            var grt = ground.GetComponent<RectTransform>();
            grt.SetParent(slot, worldPositionStays: false);
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = grt.offsetMax = Vector2.zero;
            grt.localScale = Vector3.one;
            grt.SetAsFirstSibling();

            var fill = ground.GetComponent<Image>();
            AvStyle style = AvStyleHost.Style("stock-panel");
            Color ink = AvStyleHost.Resolve(style.Background, AvTheme.Unity(AvTokens.Ground));
            fill.sprite = null;
            fill.color = new Color(ink.r, ink.g, ink.b, 1f);   // opaque: it also covers the cockpit MFD behind it
            fill.raycastTarget = true;

            Vector2 box = slot.sizeDelta;
            AvKit.Outline(grt, new Rect(0f, 0f, box.x, box.y), AvTheme.Hairline);
            AvKit.CornerTicks(grt, new Rect(0f, 0f, box.x, box.y), AvTheme.Hairline);

            skin.Ground = ground;
            ground.SetActive(false);

            TryRestyle(screen, skin);

            skins[screen] = skin;
        }

        // ---------------------------------------------------------------- restyle

        /// <summary>
        /// Recolour the stock content to the avionics palette WMC and OPS use — labels dim,
        /// values bright, rules hairline, and section boxes cleared so the content sits on
        /// the one ground instead of a box inside a box — without moving anything. The game's
        /// own <c>TextStyleApplier</c> / <c>ImageStyleApplier</c>
        /// components are switched off first so they stop re-imposing the stock green; that
        /// and every colour is recorded for <see cref="Skin.Restore"/>.
        ///
        /// <para>Idempotent per element (<see cref="Skin.Styled"/>) and safe to call
        /// repeatedly: several stock panels — the faction ones especially — instantiate their
        /// row prefabs lazily on first refresh, well after the panel is docked, so a single
        /// pass at dock time misses everything that has not been built yet.</para>
        /// </summary>
        private static void Restyle(MFDScreen screen, Skin skin)
        {
            if (screen.displayPanel == null) return;
            Transform root = screen.displayPanel.transform;

            Color dim = AvTheme.Dim;
            Color primary = AvTheme.TextPrimary;
            Color hairline = AvTheme.Hairline;

            foreach (var applier in root.GetComponentsInChildren<TextStyleApplier>(true))
                Disable(skin, applier);
            foreach (var applier in root.GetComponentsInChildren<ImageStyleApplier>(true))
                Disable(skin, applier);

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label == null || !skin.Styled.Add(label.GetInstanceID())) continue;
                string n = label.name.ToLowerInvariant();

                Color target;
                bool bold = false;
                if (n.Contains("value") || n == "factionname_value")
                {
                    target = primary;
                    bold = n.Contains("factionname");
                }
                else if (label.transform.parent == root && n == "header")
                {
                    target = primary;   // the panel's own title, one level under the display panel
                    bold = true;
                }
                else
                {
                    target = dim;       // section headers, column labels, button captions
                }

                Color was = label.color;
                FontStyles wasStyle = label.fontStyle;
                skin.Undo.Add(() => { if (label != null) { label.color = was; label.fontStyle = wasStyle; } });
                label.color = target;
                if (bold) label.fontStyle |= FontStyles.Bold;
            }

            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.GetComponent<Button>() != null) continue;
                if (!skin.Styled.Add(img.GetInstanceID())) continue;

                string n = img.name.ToLowerInvariant();

                Color target;
                if (n.Contains("line"))
                    target = hairline;                          // Top_Line / Bottom_Line rules
                else if (n.Contains("container") || n == "buttons" || n.EndsWith("options") ||
                         n == "targetlist")
                    target = Color.clear;                       // stock section boxes — one ground, not a box inside a box
                else
                    continue;                                   // toggles, icons, scrollbars, faction image — left alone

                Color was = img.color;
                skin.Undo.Add(() => { if (img != null) img.color = was; });
                img.color = target;
            }
        }

        private static void Disable(Skin skin, Behaviour applier)
        {
            if (applier == null || !applier.enabled) return;
            applier.enabled = false;
            skin.Undo.Add(() => { if (applier != null) applier.enabled = true; });
        }

        /// <summary>
        /// Called each time a stock panel is shown: measure how far its content hangs off
        /// the top of the canvas (several stock panels are laid out for a shorter bezel and
        /// start well above the origin) and pull the whole panel down by exactly that, so
        /// its first row lands <see cref="TopMargin"/> below the edge. The panel moves as one
        /// — no stock child is re-laid-out. <see cref="MfdPanelDock.OnScreenShown"/> has just
        /// set the root to (0,0), so this drops it from there.
        /// </summary>
        public static void OnShown(MFDScreen screen)
        {
            if (screen == null || !skins.TryGetValue(screen, out Skin skin) || skin.Rt == null) return;

            if (skin.Ground != null) skin.Ground.SetActive(true);
            skin.ShownAt = Time.unscaledTime;
            TryRestyle(screen, skin);
            MeasureAndDrop(screen, skin);
        }

        /// <summary>
        /// Re-measure the drop, and pick up rows the game has only just built, for a couple
        /// of seconds after a panel is shown. The faction panels in particular build their
        /// rows a frame or two late — off the top, and stock green — so the pass at dock time
        /// misses them. Called every frame from <see cref="WingCommandManager"/>.
        /// </summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            foreach (Skin skin in skins.Values)
            {
                if (skin.Screen == null || !skin.Screen.isActive) continue;
                if (now - skin.ShownAt > 3f) continue;
                TryRestyle(skin.Screen, skin);
                MeasureAndDrop(skin.Screen, skin);
            }
        }

        private static void TryRestyle(MFDScreen screen, Skin skin)
        {
            try { Restyle(screen, skin); }
            catch (Exception e) { Plugin.Logger.LogWarning("VanillaPanelSkin restyle failed: " + e.Message); }
        }

        /// <summary>
        /// From the root's zeroed position, measure how far the panel's highest rendered
        /// point sits above <see cref="TopMargin"/> and drop the whole root by exactly that.
        /// Always computed from zero, so it is safe to call repeatedly.
        /// </summary>
        private static void MeasureAndDrop(MFDScreen screen, Skin skin)
        {
            if (skin.Rt == null) return;

            RectTransform display = screen.displayPanel != null
                ? screen.displayPanel.transform as RectTransform
                : null;

            skin.Rt.anchoredPosition = Vector2.zero;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(skin.Rt);
            if (display != null) LayoutRebuilder.ForceRebuildLayoutImmediate(display);

            Canvas canvas = skin.Rt.GetComponentInParent<Canvas>();
            var canvasRt = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (canvasRt == null) return;

            float top = HighestLocalY(skin.Rt, canvasRt);
            if (display != null) top = Mathf.Max(top, HighestLocalY(display, canvasRt));

            float overhang = top - (canvasRt.rect.yMax - TopMargin);
            if (overhang > 0f) skin.Rt.anchoredPosition = new Vector2(0f, -overhang);
        }

        private static readonly Vector3[] cornerScratch = new Vector3[4];

        private static float HighestLocalY(RectTransform rt, RectTransform space)
        {
            rt.GetWorldCorners(cornerScratch);
            float top = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float y = space.InverseTransformPoint(cornerScratch[i]).y;
                if (y > top) top = y;
            }
            return top;
        }

        /// <summary>Hide a stock panel's ground with the panel; it is a slot child, which stays active.</summary>
        public static void SetGroundVisible(MFDScreen screen, bool visible)
        {
            if (screen == null || !skins.TryGetValue(screen, out Skin skin)) return;
            if (skin.Ground != null) skin.Ground.SetActive(visible);
        }

        public static bool IsSkinned(MFDScreen screen) => screen != null && skins.ContainsKey(screen);

        /// <summary>Undo every skinned panel and forget them.</summary>
        public static void Restore()
        {
            foreach (Skin skin in skins.Values) skin.Restore();
            skins.Clear();
        }

        /// <summary>Drop state at end of mission; the next scene re-skins its own panels.</summary>
        public static void Reset() => skins.Clear();
    }
}
