using System.Collections.Generic;
using HarmonyLib;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Lays the maximised tactical map out in three columns: mod panels on the left, the map
    /// in the centre, and one thin rail of buttons on the right.
    ///
    /// <para>The stock screen puts a 900×900 map square in the middle of the canvas with a
    /// column of bezel buttons down each side, which leaves roughly 500 units of dead space
    /// on both flanks and no coherent place for a mod panel. The previous version of this
    /// file tried to solve that by turning the two bezel columns into rows above and below
    /// the map; the result read as scattered, and it was answering a question nobody asked —
    /// the columns were never in the map's way. This lays out all three regions instead.</para>
    ///
    /// <para><b>Everything here is reversible.</b> Vanilla state is snapshotted on the first
    /// maximise and restored on minimise, so a player who turns the setting off, or removes
    /// the mod, gets the stock screen back exactly.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class MfdRailPatch
    {
        /// <summary>Extra size the map's background carries over the map viewport, as vanilla does.</summary>
        private const float BackgroundBleed = 20f;

        private sealed class MapSnapshot
        {
            public RectTransform Root;
            public Vector2 RootSize;
            public Vector2 RootAnchoredPosition;
            public Vector2 RootAnchorMin;
            public Vector2 RootAnchorMax;
            public Vector2 RootPivot;

            public RectTransform Background;
            public Vector2 BackgroundSize;
            public Image BackgroundImage;
            public Sprite BackgroundSprite;
            public Image.Type BackgroundType;
            public Color BackgroundColor;

            public void Restore()
            {
                if (Root != null)
                {
                    Root.anchorMin = RootAnchorMin;
                    Root.anchorMax = RootAnchorMax;
                    Root.pivot = RootPivot;
                    Root.sizeDelta = RootSize;
                    Root.anchoredPosition = RootAnchoredPosition;
                }

                if (Background != null) Background.sizeDelta = BackgroundSize;

                if (BackgroundImage != null)
                {
                    BackgroundImage.sprite = BackgroundSprite;
                    BackgroundImage.type = BackgroundType;
                    BackgroundImage.color = BackgroundColor;
                }
            }

            public void RestoreFraming(DynamicMap dynamicMap)
            {
                if (Root != null)
                {
                    Root.anchorMin = new Vector2(0.5f, 0.5f);
                    Root.anchorMax = new Vector2(0.5f, 0.5f);
                    Root.pivot = new Vector2(0.5f, 0.5f);
                    Root.localPosition = Vector3.zero;
                    Root.localRotation = Quaternion.identity;
                    Root.localScale = Vector3.one;
                    if (dynamicMap != null) Root.sizeDelta = Vector2.one * dynamicMap.mapScaleMinimized;
                }

                if (Background != null && dynamicMap != null)
                {
                    Background.sizeDelta = Vector2.one * dynamicMap.mapScaleMinimized + new Vector2(20f, 20f);
                    Background.localPosition = Vector3.zero;
                    Background.localScale = Vector3.one;
                }

                if (BackgroundImage != null)
                {
                    BackgroundImage.sprite = BackgroundSprite;
                    BackgroundImage.type = BackgroundType;
                    BackgroundImage.color = BackgroundColor;
                }

                if (dynamicMap != null)
                {
                    dynamicMap.mapScaleCurrent = dynamicMap.mapScaleMinimized;
                    dynamicMap.mapDisplayFactor = dynamicMap.mapScaleMaximized / dynamicMap.mapDimension;
                    var centerMethod = AccessTools.Method(typeof(DynamicMap), "CenterMinimizedMap");
                    centerMethod?.Invoke(dynamicMap, null);
                }
            }
        }

        private sealed class ButtonSnapshot
        {
            public Button Button;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public MfdRail.ButtonSkin Skin;

            public void Restore()
            {
                Skin?.Restore();

                if (Button == null) return;

                Transform t = Button.transform;
                t.SetParent(Parent, worldPositionStays: false);
                t.localPosition = LocalPosition;
                t.localRotation = LocalRotation;
                t.localScale = LocalScale;
            }
        }

        /// <summary>
        /// A bezel column's backdrop, hidden once the rail has emptied it.
        ///
        /// LeftButtons and RightButtons each carry their own Image. Reparenting the buttons
        /// out leaves those two panels behind as small dark rectangles floating either side
        /// of the map — the frame of a bezel that no longer has anything in it.
        /// </summary>
        private sealed class ContainerSkin
        {
            public Image Backdrop;
            public bool WasEnabled;

            public void Restore()
            {
                if (Backdrop != null) Backdrop.enabled = WasEnabled;
            }
        }

        private static readonly List<ButtonSnapshot> buttons = new List<ButtonSnapshot>();
        private static readonly List<ContainerSkin> containers = new List<ContainerSkin>();

        /// <summary>Hairline outline and corner ticks around the map viewport, so the three
        /// columns read as one instrument rather than a map dropped between two panels.</summary>
        private static GameObject mapFrame;
        private const string MapFrameName = "NOAvionics.MapFrame";

        /// <summary>
        /// Bezel buttons the rail declined, hidden while it owns the layout.
        ///
        /// The stock bezel has six slots a side with about three filled; a spare is a live
        /// button labelled "-" that does nothing. Leaving them behind put three dark empty
        /// boxes across the map where the old columns used to be. Vanilla re-activates them
        /// on every maximise, so this runs after that and hides them again.
        /// </summary>
        private static readonly List<GameObject> spares = new List<GameObject>();
        private static MapSnapshot map;
        private static bool applied;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
        public static void MaximizePostfix(DynamicMap __instance)
        {
            if (__instance == null) return;

            if (!Plugin.Settings.FitMapToPanels.Value || !GameAccess.MfdAvailable)
            {
                // Restoring here happens with the map still maximised, so the map rect does
                // have to be put back — unlike on Minimize, where vanilla has already done it.
                Restore(onMinimize: false);
                return;
            }

            Canvas canvas = __instance.maximizedMapCanvas;
            if (!MfdLayout.TryResolve(canvas, out MfdLayout.Columns columns)) return;

            ResizeMap(__instance, columns);
            BuildRail(canvas, columns);
            DockPanels(canvas, columns);

            applied = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
        public static void MinimizePostfix(DynamicMap __instance) => Restore(onMinimize: true, dynamicMap: __instance);

        // ------------------------------------------------------------------------- map

        /// <summary>
        /// Move the map into the centre column.
        ///
        /// Only the *viewport* changes. The map root carries a <c>RectMask2D</c> over a much
        /// larger content image, so resizing its rect shows more map at the same scale rather
        /// than scaling what is there. That distinction matters: <c>mapScaleMaximized</c> is
        /// public and looks like the obvious lever, but the value 900 is also written as a
        /// literal inside <c>GetCursorCoordinates</c> and <c>LoadMapImage</c>. Changing the
        /// field moves <c>mapDisplayFactor</c> — and therefore every icon — while those two
        /// literals stay put, so icons and right-click waypoints would land at the wrong
        /// world coordinates. Changing the rect leaves every coordinate path consistent.
        ///
        /// Cursor hit-testing and pan clamping both read the background rect, so they follow
        /// this for free.
        /// </summary>
        private static void ResizeMap(DynamicMap dynamicMap, MfdLayout.Columns columns)
        {
            // The private mapRectTransform is this component's own RectTransform, and the
            // private backgroundRectTransform is the public mapBackground's — so the whole
            // relayout needs no reflection.
            var root = dynamicMap.GetComponent<RectTransform>();
            RectTransform background = dynamicMap.mapBackground != null
                ? dynamicMap.mapBackground.rectTransform
                : null;
            if (root == null) return;

            if (map == null)
            {
                map = new MapSnapshot
                {
                    Root = root,
                    RootSize = root.sizeDelta,
                    RootAnchoredPosition = root.anchoredPosition,
                    RootAnchorMin = root.anchorMin,
                    RootAnchorMax = root.anchorMax,
                    RootPivot = root.pivot,
                    Background = background,
                    BackgroundSize = background != null ? background.sizeDelta : Vector2.zero,
                    BackgroundImage = dynamicMap.mapBackground,
                    BackgroundSprite = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.sprite : null,
                    BackgroundType = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.type : Image.Type.Simple,
                    BackgroundColor = dynamicMap.mapBackground != null ? dynamicMap.mapBackground.color : Color.white,
                };
            }

            var size = new Vector2(columns.Map.width, columns.Map.height);

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            root.anchoredPosition = MfdLayout.CentreOf(columns.Map);

            if (background != null)
            {
                background.sizeDelta = size + new Vector2(BackgroundBleed, BackgroundBleed);

                // The map's own ground is a flat blue panel, which is the single largest
                // thing on screen still painted in a different language from the panels
                // beside it. Give it the same chamfered bezel and green-glass ground so the
                // three columns read as one instrument rather than a mod either side of a
                // stock screen. The terrain and icons ride above this and are untouched.
                Image ground = dynamicMap.mapBackground;
                ground.sprite = AvSprites.Panel;
                ground.type = Image.Type.Sliced;
                ground.color = AvTheme.Unity(AvTokens.Ground.WithAlpha(0.90f));
            }

            // Nothing but ClampToMapEdge reads mapScaleCurrent, and it uses it as a radius.
            // The shorter side is the honest answer for a non-square viewport.
            dynamicMap.mapScaleCurrent = Mathf.Min(size.x, size.y);

            EnsureMapFrame(dynamicMap.maximizedMapCanvas, columns);
        }

        /// <summary>
        /// Draw (or move) the map viewport's frame: a 1px hairline outline and four corner
        /// ticks, matching the panel column's own frame. A canvas child rather than a child
        /// of the map root, whose <c>RectMask2D</c> would clip it.
        /// </summary>
        private static void EnsureMapFrame(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;

            if (mapFrame == null)
            {
                Transform existing = canvas.transform.Find(MapFrameName);
                mapFrame = existing != null ? existing.gameObject : null;
            }

            if (mapFrame == null)
            {
                mapFrame = new GameObject(MapFrameName, typeof(RectTransform));
                mapFrame.GetComponent<RectTransform>().SetParent(canvas.transform, worldPositionStays: false);
            }
            else
            {
                for (int i = mapFrame.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(mapFrame.transform.GetChild(i).gameObject);
            }

            var rt = mapFrame.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(columns.Map.width, columns.Map.height);
            rt.anchoredPosition = MfdLayout.TopLeftOf(columns.Map);
            rt.localScale = Vector3.one;

            var area = new Rect(0f, 0f, columns.Map.width, columns.Map.height);
            AvKit.Outline(rt, area, AvTheme.Hairline);
            AvKit.CornerTicks(rt, area, AvTheme.Hairline);

            rt.SetAsLastSibling();
        }

        // ------------------------------------------------------------------------ rail

        private static void BuildRail(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = canvas.GetComponentInChildren<VirtualMFD>(true) ?? Object.FindObjectOfType<VirtualMFD>();
            if (mfd == null) return;

            MfdRail.Ensure(canvas, columns);

            List<Button> left = GameAccess.GetLeftButtons(mfd);
            List<Button> right = GameAccess.GetRightButtons(mfd);

            if (buttons.Count == 0)
            {
                Snapshot(left);
                Snapshot(right);
            }

            // Both vanilla columns become one rail. The stock parents carry a
            // VerticalLayoutGroup that would overwrite any position written to a button while
            // it is still inside them, so reparenting out is what makes placement stick.
            var skins = new List<MfdRail.ButtonSkin>();
            MfdRail.Adopt(left, GameAccess.GetLeftScreens(mfd), skins);
            MfdRail.Adopt(right, GameAccess.GetRightScreens(mfd), skins);

            AttachSkins(skins);
            HideSpares(left, GameAccess.GetLeftScreens(mfd));
            HideSpares(right, GameAccess.GetRightScreens(mfd));
            HideEmptiedContainers();
        }

        private static void Snapshot(List<Button> source)
        {
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                Button button = source[i];
                if (button == null) continue;

                Transform t = button.transform;
                buttons.Add(new ButtonSnapshot
                {
                    Button = button,
                    Parent = t.parent,
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    LocalScale = t.localScale,
                });
            }
        }

        /// <summary>Hide every bezel button that has no screen behind it.</summary>
        private static void HideSpares(List<Button> source, List<MFDScreen> screens)
        {
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                Button button = source[i];
                if (button == null) continue;

                bool drivesAScreen = screens != null && i < screens.Count && screens[i] != null;
                if (drivesAScreen) continue;
                if (!button.gameObject.activeSelf) continue;

                if (!spares.Contains(button.gameObject)) spares.Add(button.gameObject);
                button.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Hide the backdrop of any bezel container the rail has emptied.
        ///
        /// Only containers that actually lost every button are hidden, so a column the rail
        /// declined to adopt from keeps its frame.
        /// </summary>
        private static void HideEmptiedContainers()
        {
            if (containers.Count > 0) return;

            for (int i = 0; i < buttons.Count; i++)
            {
                Transform parent = buttons[i].Parent;
                if (parent == null) continue;
                if (StillHoldsAButton(parent)) continue;

                Image backdrop = parent.GetComponent<Image>();
                if (backdrop == null || !backdrop.enabled) continue;
                if (AlreadyTracked(backdrop)) continue;

                containers.Add(new ContainerSkin { Backdrop = backdrop, WasEnabled = true });
                backdrop.enabled = false;
            }
        }

        private static bool StillHoldsAButton(Transform container)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);

                // Spares left in place but hidden do not count: a container holding only
                // those is empty as far as anything on screen is concerned.
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Button>() != null) return true;
            }
            return false;
        }

        private static bool AlreadyTracked(Image backdrop)
        {
            for (int i = 0; i < containers.Count; i++)
                if (containers[i].Backdrop == backdrop) return true;
            return false;
        }

        /// <summary>Pair each restyled button with the snapshot that has to undo it.</summary>
        private static void AttachSkins(List<MfdRail.ButtonSkin> skins)
        {
            for (int i = 0; i < skins.Count; i++)
            {
                MfdRail.ButtonSkin skin = skins[i];
                if (skin == null || skin.Background == null) continue;

                for (int j = 0; j < buttons.Count; j++)
                {
                    if (buttons[j].Button != null &&
                        buttons[j].Button.gameObject == skin.Background.gameObject)
                    {
                        buttons[j].Skin = skin;
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------------- panels

        private static void DockPanels(Canvas canvas, MfdLayout.Columns columns)
        {
            VirtualMFD mfd = canvas.GetComponentInChildren<VirtualMFD>(true) ?? Object.FindObjectOfType<VirtualMFD>();
            if (mfd == null) return;

            MfdPanelDock.Ensure(canvas, columns);
            MfdPanelDock.DockModScreens(mfd);
        }

        /// <summary>
        /// Re-resolve grid and update map, rail and panel dock when the panel column widens.
        /// </summary>
        public static void ReLayout(float panelWidth)
        {
            if (!applied || map == null || map.Root == null) return;

            Canvas canvas = map.Root.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (!MfdLayout.TryResolve(canvas, out MfdLayout.Columns columns, panelWidth)) return;

            var dynamicMap = map.Root.GetComponent<DynamicMap>();
            if (dynamicMap != null) ResizeMap(dynamicMap, columns);

            MfdRail.Ensure(canvas, columns);
            MfdPanelDock.Ensure(canvas, columns);
        }

        // --------------------------------------------------------------------- restore

        private static void Restore(bool onMinimize = false, DynamicMap dynamicMap = null)
        {
            if (!applied) return;

            // Backdrops first: the buttons go back into these containers next, and a hidden
            // frame around restored buttons would be its own artifact.
            for (int i = 0; i < containers.Count; i++) containers[i].Restore();
            containers.Clear();

            // Re-activate spare buttons ONLY on the settings-off path while still maximized.
            // On Minimize, vanilla's VirtualMFD_onMapMinimized has already hidden all bezel
            // buttons; unhiding spares here would leave stray '-' buttons floating in the cockpit.
            if (!onMinimize)
            {
                for (int i = 0; i < spares.Count; i++)
                    if (spares[i] != null) spares[i].SetActive(true);
            }
            spares.Clear();

            for (int i = 0; i < buttons.Count; i++) buttons[i].Restore();
            buttons.Clear();

            if (mapFrame != null) { Object.Destroy(mapFrame); mapFrame = null; }

            MfdPanelDock.Restore();

            // On Minimize, restore framing and minimap state cleanly back to the HUD anchor
            // without canopy projection or stretched offsets.
            // On the settings-off path (still maximized), Restore() puts everything back.
            if (onMinimize)
            {
                map?.RestoreFraming(dynamicMap);
            }
            else
            {
                map?.Restore();
            }
            map = null;

            applied = false;
        }

        /// <summary>Forget captured state at end of mission so the next scene re-snapshots.</summary>
        public static void Reset()
        {
            buttons.Clear();
            containers.Clear();
            spares.Clear();
            if (mapFrame != null) { Object.Destroy(mapFrame); mapFrame = null; }
            map = null;
            applied = false;
            MfdRail.Reset();
            MfdPanelDock.Reset();
        }
    }
}
