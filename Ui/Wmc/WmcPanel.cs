using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Spec M7b §3: the WMC bezel panel on the maximized map — an <see cref="AvScreen"/> (display glass
    /// included) with six tabs, claimed through <see cref="BezelRegistry.Wmc"/>, refreshed at 5 Hz.</summary>
    internal sealed class WmcPanel : IWingService
    {
        public const int TabWing = 0, TabOrders = 1, TabForm = 2, TabDoctrine = 3, TabAp = 4, TabLog = 5;
        public static readonly string[] TabLabels = { "WING", "ORDERS", "FORM", "DOCT", "AP", "LOG" };

        public static WmcPanel Instance { get; private set; }
        public string Name => "WMC";

        private readonly Dictionary<string, AvButton> controls = new Dictionary<string, AvButton>();
        private readonly WmcContext context = new WmcContext();
        private readonly WmcScopeBar scopeBar = new WmcScopeBar();
        private IWmcTab[] tabs;
        private MFDScreen screen;
        private Button bezelButton;
        private GameObject root;
        private AvScreen shell;
        private float nextAttempt, nextRefresh;
        private bool gaveUp;

        public WmcPanel() => Instance = this;

        public bool Visible => screen != null && screen.isActive && DynamicMap.mapMaximized;
        public int Page => shell != null ? shell.Page : -1;
        public IReadOnlyDictionary<string, AvButton> Controls => controls;
        public WmcContext Context => context;

        public void Activate() => Reset();

        public void Deactivate() => Reset();

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            bool enabled = !gaveUp && Plugin.Settings.ShowWmc.Value && GameAccess.MfdAvailable;
            if (!enabled)
            {
                if (screen != null && screen.isActive) screen.CloseScreen(screen.transform.localPosition);
                context.Map.Update(context, false);
                return;
            }
            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }
            MfdPresentation.Tick();
            // Every frame: the right button is followed per frame (spec WMC program §5).
            context.Map.Update(context, Visible);
            if (!Visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
            Refresh();
        }

        /// <summary>Maximize the map and select the WMC screen, as the player's bezel press would (automation).</summary>
        public void Open()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            if (screen != null && !screen.isActive && bezelButton != null) bezelButton.onClick.Invoke();
            nextRefresh = 0f;
        }

        /// <summary>Latch a tab (automation; the tab bar calls <see cref="AvScreen.SetPage"/> itself).</summary>
        public void Show(int tab)
        {
            if (shell == null || tab < 0 || tab >= TabLabels.Length) return;
            shell.SetPage(tab);
            nextRefresh = 0f;
        }

        /// <summary>Press a control by id as a click would (automation). False when there is none or it is hidden; a
        /// disabled button ignores the click itself (its state is private to the shared toolkit).</summary>
        public bool Press(string id)
        {
            if (!controls.TryGetValue(id, out AvButton b) || b == null || !b.gameObject.activeInHierarchy) return false;
            b.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
            nextRefresh = 0f;
            return true;
        }

        private void Reset()
        {
            MfdPresentation.Reset();
            BezelRegistry.Release(BezelRegistry.Wmc);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            screen = null;
            bezelButton = null;
            shell = null;
            tabs = null;
            controls.Clear();
            context.Selection.Clear();
            context.Map.Disarm();
            context.Draft.Clear();
            nextAttempt = nextRefresh = 0f;
            gaveUp = false;
        }

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;
                RetireStaleScreens();
                if (!MfdBezel.TryClaim(preferLeft: true, mfd: mfd,
                        out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    Fail("no free bezel button on either column");
                    return;
                }
                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    BezelRegistry.Release(BezelRegistry.Wmc);
                    return;
                }
                screen = Build(template, buttons[slot], out float height);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Wmc);
                    return;
                }
                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    // Another plugin took the slot in the same frame: retry next second.
                    Reset();
                    return;
                }
                bezelButton = buttons[slot];
                MfdPresentation.Register(screen, screen.displayPanel.transform as RectTransform,
                    new Vector2(AvTokens.PanelWidth, height), buttons[slot], left);
                Plugin.LogVerbose("[WMC] installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1));
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("[WMC] install failed: " + e);
            }
        }

        /// <summary>A hot reload leaves the previous WMC object in the slot; drop it so this one can claim the bezel.</summary>
        private static void RetireStaleScreens()
        {
            foreach (MFDScreen s in UnityEngine.Object.FindObjectsOfType<MFDScreen>())
                if (s != null && s.shortName == "WMC") UnityEngine.Object.Destroy(s.gameObject);
            BezelRegistry.Release(BezelRegistry.Wmc);
        }

        private void Fail(string reason)
        {
            gaveUp = true;
            BezelRegistry.Release(BezelRegistry.Wmc);
            screen = null;
            Plugin.Logger.LogWarning("[WMC] could not install the panel (" + reason + "). The radial menu and hotkeys still work.");
        }

        private MFDScreen Build(MFDScreen template, Button bezel, out float height)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            if (sourceText != null) AvFont.Font = sourceText.font;

            root = new GameObject("WingCommand.Wmc", typeof(RectTransform), typeof(Image));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(template.transform.parent, false);
            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            height = AvScreen.ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var content = (RectTransform)contentObject.transform;
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            shell = AvScreen.Build(content, "WMC", TabLabels,
                new[] { new[] { "FUEL MIN", "%" }, new[] { "AMMO MIN", "%" }, new[] { "WING", "" } },
                3, AvTokens.PanelWidth, height, _ => nextRefresh = 0f);

            tabs = new IWmcTab[]
            {
                new WmcWingTab(controls), new WmcOrdersTab(controls), new WmcFormTab(controls),
                new WmcDoctrineTab(controls), new WmcApTab(controls), new WmcLogTab(controls),
            };
            // The scope bar sits above every page (spec WMC program §4); pages lay out below it.
            Rect body = shell.Body;
            scopeBar.Build(shell.Content, new Rect(body.x, body.y, body.width, WmcScopeBar.Height), controls);
            float drop = WmcScopeBar.Height + AvTokens.Space2;
            var pageBody = new Rect(body.x, body.y - drop, body.width, body.height - drop);
            for (int i = 0; i < tabs.Length; i++)
            {
                var page = (RectTransform)shell.CreatePage(i, "Wmc" + TabLabels[i]).transform;
                // Review focus 2: a page taller than the body scrolls instead of painting over the status strip.
                RectTransform parent = AvScreen.Scroll(page, pageBody, tabs[i].ContentHeight, out Rect area);
                tabs[i].Build(parent, area);
            }

            MFDScreen s = root.AddComponent<MFDScreen>();
            s.shortName = "WMC";
            s.displayPanel = contentObject;
            s.aircraftOnly = false;
            s.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            s.highlight = FindHighlight(bezel);
            if (s.label == null || s.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
                return null;
            }
            shell.SetPage(TabWing);
            return s;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            foreach (Image img in button.GetComponentsInChildren<Image>(true))
                if (img.gameObject != button.gameObject) return img;
            return button.GetComponent<Image>();
        }

        private void Fill()
        {
            WingService wing = WingService.Instance;
            context.Wing = wing;
            // A client's wing is the host's: its rows arrive as the host's snapshot (spec M6 §8). The role decides, not the
            // aircraft: a client that has not spawned has none (review M7b-1 I3).
            context.Client = WingNet.ClientOnly;
            context.MissionTime = wing?.MissionTime ?? 0f;
            if (!context.Client)
            {
                context.Count = wing != null ? wing.FillSnapshot(context.Rows) : 0;
                context.Stale = false;
            }
            else
            {
                WingMirror mirror = WingNet.Mirror;
                context.Stale = mirror == null || mirror.Stale(Time.unscaledTime);
                context.Count = 0;
                if (mirror != null)
                    for (int i = 0; i < mirror.Count && i < context.Rows.Length; i++) context.Rows[context.Count++] = mirror[i];
            }
            // Review focus 1: a selected aircraft that left drops out; the scope follows the selection.
            context.Selection.Prune(context.Rows, context.Count);
            context.Rescope();
        }

        /// <summary>One refresh now (automation: a selection made this call reaches the scope before a press).</summary>
        public void Refresh()
        {
            if (shell == null || tabs == null) return;
            Fill();
            WingSummary s = WingRows.Summary(context.Rows, context.Count);
            shell.Metrics[0].Set(WmcText.Percent(s.MinFuel).TrimEnd('%'), s.Bingo ? "BINGO" : "", WingRows.Bar(s.MinFuel),
                WmcUi.LevelColor(WmcStyle.Level(s.MinFuel)));
            shell.Metrics[1].Set(WmcText.Percent(s.MinAmmo).TrimEnd('%'), "", WingRows.Bar(s.MinAmmo),
                WmcUi.LevelColor(WmcStyle.Level(s.MinAmmo)));
            shell.Metrics[2].Set(s.Count + "/" + WingService.MaxMembers, "", s.Count / (float)WingService.MaxMembers, AvTheme.Friendly);

            WingService wing = context.Wing;
            string task = wing != null && wing.Planner.Active ? wing.Planner.Current.Kind.ToString().ToUpperInvariant() : "FORM";
            if (shell.DataBar?.State != null) shell.DataBar.State.text = task;
            shell.DataBar?.SetChip(0, context.Client ? "CLIENT" : "HOST", !context.Client);
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            shell.DataBar?.SetChip(1, "AP", ap != null && ap.Session.Engaged);
            shell.DataBar?.SetChip(2, "MAP", DynamicMap.mapMaximized);

            scopeBar.Refresh(context);
            int page = shell.Page;
            if (page >= 0 && page < tabs.Length) tabs[page].Refresh(context);

            bool fresh = WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f;
            string hint = page >= 0 && page < tabs.Length ? tabs[page].Hint : "";
            shell.WriteStatus(null, null, fresh ? WingToast.Last : hint);
        }
    }
}
