using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Owned presentation hosts for the game's six stock map-MFD pages.
    ///
    /// The game controls an <see cref="MFDScreen"/> by toggling only its
    /// <c>displayPanel</c>. Repainting children in that panel made this feature depend on
    /// every stock prefab's layout groups, delayed instantiation, and naming conventions.
    /// Instead, each host keeps the native screen and controller alive, hides the old view
    /// behind a non-interactive <see cref="CanvasGroup"/>, and swaps displayPanel to a
    /// fixed, fully owned avionics surface. Native state and actions therefore remain the
    /// authority, while no native layout participates in the rendered panel.
    /// </summary>
    internal static class VanillaMfdRebuild
    {
        private const float RefreshInterval = 0.15f;

        private static readonly Dictionary<MFDScreen, Binding> bindings =
            new Dictionary<MFDScreen, Binding>();

        /// <summary>
        /// Attach an owned surface when this is one of the six controller-backed stock
        /// pages. Unknown and third-party screens are deliberately left untouched.
        /// </summary>
        public static bool TryApply(MFDScreen screen)
        {
            if (screen == null || bindings.ContainsKey(screen)) return false;
            if (screen.displayPanel == null) return false;

            Presenter presenter = CreatePresenter(screen);
            if (presenter == null) return false;

            Binding binding = null;
            try
            {
                TMP_Text sourceText = screen.GetComponentInChildren<TMP_Text>(true);
                if (sourceText != null && sourceText.font != null) AvFont.Font = sourceText.font;

                binding = new Binding(screen);
                RectTransform view = BuildViewRoot(screen.transform, presenter.Id);
                // Attach before building so the rollback path owns (and can destroy) the
                // partial tree if a stylesheet or game API changes mid-construction.
                binding.Attach(view.gameObject, presenter);
                presenter.Build(view);
                bindings.Add(screen, binding);
                presenter.Refresh(force: true);
                return true;
            }
            catch (Exception e)
            {
                binding?.Restore();
                Plugin.Logger.LogWarning("MFD replacement " + screen.name + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Refresh the newly visible surface after VirtualMFD has set isActive.</summary>
        public static void OnShown(MFDScreen screen)
        {
            if (screen != null && bindings.TryGetValue(screen, out Binding binding))
                binding.Presenter.Refresh(force: true);
        }

        /// <summary>Visible-only refresh; no layout rebuilds or prefab traversal.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            foreach (Binding binding in bindings.Values)
            {
                if (binding.Screen == null || !binding.Screen.isActive) continue;
                binding.Presenter.Refresh(force: false, now);
            }
        }

        public static bool IsHosted(MFDScreen screen) =>
            screen != null && bindings.ContainsKey(screen);

        /// <summary>Restore every native screen exactly before its dock is dismantled.</summary>
        public static void Restore()
        {
            foreach (Binding binding in bindings.Values) binding.Restore();
            bindings.Clear();
        }

        /// <summary>Scene cleanup has the same restoration contract as a settings toggle.</summary>
        public static void Reset() => Restore();

        private static Presenter CreatePresenter(MFDScreen screen)
        {
            // Controller type is the contract. The faction short names are changed by the
            // game after mission setup, so they are not a safe primary key.
            InfoPanel_Faction faction = screen.GetComponent<InfoPanel_Faction>();
            if (faction != null)
            {
                VanillaMfdPanelId id = faction.selectFaction == InfoPanel_Faction.SelectFaction.Other
                    ? VanillaMfdPanelId.Pala
                    : VanillaMfdPanelId.Bdf;
                return new FactionPresenter(screen, faction, id);
            }

            MapOptions map = screen.GetComponent<MapOptions>();
            if (map != null) return new MapPresenter(screen, map);

            HUDOptions hud = screen.GetComponent<HUDOptions>();
            if (hud != null) return new HudPresenter(screen, hud);

            TargetListSelector target = screen.GetComponent<TargetListSelector>();
            if (target != null) return new TargetPresenter(screen, target);

            ObjectiveInfoList mission = screen.GetComponent<ObjectiveInfoList>();
            if (mission != null) return new MissionPresenter(screen);

            // A game update may leave the stable bezel label but move the controller. Keep
            // the problem actionable instead of blanking the screen or mutating unknown UI.
            VanillaMfdPanelId fallback = VanillaMfdPanelCatalog.FromShortName(screen.shortName);
            return fallback == VanillaMfdPanelId.Unknown ? null : new UnavailablePresenter(screen, fallback);
        }

        private static RectTransform BuildViewRoot(Transform parent, VanillaMfdPanelId id)
        {
            var go = new GameObject("NOAvionics." + VanillaMfdPanelCatalog.Label(id),
                                    typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(AvTokens.PanelWidth, AvTokens.PanelHeight);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            Image background = go.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;
            return rt;
        }

        // ---------------------------------------------------------------- lifecycle

        private sealed class Binding
        {
            private readonly GameObject originalDisplay;
            private readonly CanvasGroup originalGroup;
            private readonly bool addedGroup;
            private readonly float groupAlpha;
            private readonly bool groupInteractable;
            private readonly bool groupBlocksRaycasts;
            private readonly bool groupIgnoreParentGroups;
            private readonly Image rootImage;
            private readonly Color rootImageColor;
            private readonly bool rootImageRaycast;
            private readonly List<BehaviourState> layoutDrivers = new List<BehaviourState>();
            private readonly List<GraphicState> externalGraphics = new List<GraphicState>();
            private readonly List<SelectableState> externalSelectables = new List<SelectableState>();

            public readonly MFDScreen Screen;
            public GameObject View;
            public Presenter Presenter;

            public Binding(MFDScreen screen)
            {
                Screen = screen;
                originalDisplay = screen.displayPanel;

                originalGroup = originalDisplay.GetComponent<CanvasGroup>();
                if (originalGroup == null)
                {
                    originalGroup = originalDisplay.AddComponent<CanvasGroup>();
                    addedGroup = true;
                }
                else
                {
                    groupAlpha = originalGroup.alpha;
                    groupInteractable = originalGroup.interactable;
                    groupBlocksRaycasts = originalGroup.blocksRaycasts;
                    groupIgnoreParentGroups = originalGroup.ignoreParentGroups;
                }

                // The source remains active. Native controller components can keep their
                // lists and text fields current, but it can neither draw nor eat map input.
                originalDisplay.SetActive(true);
                originalGroup.alpha = 0f;
                originalGroup.interactable = false;
                originalGroup.blocksRaycasts = false;

                rootImage = screen.GetComponent<Image>();
                if (rootImage != null)
                {
                    rootImageColor = rootImage.color;
                    rootImageRaycast = rootImage.raycastTarget;
                    // MfdScreenChromePatch later enables this Graphic on ShowScreen, so it
                    // must be transparent rather than merely disabled.
                    rootImage.color = Color.clear;
                    rootImage.raycastTarget = false;
                }

                CaptureAndDisable(screen.GetComponents<ContentSizeFitter>());
                CaptureAndDisable(screen.GetComponents<AspectRatioFitter>());
                // A future stock prefab may replace the faction screen's fitter with a
                // layout group. Neither is allowed to reposition the direct owned host.
                CaptureAndDisable(screen.GetComponents<LayoutGroup>());
                CaptureAndMuteExternalChrome(screen);
            }

            public void Attach(GameObject view, Presenter presenter)
            {
                View = view;
                Presenter = presenter;
                Screen.displayPanel = view;
                view.SetActive(Screen.isActive);
            }

            public void Restore()
            {
                if (Screen != null)
                {
                    // Put the pointer back before destroying the owned object: a late stock
                    // CloseScreen/ShowScreen callback must always see a live native panel.
                    if (Screen.displayPanel == View) Screen.displayPanel = originalDisplay;

                    if (rootImage != null)
                    {
                        rootImage.color = rootImageColor;
                        rootImage.raycastTarget = rootImageRaycast;
                        // Match the live screen state, not the prefab snapshot. A closed
                        // screen can be restored before its final CloseScreen callback; using
                        // the original enabled flag here would flash or strand its border.
                        // MfdScreenChromePatch re-enables it on the next ShowScreen call.
                        rootImage.enabled = Screen.isActive;
                    }

                    for (int i = 0; i < layoutDrivers.Count; i++) layoutDrivers[i].Restore();
                    for (int i = 0; i < externalGraphics.Count; i++) externalGraphics[i].Restore();
                    for (int i = 0; i < externalSelectables.Count; i++) externalSelectables[i].Restore();

                    if (originalGroup != null)
                    {
                        if (addedGroup)
                        {
                            UnityEngine.Object.Destroy(originalGroup);
                        }
                        else
                        {
                            originalGroup.alpha = groupAlpha;
                            originalGroup.interactable = groupInteractable;
                            originalGroup.blocksRaycasts = groupBlocksRaycasts;
                            originalGroup.ignoreParentGroups = groupIgnoreParentGroups;
                        }
                    }

                    if (originalDisplay != null) originalDisplay.SetActive(Screen.isActive);
                }

                if (View != null) UnityEngine.Object.Destroy(View);
                View = null;
            }

            private void CaptureAndDisable(Behaviour[] drivers)
            {
                if (drivers == null) return;
                for (int i = 0; i < drivers.Length; i++)
                {
                    Behaviour driver = drivers[i];
                    if (driver == null || !driver.enabled) continue;
                    layoutDrivers.Add(new BehaviourState(driver));
                    driver.enabled = false;
                }
            }

            private void CaptureAndMuteExternalChrome(MFDScreen screen)
            {
                Transform source = originalDisplay == null ? null : originalDisplay.transform;
                if (source == null) return;

                Graphic[] graphics = screen.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null || graphic == rootImage || graphic.transform.IsChildOf(source)) continue;
                    externalGraphics.Add(new GraphicState(graphic));
                    graphic.color = Color.clear;
                    graphic.raycastTarget = false;
                }

                Selectable[] selectables = screen.GetComponentsInChildren<Selectable>(true);
                for (int i = 0; i < selectables.Length; i++)
                {
                    Selectable selectable = selectables[i];
                    if (selectable == null || selectable.transform.IsChildOf(source)) continue;
                    externalSelectables.Add(new SelectableState(selectable));
                    selectable.interactable = false;
                }
            }
        }

        private sealed class BehaviourState
        {
            private readonly Behaviour behaviour;
            private readonly bool enabled;

            public BehaviourState(Behaviour behaviour)
            {
                this.behaviour = behaviour;
                enabled = behaviour.enabled;
            }

            public void Restore()
            {
                if (behaviour != null) behaviour.enabled = enabled;
            }
        }

        private sealed class GraphicState
        {
            private readonly Graphic graphic;
            private readonly Color color;
            private readonly bool raycastTarget;

            public GraphicState(Graphic graphic)
            {
                this.graphic = graphic;
                color = graphic.color;
                raycastTarget = graphic.raycastTarget;
            }

            public void Restore()
            {
                if (graphic == null) return;
                graphic.color = color;
                graphic.raycastTarget = raycastTarget;
            }
        }

        private sealed class SelectableState
        {
            private readonly Selectable selectable;
            private readonly bool interactable;

            public SelectableState(Selectable selectable)
            {
                this.selectable = selectable;
                interactable = selectable.interactable;
            }

            public void Restore()
            {
                if (selectable != null) selectable.interactable = interactable;
            }
        }

        // ------------------------------------------------------------------- shell

        private sealed class MfdShell
        {
            private const float Height = AvTokens.PanelHeight;

            private readonly AvButton[] tabs;

            public readonly RectTransform Content;
            public readonly AvStyled.DataBar DataBar;
            public readonly TMP_Text Status;
            public readonly Rect Body;

            public MfdShell(RectTransform root, VanillaMfdPanelId id, int tabCount)
            {
                var contentObject = new GameObject("Content", typeof(RectTransform));
                Content = contentObject.GetComponent<RectTransform>();
                Content.SetParent(root, worldPositionStays: false);
                AvKit.Stretch(Content);

                float width = AvTokens.PanelWidth;
                float inner = width - AvTokens.Pad * 2f;
                var bar = new Rect(AvTokens.Pad, -AvTokens.Pad, inner, AvTokens.TitleBarHeight + 2f);
                DataBar = AvStyled.TopBar(Content, bar, VanillaMfdPanelCatalog.Label(id), 3);

                float y = bar.y - bar.height - AvTokens.Space2;
                if (tabCount > 0)
                {
                    tabs = new AvButton[tabCount];
                    float tabWidth = (inner - AvTokens.Gap * (tabCount - 1)) / tabCount;
                    for (int i = 0; i < tabCount; i++)
                    {
                        int index = i;
                        tabs[i] = AvStyled.Button(Content,
                            new Rect(AvTokens.Pad + i * (tabWidth + AvTokens.Gap), y,
                                     tabWidth, AvTokens.TabHeight),
                            "—", "tab", () => OnTabPressed?.Invoke(index), AvButtonStyle.Tab);
                    }
                    y -= AvTokens.TabHeight + AvTokens.Space3;
                }

                float statusY = -(Height - AvTokens.Pad - AvTokens.StatusStripHeight);
                Status = AvStyled.StatusStrip(Content,
                    new Rect(AvTokens.Pad, statusY, inner, AvTokens.StatusStripHeight));
                Body = new Rect(AvTokens.Pad, y, inner, y - (statusY + AvTokens.Space2));

                AvKit.CornerTicks(Content, new Rect(0f, 0f, width, Height), AvTheme.Hairline);
            }

            public Action<int> OnTabPressed { get; set; }

            public RectTransform CreatePage(string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                RectTransform page = go.GetComponent<RectTransform>();
                page.SetParent(Content, worldPositionStays: false);
                AvKit.Place(page, Body);
                return page;
            }

            public void ConfigureTabs(string[] labels, Action<int> onTab)
            {
                OnTabPressed = onTab;
                if (tabs == null || labels == null) return;
                int count = Mathf.Min(tabs.Length, labels.Length);
                for (int i = 0; i < count; i++) tabs[i].SetText(labels[i]);
            }

            public void SetSelectedTab(int selected)
            {
                if (tabs == null) return;
                for (int i = 0; i < tabs.Length; i++) tabs[i].SetLatched(i == selected);
            }
        }

        private abstract class Presenter
        {
            private float nextRefresh;

            protected Presenter(MFDScreen screen, VanillaMfdPanelId id)
            {
                Screen = screen;
                Id = id;
            }

            protected readonly MFDScreen Screen;
            public readonly VanillaMfdPanelId Id;
            protected MfdShell Shell;

            public void Build(RectTransform root)
            {
                Shell = new MfdShell(root, Id, TabCount);
                BuildContent();
            }

            public void Refresh(bool force, float now = 0f)
            {
                if (Screen == null) return;
                if (!force)
                {
                    if (now <= 0f) now = Time.unscaledTime;
                    if (now < nextRefresh) return;
                }

                nextRefresh = Time.unscaledTime + RefreshInterval;
                try
                {
                    RefreshContent();
                    UpdateStatus(AmbientStatus());
                }
                catch (Exception e)
                {
                    Shell.Status.text = "> NATIVE ADAPTER ERROR — " + e.GetType().Name;
                    Plugin.Logger.LogWarning("MFD " + VanillaMfdPanelCatalog.Label(Id) +
                                             " refresh failed: " + e.Message);
                }
            }

            protected virtual int TabCount => 0;
            protected abstract void BuildContent();
            protected abstract void RefreshContent();
            protected abstract string AmbientStatus();

            protected void RequestRefresh()
            {
                nextRefresh = 0f;
                Refresh(force: true);
            }

            protected void UpdateStatus(string ambient)
            {
                string text = AvButton.HoveredTooltip;
                if (string.IsNullOrEmpty(text)) text = MapPicker.Prompt;
                if (string.IsNullOrEmpty(text)) text = ambient;
                Shell.Status.text = "> " + (text ?? "READY");
            }

            protected static void DrawSpine(RectTransform page) =>
                AvStyled.Spine(page, new Rect(0f, 0f, 3f, page.rect.height));

            protected static float Heading(RectTransform parent, float y, float width,
                                           string title, string note = null)
            {
                AvStyled.SpineTick(parent, 3f, y - 7f);
                AvStyled.Label(parent, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 14f),
                               title, "section-title");
                if (!string.IsNullOrEmpty(note))
                {
                    AvStyled.Label(parent,
                        new Rect(width * 0.52f, y, width * 0.48f, 14f), note,
                        "section-title-note", align: TextAlignmentOptions.MidlineRight);
                }
                AvKit.Rule(parent, new Rect(AvTokens.Space3, y - 16f,
                                            width - AvTokens.Space3, 1f), AvTheme.Hairline);
                return y - AvTokens.Space5;
            }
        }

        // --------------------------------------------------------------- MAP panel

        private sealed class MapPresenter : Presenter
        {
            private sealed class ToggleBinding
            {
                public AvButton Button;
                public Func<bool> IsOn;
            }

            private readonly MapOptions options;
            private readonly List<ToggleBinding> toggles = new List<ToggleBinding>();

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map)
            {
                this.options = options;
            }

            protected override void BuildContent()
            {
                RectTransform page = Shell.CreatePage("MapOptions");
                DrawSpine(page);

                float y = -AvTokens.Space1;
                y = Heading(page, y, Shell.Body.width, "MARKERS", "LIVE MAP LAYERS");
                AddGrid(page, ref y, 4,
                    new[] { "OBJECTIVES", "TARGETS", "JAMMING", "LABELS" },
                    new Func<bool>[]
                    {
                        () => options.showObjectives,
                        () => options.showTargetInfo,
                        () => options.showJamming,
                        () => options.showGridLabels,
                    },
                    new Action[]
                    {
                        options.ToggleShowObjectives,
                        options.ToggleShowTargetInfo,
                        options.ToggleShowJamming,
                        options.ToggleShowGridLabels,
                    });

                y = Heading(page, y, Shell.Body.width, "TOOLTIP", "MAP HOVER DETAIL");
                AddGrid(page, ref y, 4,
                    new[] { "HIDE", "INFO", "AMMO", "ORDERS" },
                    new Func<bool>[]
                    {
                        () => options.tooltipType == MapOptions.TooltipType.None,
                        () => options.tooltipType == MapOptions.TooltipType.Info,
                        () => options.tooltipType == MapOptions.TooltipType.Ammo,
                        () => options.tooltipType == MapOptions.TooltipType.Order,
                    },
                    new Action[]
                    {
                        () => options.SetToolTipType((int)MapOptions.TooltipType.None),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Info),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Ammo),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Order),
                    });

                y = Heading(page, y, Shell.Body.width, "ICON SCALE", "TACTICAL SYMBOL SIZE");
                AddGrid(page, ref y, 3,
                    new[] { "SMALL", "MEDIUM", "LARGE" },
                    new Func<bool>[]
                    {
                        () => Mathf.Approximately(options.iconSize, 0.6f),
                        () => Mathf.Approximately(options.iconSize, 0.8f),
                        () => Mathf.Approximately(options.iconSize, 1.0f),
                    },
                    new Action[]
                    {
                        () => options.SetIconSize(0),
                        () => options.SetIconSize(1),
                        () => options.SetIconSize(2),
                    });

                y = Heading(page, y, Shell.Body.width, "SPECIAL ICONS");
                AddGrid(page, ref y, 2,
                    new[] { "PILOTS", "AIRBASES" },
                    new Func<bool>[] { () => options.showPilotIcons, () => options.showAirbaseIcon },
                    new Action[] { options.ToggleShowPilotIcons, options.ToggleShowAirbaseIcons });
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "DISPLAY FILTERS";
                Shell.DataBar.SetChip(0, options.showObjectives ? "OBJ ON" : "OBJ OFF", options.showObjectives);
                Shell.DataBar.SetChip(1, options.showTargetInfo ? "TGT ON" : "TGT OFF", options.showTargetInfo);
                Shell.DataBar.SetChip(2, "SCALE " + IconSizeLabel(), true);
                for (int i = 0; i < toggles.Count; i++)
                    toggles[i].Button.SetLatched(toggles[i].IsOn());
            }

            protected override string AmbientStatus() =>
                "MAP FILTERS — " + (options.showObjectives ? "OBJECTIVES" : "OBJECTIVES HIDDEN");

            private string IconSizeLabel()
            {
                if (options.iconSize < 0.7f) return "S";
                if (options.iconSize < 0.9f) return "M";
                return "L";
            }

            private void AddGrid(RectTransform parent, ref float y, int columns,
                                 string[] labels, Func<bool>[] states, Action[] actions)
            {
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    int row = i / columns;
                    int column = i % columns;
                    var button = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + column * (width + gap),
                                 y - row * (AvTokens.RowHeight + gap), width, AvTokens.RowHeight),
                        labels[i], "toggle", () =>
                        {
                            actions[index]();
                            RequestRefresh();
                        }, AvButtonStyle.Toggle);
                    button.WithTooltip(labels[i] + " MAP OPTION");
                    toggles.Add(new ToggleBinding { Button = button, IsOn = states[i] });
                }
                int rows = Mathf.CeilToInt(labels.Length / (float)columns);
                y -= rows * (AvTokens.RowHeight + gap) + AvTokens.Space3;
            }
        }

        // --------------------------------------------------------------- HUD panel

        private sealed class HudPresenter : Presenter
        {
            private readonly HUDOptions options;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels =
                new Dictionary<HUDOptions_ToggleButton, string>();
            private RectTransform[] pages;
            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private MfdPagingGrid vehicles;
            private MfdPagingGrid buildings;

            public HudPresenter(MFDScreen screen, HUDOptions options)
                : base(screen, VanillaMfdPanelId.Hud)
            {
                this.options = options;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "MODE", "VEHICLES", "BUILDINGS" }, SelectPage);
                pages = new[]
                {
                    Shell.CreatePage("HudModes"),
                    Shell.CreatePage("HudVehicles"),
                    Shell.CreatePage("HudBuildings"),
                };

                BuildModePage(pages[0]);
                DrawSpine(pages[1]);
                float vehicleTop = Heading(pages[1], -AvTokens.Space1, Shell.Body.width,
                                            "VEHICLE PRIORITY", "ONE TYPE AT A TIME");
                vehicles = new MfdPagingGrid(pages[1], vehicleTop, Shell.Body.width, 3, 5);

                DrawSpine(pages[2]);
                float buildingTop = Heading(pages[2], -AvTokens.Space1, Shell.Body.width,
                                             "BUILDING PRIORITY", "ONE TYPE AT A TIME");
                buildings = new MfdPagingGrid(pages[2], buildingTop, Shell.Body.width, 3, 5);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                // These lists are populated by the native component's startup pass. The
                // dock can attach a host one frame sooner, so wait for a complete model
                // rather than logging an error every refresh or freezing a partial view.
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    Shell.DataBar.State.text = "WAITING FOR HUD OPTIONS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                Shell.DataBar.State.text = "HUD PRIORITY MATRIX";
                SetGridInput(true);
                Shell.DataBar.SetChip(0, options.currentMode.ToString(), true);
                Shell.DataBar.SetChip(1, CountEnabled(options.listVehicleTypes) + " VEH", true);
                Shell.DataBar.SetChip(2, CountEnabled(options.listBuildingTypes) + " BLD", true);

                modes.SetData(options.listModes.Count,
                    i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(options.listModes[i], "MODE"),
                    i => options.listModes[i] != null && options.listModes[i].status,
                    SelectMode);
                categories.SetData(options.listCategories.Count,
                    i => NativeCategoryLabel(options.listCategories[i], i),
                    i => options.listCategories[i] != null && options.listCategories[i].maximized,
                    ToggleCategory);
                vehicles.SetData(options.listVehicleTypes.Count,
                    i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                    i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                    SelectVehicle);
                buildings.SetData(options.listBuildingTypes.Count,
                    i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                    i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                    SelectBuilding);
            }

            protected override string AmbientStatus() =>
                "HUD " + options.currentMode + " — SELECT WHAT GETS EMPHASISED";

            private void BuildModePage(RectTransform root)
            {
                DrawSpine(root);
                float y = Heading(root, -AvTokens.Space1, Shell.Body.width,
                                  "ENGAGEMENT MODE", "AUTO HUD PROFILE");
                modes = new MfdPagingGrid(root, y, Shell.Body.width, 3, 2, pager: false);
                y -= AvTokens.RowHeight * 2f + AvTokens.Gap * 2f + AvTokens.Space3;
                y = Heading(root, y, Shell.Body.width, "PRIORITY GATES", "MAXIMISE TRACKS");
                categories = new MfdPagingGrid(root, y, Shell.Body.width, 3, 2, pager: false);
            }

            private void SelectPage(int next)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == next);
                Shell.SetSelectedTab(next);
                RequestRefresh();
            }

            private void SelectMode(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listModes.Count) return;
                HUDOptions_ToggleButton source = options.listModes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                if (index < 6) options.currentMode = (HUDOptions.HUDMode)index;
                Persist();
            }

            private void ToggleCategory(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listCategories.Count) return;
                HUDOptions_Category category = options.listCategories[index];
                if (category == null) return;
                category.Set(!category.maximized);
                Persist();
            }

            private void SelectVehicle(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listVehicleTypes.Count) return;
                HUDOptions_ToggleButton source = options.listVehicleTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void SelectBuilding(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listBuildingTypes.Count) return;
                HUDOptions_ToggleButton source = options.listBuildingTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void Persist()
            {
                options.SaveSettings();
                options.NeedUpdateIcons();
                RequestRefresh();
            }

            private string LabelFor(HUDOptions_ToggleButton source, string fallback)
            {
                if (source == null) return fallback;
                if (!labels.TryGetValue(source, out string label))
                {
                    label = NativeLabel(source, fallback);
                    labels.Add(source, label);
                }
                return label;
            }

            private bool Ready => options != null && options.listModes != null &&
                                  options.listCategories != null &&
                                  options.listVehicleTypes != null &&
                                  options.listBuildingTypes != null;

            private void SetGridInput(bool enabled)
            {
                modes?.SetInteractable(enabled);
                categories?.SetInteractable(enabled);
                vehicles?.SetInteractable(enabled);
                buildings?.SetInteractable(enabled);
            }
        }

        // The faction, target and mission presenters use the same host and paging primitive
        // below; keeping the controller-specific code here makes a future game API change a
        // local adapter edit instead of another prefab traversal.

        // ----------------------------------------------------------- faction panels

        /// <summary>
        /// The left and right faction screens share a single data adapter.  The source
        /// controller tells us whose HQ to read; this avoids treating the mutable bezel
        /// short-name as a gameplay identifier and deliberately does not call the stock
        /// airbase switch (which currently switches to players internally).
        /// </summary>
        private sealed class FactionPresenter : Presenter
        {
            private enum LedgerMode { Reserves, Losses, Value, Manpower }
            private enum InfoMode { Airbases, Players }

            private readonly InfoPanel_Faction source;
            private readonly List<UnitDefinition> definitions = new List<UnitDefinition>();
            private readonly List<string> infoRows = new List<string>();

            private RectTransform[] pages;
            private MfdPagingGrid definitionGrid;
            private MfdPagingGrid infoGrid;
            private AvButton[] definitionTabs;
            private AvButton[] ledgerTabs;
            private AvButton[] infoTabs;
            private TMP_Text factionName;
            private Image factionLogo;
            private TMP_Text[] forceTotals;
            private AvStyled.Metric[] ledgerMetrics;
            private int definitionGroup;
            private bool definitionsLoaded;
            private LedgerMode ledgerMode;
            private InfoMode infoMode;

            public FactionPresenter(MFDScreen screen, InfoPanel_Faction source, VanillaMfdPanelId id)
                : base(screen, id)
            {
                this.source = source;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "FORCES", "LEDGER", "STATUS" }, SelectPage);
                pages = new[]
                {
                    Shell.CreatePage("Forces"),
                    Shell.CreatePage("Ledger"),
                    Shell.CreatePage("Status"),
                };

                BuildForcesPage(pages[0]);
                BuildLedgerPage(pages[1]);
                BuildStatusPage(pages[2]);
                SelectDefinitions(0);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                FactionHQ hq = source == null ? null : source.factionHQ;
                if (hq == null)
                {
                    Shell.DataBar.State.text = "WAITING FOR FACTION HQ";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                string name = hq.faction == null ? "FACTION" : hq.faction.factionName;
                Shell.DataBar.State.text = AvTheme.Truncate((name ?? "FACTION").ToUpperInvariant(), 18);
                Shell.DataBar.SetChip(0, "SCORE " + hq.factionScore.ToString("0.0"), true);
                Shell.DataBar.SetChip(1, UnitConverter.ValueReading(hq.factionFunds), true);
                Shell.DataBar.SetChip(2, "WHD " + hq.GetWarheadStockpile(), true);

                factionName.text = name ?? "FACTION";
                Sprite logo = hq.faction == null ? null : hq.faction.factionColorLogo;
                factionLogo.sprite = logo;
                factionLogo.enabled = logo != null;

                SetForceTotals(hq.missionStatsTracker == null
                    ? default(MissionStatsTracker.TypeStat)
                    : hq.missionStatsTracker.units);
                RefreshDefinitionGrid(hq);
                RefreshLedger(hq);
                RefreshInfo(hq);
                UpdateButtonRows();
            }

            protected override string AmbientStatus()
            {
                return infoMode == InfoMode.Airbases
                    ? "FACTION FORCE INVENTORY — AIRBASE STATUS"
                    : "FACTION FORCE INVENTORY — PLAYER STATUS";
            }

            private void BuildForcesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "FORCE INVENTORY", "LIVE ASSETS");

                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3,
                             68f), AvTheme.RailReady);
                factionLogo = AvKit.Panel(page, new Rect(AvTokens.Space4, y - 8f, 52f, 52f),
                                           Color.white, AvSprites.Control);
                factionLogo.preserveAspect = true;
                factionLogo.raycastTarget = false;
                factionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4 + 64f, y - 8f, Shell.Body.width - 100f, 28f),
                    "SYNCING FACTION", "metric-value");
                AvStyled.Label(page,
                    new Rect(AvTokens.Space4 + 64f, y - 42f, Shell.Body.width - 100f, 12f),
                    "LIVE THEATER ORDER OF BATTLE", "metric-cap");
                y -= 80f;

                forceTotals = new TMP_Text[4];
                string[] labels = { "BLD", "VEH", "SHP", "AIR" };
                float totalWidth = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap * 3f) / 4f;
                for (int i = 0; i < forceTotals.Length; i++)
                {
                    float x = AvTokens.Space3 + i * (totalWidth + AvTokens.Gap);
                    AvStyled.Label(page, new Rect(x, y, totalWidth, 11f), labels[i], "metric-key",
                                   align: TextAlignmentOptions.Center);
                    forceTotals[i] = AvStyled.Label(page, new Rect(x, y - 20f, totalWidth, 20f),
                                                     "—", "row-value",
                                                     align: TextAlignmentOptions.Center);
                }
                y -= 44f;

                y = Heading(page, y, Shell.Body.width, "ASSET CLASS", "TAP TO FILTER");
                definitionTabs = CreateButtonRow(page, y,
                    new[] { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" }, SelectDefinitions);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, Shell.Body.width, "UNIT READOUT", "CURRENT / LOST");
                definitionGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 4);
            }

            private void BuildLedgerPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "THEATER LEDGER", "MISSION ACCOUNTING");
                ledgerTabs = CreateButtonRow(page, y,
                    new[] { "RESERVES", "LOSSES", "VALUE", "MANPOWER" }, SelectLedger);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "ASSET BREAKDOWN", "LIVE TOTALS");
                ledgerMetrics = new AvStyled.Metric[4];
                string[] labels = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
                float gap = AvTokens.Gap;
                float cellWidth = (Shell.Body.width - AvTokens.Space3 - gap) * 0.5f;
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    int row = i / 2;
                    int column = i % 2;
                    ledgerMetrics[i] = AvStyled.MetricCell(page,
                        new Rect(AvTokens.Space3 + column * (cellWidth + gap),
                                 y - row * 92f, cellWidth, 82f), labels[i], "UNIT");
                }
            }

            private void BuildStatusPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "FACTION STATUS", "LIVE THEATER LINK");
                infoTabs = CreateButtonRow(page, y, new[] { "AIRBASES", "PLAYERS" }, SelectInfo);
                y -= AvTokens.RowHeight + AvTokens.Space3;
                y = Heading(page, y, Shell.Body.width, "ACTIVE ENTRIES", "DIRECTORY");
                infoGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 8);
            }

            private AvButton[] CreateButtonRow(RectTransform parent, float y, string[] labels,
                                               Action<int> selected)
            {
                var row = new AvButton[labels.Length];
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * (labels.Length - 1)) /
                              labels.Length;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    row[i] = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + i * (width + gap), y, width, AvTokens.RowHeight),
                        labels[i], "toggle", () => selected(index), AvButtonStyle.Toggle);
                }
                return row;
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SelectDefinitions(int selected)
            {
                definitionGroup = Mathf.Clamp(selected, 0, 3);
                PopulateDefinitions();
                RequestRefresh();
            }

            private void SelectLedger(int selected)
            {
                ledgerMode = (LedgerMode)Mathf.Clamp(selected, 0, 3);
                RequestRefresh();
            }

            private void SelectInfo(int selected)
            {
                // Keep this local. InfoPanel_Faction.SetDisplayAirbases currently selects
                // Players in stock code, exactly the bug this owned view avoids inheriting.
                infoMode = selected == 0 ? InfoMode.Airbases : InfoMode.Players;
                infoGrid.ResetPage();
                RequestRefresh();
            }

            private void PopulateDefinitions()
            {
                definitions.Clear();
                definitionsLoaded = false;
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (encyclopedia == null) return;

                switch (definitionGroup)
                {
                    case 0:
                        if (encyclopedia.buildings == null) return;
                        for (int i = 0; i < encyclopedia.buildings.Count; i++)
                            definitions.Add(encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles == null) return;
                        for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                            definitions.Add(encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships == null) return;
                        for (int i = 0; i < encyclopedia.ships.Count; i++)
                            definitions.Add(encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft == null) return;
                        for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                            definitions.Add(encyclopedia.aircraft[i]);
                        break;
                }
                definitionsLoaded = true;
            }

            private void SetForceTotals(MissionStatsTracker.TypeStat stats)
            {
                if (forceTotals == null) return;
                for (int i = 0; i < forceTotals.Length; i++) forceTotals[i].text = "—";
                forceTotals[0].text = stats.buildings.current.ToString("0");
                forceTotals[1].text = stats.vehicles.current.ToString("0");
                forceTotals[2].text = stats.ships.current.ToString("0");
                forceTotals[3].text = stats.aircraft.current.ToString("0");
            }

            private void RefreshDefinitionGrid(FactionHQ hq)
            {
                if (!definitionsLoaded) PopulateDefinitions();
                definitionGrid.SetData(definitions.Count,
                    i => DefinitionLabel(definitions[i], hq),
                    i => false,
                    _ => { });
            }

            private string DefinitionLabel(UnitDefinition definition, FactionHQ hq)
            {
                if (definition == null) return "UNKNOWN";
                int current = hq.missionStatsTracker == null ? 0 :
                    hq.missionStatsTracker.GetCurrentUnits(definition);
                int lost = hq.missionStatsTracker == null ? 0 :
                    hq.missionStatsTracker.GetLostUnits(definition);
                string code = string.IsNullOrEmpty(definition.code) ? definition.unitName : definition.code;
                return AvTheme.Truncate((code ?? "UNIT").ToUpperInvariant(), 11) + " " +
                       current + "/" + lost;
            }

            private void RefreshLedger(FactionHQ hq)
            {
                if (ledgerMetrics == null || hq.missionStatsTracker == null) return;
                MissionStatsTracker.TypeStat category = LedgerCategory(hq.missionStatsTracker);
                MissionStatsTracker.Stat[] values =
                {
                    category.buildings, category.vehicles, category.ships, category.aircraft,
                };
                string unit = ledgerMode == LedgerMode.Value ? "CR" :
                              ledgerMode == LedgerMode.Manpower ? "PAX" : "UNIT";
                string caption = ledgerMode.ToString().ToUpperInvariant();
                for (int i = 0; i < ledgerMetrics.Length; i++)
                {
                    MissionStatsTracker.Stat stat = values[i];
                    float value = ledgerMode == LedgerMode.Reserves ? ReserveCount(hq, i) :
                                  LedgerValue(stat);
                    float denominator = ledgerMode == LedgerMode.Reserves
                        ? Mathf.Max(1f, value + stat.current)
                        : Mathf.Max(1f, stat.total);
                    float fraction = ledgerMode == LedgerMode.Losses ? stat.lost / denominator :
                                     value / denominator;
                    ledgerMetrics[i].Unit.text = unit;
                    ledgerMetrics[i].Set(FormatLedger(value), caption, fraction,
                                         ledgerMode == LedgerMode.Losses ? AvTheme.Warning : AvTheme.Accent);
                }
            }

            private MissionStatsTracker.TypeStat LedgerCategory(MissionStatsTracker tracker)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses:
                    case LedgerMode.Reserves:
                        return tracker.units;
                    case LedgerMode.Value:
                        return tracker.value;
                    default:
                        return tracker.manpower;
                }
            }

            private float LedgerValue(MissionStatsTracker.Stat stat)
            {
                switch (ledgerMode)
                {
                    case LedgerMode.Losses: return stat.lost;
                    case LedgerMode.Value: return stat.current;
                    case LedgerMode.Manpower: return stat.current;
                    default: return stat.current;
                }
            }

            private string FormatLedger(float value)
            {
                return ledgerMode == LedgerMode.Value
                    ? UnitConverter.ValueReading(value)
                    : value.ToString("0");
            }

            private static int ReserveCount(FactionHQ hq, int group)
            {
                Encyclopedia encyclopedia = Encyclopedia.i;
                if (hq == null || encyclopedia == null) return 0;

                int total = 0;
                switch (group)
                {
                    case 0:
                        if (encyclopedia.buildings != null)
                            for (int i = 0; i < encyclopedia.buildings.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.buildings[i]);
                        break;
                    case 1:
                        if (encyclopedia.vehicles != null)
                            for (int i = 0; i < encyclopedia.vehicles.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.vehicles[i]);
                        break;
                    case 2:
                        if (encyclopedia.ships != null)
                            for (int i = 0; i < encyclopedia.ships.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.ships[i]);
                        break;
                    default:
                        if (encyclopedia.aircraft != null)
                            for (int i = 0; i < encyclopedia.aircraft.Count; i++)
                                total += SupportedSupply(hq, encyclopedia.aircraft[i]);
                        break;
                }
                return total;
            }

            private static int SupportedSupply(FactionHQ hq, UnitDefinition definition)
            {
                // FactionHQ stores reserve supply only for mobile definitions. The
                // encyclopedia also exposes buildings through UnitDefinition, but passing
                // one to GetUnitSupply throws in current game builds. Keep the adapter
                // tolerant of mixed/future category lists by filtering on the API contract.
                if (hq == null || definition == null) return 0;
                if (!(definition is AircraftDefinition) && !(definition is VehicleDefinition)) return 0;
                return Mathf.Max(0, hq.GetUnitSupply(definition));
            }

            private void RefreshInfo(FactionHQ hq)
            {
                infoRows.Clear();
                if (infoMode == InfoMode.Airbases)
                {
                    foreach (Airbase airbase in hq.GetAirbases())
                    {
                        if (airbase != null) infoRows.Add("AIRBASE  " + airbase.name.ToUpperInvariant());
                    }
                }
                else
                {
                    foreach (var player in hq.GetPlayers(sortByScore: false))
                    {
                        if (player != null) infoRows.Add("PLAYER   " + player);
                    }
                }

                if (infoRows.Count == 0)
                    infoRows.Add(infoMode == InfoMode.Airbases ? "NO ACTIVE AIRBASES" : "NO ACTIVE PLAYERS");
                infoGrid.SetData(infoRows.Count, i => infoRows[i], i => false, _ => { });
            }

            private void UpdateButtonRows()
            {
                SetRow(definitionTabs, definitionGroup);
                SetRow(ledgerTabs, (int)ledgerMode);
                SetRow(infoTabs, infoMode == InfoMode.Airbases ? 0 : 1);
            }

            private static void SetRow(AvButton[] buttons, int selected)
            {
                if (buttons == null) return;
                for (int i = 0; i < buttons.Length; i++) buttons[i].SetLatched(i == selected);
            }
        }

        // ------------------------------------------------------------ target panel

        private sealed class TargetPresenter : Presenter
        {
            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();

            private RectTransform[] pages;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private MfdPagingGrid selectedGrid;
            private AvButton resetFilters;
            private AvButton clearTargets;
            private AvButton followHud;
            private AvButton laser;

            public TargetPresenter(MFDScreen screen, TargetListSelector selector)
                : base(screen, VanillaMfdPanelId.Tgt)
            {
                this.selector = selector;
            }

            protected override int TabCount => 2;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "FILTERS", "SELECTED" }, SelectPage);
                pages = new[] { Shell.CreatePage("Filters"), Shell.CreatePage("Selected") };
                BuildFiltersPage(pages[0]);
                BuildSelectedPage(pages[1]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                if (!Ready)
                {
                    SetFilterInput(false);
                    Shell.DataBar.State.text = "WAITING FOR TARGET FILTERS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                SetFilterInput(true);
                int filters = CountEnabled(selector.toggleFactionItems) +
                              CountEnabled(selector.toggleUnitTypesItems) +
                              CountEnabled(selector.toggleVehicleTypesItems);
                Shell.DataBar.State.text = "TARGET ACQUISITION";
                Shell.DataBar.SetChip(0, filters + " FILTERS", filters > 0);
                Shell.DataBar.SetChip(1, selector.toggleFollowHUD.status ? "HUD LINK" : "MANUAL", true);
                Shell.DataBar.SetChip(2, selector.toggleLaser.status ? "LASER" : "NO LASER",
                                      selector.toggleLaser.status);

                resetFilters.SetLatched(filters == 0);
                clearTargets.SetEnabled(SelectedCount() > 0);
                followHud.SetLatched(selector.toggleFollowHUD.status);
                laser.SetLatched(selector.toggleLaser.status);

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
            }

            protected override string AmbientStatus() =>
                "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";

            private void BuildFiltersPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "TARGET FILTER", "LIVE MAP ACQUISITION");
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * 3f) / 4f;
                resetFilters = AvStyled.Button(page, new Rect(AvTokens.Space3, y, width, AvTokens.RowHeight),
                    "RESET", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                clearTargets = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + (width + gap), y, width, AvTokens.RowHeight),
                    "CLEAR", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                followHud = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + 2f * (width + gap), y, width, AvTokens.RowHeight),
                    "HUD LINK", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleFollowHUD.Toggle();
                        selector.OnToggleFollowHUD();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    },
                    AvButtonStyle.Toggle);
                laser = AvStyled.Button(page,
                    new Rect(AvTokens.Space3 + 3f * (width + gap), y, width, AvTokens.RowHeight),
                    "LASER", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "FACTION", "FRIEND / HOSTILE");
                factionGrid = new MfdPagingGrid(page, y, Shell.Body.width, 2, 1, pager: false);
                AddRightClickActions(factionGrid, OnlyFaction);
                y -= AvTokens.RowHeight + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "UNIT CLASS", "AIR / GROUND / SEA");
                unitGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 2, pager: false);
                AddRightClickActions(unitGrid, OnlyUnitType);
                y -= AvTokens.RowHeight * 2f + AvTokens.Gap + AvTokens.Space3;

                y = Heading(page, y, Shell.Body.width, "PLATFORM TYPE", "DETAILED FILTER");
                vehicleGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 4);
                AddRightClickActions(vehicleGrid, OnlyVehicleType);
            }

            private void BuildSelectedPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "SELECTED TARGETS", "MAP ICONS");
                selectedGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9);
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick);
            }

            private void AddRightClickActions(MfdPagingGrid grid, Action<int> onOnly)
            {
                for (int i = 0; i < 12; i++)
                {
                    int slot = i;
                    AvButton button = grid.ButtonAt(i);
                    if (button == null) continue;
                    MfdRightClickAction action = button.gameObject.AddComponent<MfdRightClickAction>();
                    action.Configure(() => onOnly(grid.CurrentIndex(slot)));
                }
            }

            private void ToggleFaction(int index) => Toggle(selector.toggleFactionItems, index);
            private void ToggleUnitType(int index) => Toggle(selector.toggleUnitTypesItems, index);
            private void ToggleVehicleType(int index) => Toggle(selector.toggleVehicleTypesItems, index);

            private void OnlyFaction(int index) => SetOnly(selector.toggleFactionItems, index);
            private void OnlyUnitType(int index) => SetOnly(selector.toggleUnitTypesItems, index);
            private void OnlyVehicleType(int index) => SetOnly(selector.toggleVehicleTypesItems, index);

            private void Toggle(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                selector.SetOnlyItem(entries[index]);
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void RefreshSelectedGrid()
            {
                selectedUnits.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.selectedIcons != null)
                {
                    for (int i = 0; i < map.selectedIcons.Count; i++)
                    {
                        UnitMapIcon icon = map.selectedIcons[i] as UnitMapIcon;
                        if (icon != null && icon.unit != null) selectedUnits.Add(icon.unit);
                    }
                }

                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => false, _ => { });
            }

            private int SelectedCount()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return map == null || map.selectedIcons == null ? 0 : map.selectedIcons.Count;
            }

            private bool Ready => selector != null && selector.toggleFollowHUD != null &&
                                  selector.toggleLaser != null && selector.toggleFactionItems != null &&
                                  selector.toggleUnitTypesItems != null &&
                                  selector.toggleVehicleTypesItems != null;

            private void SetFilterInput(bool enabled)
            {
                resetFilters?.SetEnabled(enabled);
                clearTargets?.SetEnabled(enabled);
                followHud?.SetEnabled(enabled);
                laser?.SetEnabled(enabled);
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
            }

            private static int CountEnabled(List<TargetListSelector_ToggleButton> entries)
            {
                if (entries == null) return 0;
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].status) count++;
                return count;
            }

            private static string NativeTargetLabel(TargetListSelector_ToggleButton entry)
            {
                if (entry != null && entry.label != null && !string.IsNullOrEmpty(entry.label.text))
                    return AvTheme.Truncate(entry.label.text.Replace("\n", " ").ToUpperInvariant(), 22);
                return NativeLabel(entry, "FILTER");
            }

            private static string TargetUnitLabel(Unit unit)
            {
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return AvTheme.Truncate((name ?? "UNIT").ToUpperInvariant(), 34);
            }
        }

        // ----------------------------------------------------------- mission panel

        private sealed class MissionPresenter : Presenter
        {
            private readonly List<Objective> objectives = new List<Objective>();
            private RectTransform[] pages;
            private TMP_Text missionName;
            private TMP_Text missionDescription;
            private TMP_Text missionClock;
            private TMP_Text escalation;
            private MfdPagingGrid objectiveGrid;

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override int TabCount => 2;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "MISSION", "OBJECTIVES" }, SelectPage);
                pages = new[] { Shell.CreatePage("Mission"), Shell.CreatePage("Objectives") };
                BuildMissionPage(pages[0]);
                BuildObjectivesPage(pages[1]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                RefreshMissionCopy(mission, manager);
                RefreshObjectives();

                Shell.DataBar.State.text = "MISSION OVERVIEW";
                Shell.DataBar.SetChip(0, objectives.Count + " ACTIVE", objectives.Count > 0);
                Shell.DataBar.SetChip(1, EscalationLabel(manager), manager != null);
                Shell.DataBar.SetChip(2, MissionClock(manager), manager != null);
            }

            protected override string AmbientStatus() =>
                objectives.Count == 0 ? "MISSION STATUS — NO ACTIVE OBJECTIVES" :
                "MISSION STATUS — " + objectives.Count + " ACTIVE OBJECTIVES";

            private void BuildMissionPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "MISSION BRIEF", "LIVE OPERATIONS FEED");
                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 196f), AvTheme.RailInfo);
                missionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 14f, Shell.Body.width - AvTokens.Space5, 30f),
                    "LOADING MISSION", "metric-value");
                missionDescription = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 54f, Shell.Body.width - AvTokens.Space5, 74f),
                    "", "row-sub");
                missionClock = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 144f, Shell.Body.width - AvTokens.Space5, 18f),
                    "TIME —", "row-main");
                escalation = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 168f, Shell.Body.width - AvTokens.Space5, 18f),
                    "ESCALATION —", "row-main");
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "ACTIVE OBJECTIVES", "LIVE MISSION GRAPH");
                objectiveGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9);
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void RefreshMissionCopy(Mission mission, MissionManager manager)
            {
                if (mission == null)
                {
                    missionName.text = "NO MISSION LOADED";
                    missionDescription.text = "Waiting for mission controller data.";
                }
                else
                {
                    missionName.text = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    missionDescription.text = mission.missionSettings == null ? "" :
                        AvTheme.Truncate(mission.missionSettings.description ?? "", 180);
                }

                missionClock.text = "MISSION TIME  " + MissionClock(manager);
                escalation.text = "ESCALATION    " + EscalationLabel(manager);
            }

            private void RefreshObjectives()
            {
                objectives.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.HQ != null &&
                    MissionPosition.TryGetActiveObjectives(map.HQ, out List<Objective> active) && active != null)
                {
                    objectives.AddRange(active);
                }

                objectiveGrid.SetData(objectives.Count,
                    i => ObjectiveLabel(objectives[i]),
                    i => objectives[i] != null && objectives[i].CompletePercent >= 0.999f,
                    _ => { });
            }

            private static string ObjectiveLabel(Objective objective)
            {
                if (objective == null) return "OBJECTIVE LINK LOST";
                string prefix = objective.CompletePercent >= 0.999f ? "DONE  " : "LIVE  ";
                return prefix + AvTheme.Truncate(objective.ToUIString(oneLine: true)
                    .Replace("\n", " ").ToUpperInvariant(), 34);
            }

            private static string EscalationLabel(MissionManager manager)
            {
                if (manager == null) return "LINK LOST";
                if (manager.currentEscalation >= manager.strategicThreshold) return "STRATEGIC";
                if (manager.currentEscalation >= manager.tacticalThreshold) return "TACTICAL";
                return "CONVENTIONAL";
            }

            private static string MissionClock(MissionManager manager)
            {
                if (manager == null) return "—";
                int seconds = Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
        }

        private sealed class UnavailablePresenter : Presenter
        {
            public UnavailablePresenter(MFDScreen screen, VanillaMfdPanelId id) : base(screen, id) { }

            protected override void BuildContent()
            {
                RectTransform page = Shell.CreatePage("Unavailable");
                DrawSpine(page);
                AvStyled.SpineTick(page, 3f, -AvTokens.Space3);
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space3,
                                               Shell.Body.width - AvTokens.Space3, 18f),
                               "NATIVE ADAPTER UNAVAILABLE", "section-title");
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space6,
                                               Shell.Body.width - AvTokens.Space3, 56f),
                               "This game build changed the controller attached to this MFD. " +
                               "The source panel remains intact and will be restored when the map closes.",
                               "row-sub");
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "COMPATIBILITY HOLD";
                Shell.DataBar.SetChip(0, "NATIVE", false);
                Shell.DataBar.SetChip(1, "ADAPTER", false);
                Shell.DataBar.SetChip(2, "CHECK LOG", false);
            }

            protected override string AmbientStatus() => "UPDATE COMPATIBILITY REQUIRED";
        }

        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// A bounded, pooled control grid. It mutates labels and latch state in place, so a
        /// changing mission inventory cannot create or lay out an unbounded widget tree.
        /// </summary>
        private sealed class MfdPagingGrid
        {
            private readonly AvButton[] buttons;
            private readonly int perPage;
            private readonly TMP_Text pageLabel;
            private readonly AvButton previous;
            private readonly AvButton next;

            private int page;
            private int count;
            private Func<int, string> label;
            private Func<int, bool> selected;
            private Func<int, bool> enabled;
            private Action<int> clicked;

            public MfdPagingGrid(RectTransform parent, float y, float width, int columns, int rows,
                                  bool pager = true)
            {
                perPage = Mathf.Max(1, columns * rows);
                buttons = new AvButton[perPage];
                float gap = AvTokens.Gap;
                float cellWidth = (width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < perPage; i++)
                {
                    int slot = i;
                    int row = i / columns;
                    int column = i % columns;
                    buttons[i] = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + column * (cellWidth + gap),
                                 y - row * (AvTokens.RowHeight + gap),
                                 cellWidth, AvTokens.RowHeight),
                        "", "toggle", () => Click(slot), AvButtonStyle.Toggle);
                }

                if (pager)
                {
                    float pagerY = y - rows * (AvTokens.RowHeight + gap) - AvTokens.Space1;
                    AvButton[] pagerButtons = AvKit.Stepper(parent, AvTokens.Space3, pagerY,
                                                             width - AvTokens.Space3,
                                                             out pageLabel, Previous, Next);
                    previous = pagerButtons[0];
                    next = pagerButtons[1];
                }
            }

            public int CurrentIndex(int slot) => page * perPage + slot;

            public AvButton ButtonAt(int slot) =>
                slot >= 0 && slot < buttons.Length ? buttons[slot] : null;

            public void SetData(int newCount, Func<int, string> labels,
                                Func<int, bool> isSelected, Action<int> onClick,
                                Func<int, bool> isEnabled = null)
            {
                count = Mathf.Max(0, newCount);
                label = labels;
                selected = isSelected;
                clicked = onClick;
                enabled = isEnabled;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            public void ResetPage()
            {
                page = 0;
                Refresh();
            }

            /// <summary>Temporarily fence controls while a native model is still initializing.</summary>
            public void SetInteractable(bool on)
            {
                if (on)
                {
                    Refresh();
                    return;
                }

                for (int i = 0; i < buttons.Length; i++) buttons[i].SetEnabled(false);
                previous?.SetEnabled(false);
                next?.SetEnabled(false);
            }

            private int PageCount => Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));

            private void Previous()
            {
                if (page > 0) page--;
                Refresh();
            }

            private void Next()
            {
                if (page < PageCount - 1) page++;
                Refresh();
            }

            private void Click(int slot)
            {
                int index = CurrentIndex(slot);
                if (index < 0 || index >= count) return;
                if (enabled != null && !enabled(index)) return;
                clicked?.Invoke(index);
                Refresh();
            }

            private void Refresh()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    bool canUse = exists && (enabled == null || enabled(index));
                    buttons[i].SetEnabled(canUse);
                    buttons[i].SetLatched(exists && selected != null && selected(index));
                    buttons[i].SetText(exists && label != null ? label(index) : "");
                }

                if (pageLabel != null) pageLabel.text = count == 0 ? "NO ENTRIES" :
                    (page + 1).ToString() + " / " + PageCount.ToString();
                previous?.SetEnabled(page > 0);
                next?.SetEnabled(page < PageCount - 1);
            }
        }

        private static int CountEnabled(List<HUDOptions_ToggleButton> sources)
        {
            if (sources == null) return 0;
            int count = 0;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i] != null && sources[i].status) count++;
            return count;
        }

        private static string NativeCategoryLabel(HUDOptions_Category category, int fallback)
        {
            if (category != null && category.label != null && !string.IsNullOrEmpty(category.label.text))
                return category.label.text.ToUpperInvariant();
            return fallback == 0 ? "FRIENDLY" : fallback == 1 ? "ENEMY" : "CATEGORY " + (fallback + 1);
        }

        private static string NativeLabel(MonoBehaviour source, string fallback)
        {
            if (source == null) return fallback;

            TMP_Text[] labels = source.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null && !string.IsNullOrEmpty(labels[i].text))
                    return labels[i].text.Replace("\n", " ").ToUpperInvariant();
            }

            string name = source.gameObject.name;
            const string itemPrefix = "Item_";
            if (name.StartsWith(itemPrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(itemPrefix.Length);
            return string.IsNullOrEmpty(name) ? fallback : name.Replace("_", " ").ToUpperInvariant();
        }
    }

    /// <summary>Restores the native target filter's right-click "only this" behavior.</summary>
    internal sealed class MfdRightClickAction : MonoBehaviour, IPointerClickHandler
    {
        private Action action;

        public void Configure(Action onRightClick) => action = onRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            AvInput.Deselect(gameObject);
            action?.Invoke();
        }
    }
}
