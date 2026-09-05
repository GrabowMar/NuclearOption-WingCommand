using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
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
    ///
    /// Split by concern across partial files: this file holds the attach/detach lifecycle
    /// and the shared Presenter/MfdShell harness. Each vanilla screen's presenter lives in
    /// its own file - see VanillaMfdRebuild.Map.cs, .Hud.cs, .Faction.cs, .Target.cs,
    /// .Mission.cs - and the shared MfdPagingGrid widget lives in .PagingGrid.cs.
    /// </summary>
    internal static partial class VanillaMfdRebuild
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
}
