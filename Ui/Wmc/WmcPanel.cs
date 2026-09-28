using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Spec bezel v2 §3: the WMC bezel panel on the maximized map — TACTICAL · FORM · PLAN · INSPECT ‖ SUPPLY · LOADOUT ·
    /// SQUADRON on an <see cref="AvScreen"/> (display glass included), claimed through <see cref="BezelRegistry.Wmc"/>, refreshed at
    /// 5 Hz. The header is the wing's title and four vitals chips (FUEL · AMMO · THREAT · MODE) on every tab; there are no metric
    /// tiles, so every page has 84 px more body.</summary>
    internal sealed class WmcPanel : IWingService
    {
        /// <summary>A tab not built yet says so in its tooltip.</summary>
        private static readonly string[] PendingTabs =
        {
            null, null, null, null, null, null, null,
        };

        public static WmcPanel Instance { get; private set; }
        public string Name => "WMC";

        private readonly Dictionary<string, AvButton> controls = new Dictionary<string, AvButton>();
        private readonly WmcContext context = new WmcContext();
        private readonly WmcMapOverlay overlay = new WmcMapOverlay();
        private readonly int[] headerKeys = { -1, -1, -1, -1, -1 };
        private IWmcPage[] pages;
        private WmcTactical tactical;
        private WmcForm form;
        private WmcPlan plan;
        private WmcInspect inspect;
        private WmcSupply supply;
        private WmcLoadout loadout;
        private WmcWing wingPage;
        private int shownPage = -1;
        private Color titleColor;
        private MFDScreen screen;
        private Button bezelButton;
        private GameObject root;
        private RectTransform content;
        private AvScreen shell;
        private float nextAttempt, nextRefresh;
        private bool wasVisible;
        private bool gaveUp;

        public WmcPanel()
        {
            Instance = this;
            context.Map.Placed += overlay.Ping;
        }

        public bool Visible => screen != null && screen.isActive && DynamicMap.mapMaximized;
        /// <summary>A page with the COMMAND scope row is on show (spec bezel v2 §6: an unarmed right-click MOVE is TACTICAL's and
        /// FORM's only).</summary>
        public bool CommandShowing => Visible && shell != null && (shell.Page == WmcTabs.Tactical || shell.Page == WmcTabs.Form);
        /// <summary>PLAN › ELEMENTS is showing: its map tools stay armed only here.</summary>
        public bool PlanShowing => Visible && shell != null && shell.Page == WmcTabs.Plan && plan != null && plan.Sub == WmcPlan.SubElements;
        public int Page => shell != null ? shell.Page : -1;
        public string PageName => shell != null && shell.Page >= 0 && shell.Page < WmcTabs.Labels.Length ? WmcTabs.Labels[shell.Page] : "";
        public IReadOnlyDictionary<string, AvButton> Controls => controls;
        public WmcContext Context => context;
        public WmcMapOverlay Overlay => overlay;
        public WmcTactical Tactical => tactical;
        public WmcForm Form => form;
        public WmcPlan Plan => plan;
        public WmcInspect InspectPage => inspect;
        public WmcSupply Supply => supply;
        public WmcLoadout Loadout => loadout;
        public WmcWing WingPage => wingPage;
        /// <summary>Labels that would still spill out of their box (the automation's text-fit audit).</summary>
        public int Overflow => content != null ? WmcKit.Overflow(content) : 0;

        /// <summary>The tallest empty band on the page on show, in px (the automation's density audit).</summary>
        public int Gap => content != null && shell != null ? WmcKit.LargestGap(content, shell.Body) : 0;

        public void Activate()
        {
            Reset();
            DynamicMap.onMapChanged -= overlay.Dirty;
            DynamicMap.onMapChanged += overlay.Dirty;
        }

        public void Deactivate()
        {
            DynamicMap.onMapChanged -= overlay.Dirty;
            Reset();
        }

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            // Map and HUD marks do not need the panel (spec WMC program §5); they ride on its tick.
            WingMarkers.Tick(WingService.Instance);
            // R5: a text field lets go after Enter, and never keeps the keyboard once the panel is out of sight.
            WmcNameField.TickAll();
            bool visible = Visible;
            if (wasVisible && !visible) WmcNameField.BlurAny();
            wasVisible = visible;
            bool enabled = !gaveUp && Plugin.Settings.ShowWmc.Value && GameAccess.MfdAvailable;
            if (!enabled)
            {
                if (screen != null && screen.isActive) screen.CloseScreen(screen.transform.localPosition);
                context.Map.Update(context, false);
                overlay.Hide();
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
            overlay.Tick(context, Visible);
            if (!Visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
            Refresh();
        }

        /// <summary>Maximize the map and select the WMC screen, as the player's bezel press would (automation).</summary>
        public void Open()
        {
            if (gaveUp)
            {
                WingToast.Show("The WMC could not install on the map's bezel (see the log); orders stay on the radial menu");
                return;
            }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            if (screen != null && !screen.isActive && bezelButton != null) bezelButton.onClick.Invoke();
            nextRefresh = 0f;
        }

        /// <summary>Latch a tab (automation; the tab bar calls <see cref="AvScreen.SetPage"/> itself). A tab not built yet stays
        /// where it is.</summary>
        public void Show(int tab)
        {
            if (shell == null || tab < 0 || tab >= WmcTabs.Labels.Length || pages[tab] == null) return;
            shell.SetPage(tab);
            nextRefresh = 0f;
        }

        /// <summary>INSPECT on this aircraft (INSPECT › on a row, a log line, automation); it never changes who orders go to.</summary>
        public void Inspect(uint id)
        {
            if (inspect == null || id == 0u) return;
            inspect.Focus(id);
            Show(WmcTabs.Inspect);
            Refresh();
        }

        /// <summary>Press a control by id as a click would (automation). A page's control shows its page first. False when there
        /// is none or it is hidden; a disabled button ignores the click itself.</summary>
        public bool Press(string id)
        {
            int tab = WmcTabs.Of(id);
            if (tab >= 0 && pages != null && pages[tab] != null)
            {
                Show(tab);
                if (tab == WmcTabs.Plan) plan.ShowSubFor(id);
                if (tab == WmcTabs.Squadron) wingPage.ShowSubFor(id);
                Refresh();
            }
            if (!controls.TryGetValue(id ?? "", out AvButton b) || b == null || !b.gameObject.activeInHierarchy) return false;
            b.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
            nextRefresh = 0f;
            return true;
        }

        private void Reset()
        {
            WmcNameField.BlurAny();
            MfdPresentation.Reset();
            BezelRegistry.Release(BezelRegistry.Wmc);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            content = null;
            screen = null;
            bezelButton = null;
            shell = null;
            pages = null;
            tactical = null;
            form = null;
            plan = null;
            inspect = null;
            supply = null;
            loadout = null;
            wingPage = null;
            shownPage = -1;
            fitButton = null;
            fitStep = -1;
            for (int i = 0; i < headerKeys.Length; i++) headerKeys[i] = -1;
            controls.Clear();
            context.Selection.Clear();
            context.Map.Disarm();
            PauseKeyHold.ReleaseAll();
            context.Draft.Clear();
            overlay.Destroy();
            WingMarkers.Reset();
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
            Plugin.Logger.LogWarning("[WMC] could not install the panel (" + reason + "). The radial menu still gives the orders; the WMC hotkey says why it cannot open.");
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
            content = (RectTransform)contentObject.transform;
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            // Spec bezel v2 §3: no metric row; the four chips carry the vitals on every tab.
            shell = AvScreen.Build(content, "WMC", WmcTabs.Labels, null, 4, AvTokens.PanelWidth, height, OnTab);
            if (shell.DataBar?.State != null) titleColor = shell.DataBar.State.color;
            BuildFitButton();
            BuildGroupRule();

            tactical = new WmcTactical(controls);
            form = new WmcForm(controls);
            plan = new WmcPlan(controls);
            inspect = new WmcInspect(controls);
            supply = new WmcSupply(controls);
            loadout = new WmcLoadout(controls);
            wingPage = new WmcWing(controls);
            pages = new IWmcPage[] { tactical, form, plan, inspect, supply, loadout, wingPage };
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null)
                {
                    shell.Tabs[i].SetEnabled(false);
                    shell.Tabs[i].WithTooltip(PendingTabs[i]);
                    continue;
                }
                var page = (RectTransform)shell.CreatePage(i, "Wmc" + WmcTabs.Labels[i]).transform;
                pages[i].Build(page, shell.Body);
            }
            for (int i = 0; i < shell.Tabs.Length; i++) controls["tab." + WmcTabs.Labels[i].ToLowerInvariant()] = shell.Tabs[i];

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
            // No "…" anywhere (spec WMC rebuild): every label overflows and shrinks to the 10 px floor instead.
            WmcKit.FitAll(content);
            shell.SetPage(WmcTabs.Tactical);
            return s;
        }

        /// <summary>A 2 px accent rule between the flying tabs and the logistics tabs (spec bezel v2 §2).</summary>
        private void BuildGroupRule()
        {
            if (shell.Tabs.Length <= WmcTabs.FirstLogistics) return;
            var tab = (RectTransform)shell.Tabs[WmcTabs.FirstLogistics].transform;
            Vector2 at = tab.anchoredPosition;
            AvKit.Rule(content, new Rect(at.x - 1f, at.y - 3f, 2f, tab.rect.height - 6f), AvTheme.Frame).raycastTarget = false;
        }

        /// <summary>ROOM in the title row, where the page index sat (four labelled tabs need no "01/04").</summary>
        private static readonly string[] FitLabels = { "FIT", "ALL", "FOLLOW" };
        private static readonly string[] FitTips =
        {
            "Frame the wing on the map (you and every wingman).", "Show the whole theatre.", "Back to following you.",
        };
        private AvButton fitButton;
        private int fitStep = -1;
        private float fitAt;

        /// <summary>[FIT] where the page index sat (spec bezel v2 §3): each press one step of FIT › ALL › FOLLOW; the label names the
        /// next step, and reads FIT again once the map follows you. Hidden when the game's map view could not be reached.</summary>
        private void BuildFitButton()
        {
            AvStyled.DataBar bar = shell.DataBar;
            if (bar?.PageIndex == null) return;
            RectTransform slot = bar.PageIndex.rectTransform;
            bar.PageIndex.gameObject.SetActive(false);
            Vector2 at = slot.anchoredPosition;
            fitButton = AvStyled.Button(content, new Rect(at.x + 1f, at.y - 4f, slot.rect.width - 2f, 24f), "FIT", "btn", PressFit,
                AvButtonStyle.Quiet);
            controls["hdr.fit"] = fitButton;
            fitButton.gameObject.SetActive(WmcMap.Usable);
            SetFit(0);
        }

        private void PressFit()
        {
            fitAt = Time.unscaledTime;
            switch (fitStep)
            {
                case 0:
                    var box = new MapBox();
                    Aircraft player = context.Wing?.Player;
                    if (player != null) box.Add(player.GlobalPosition().x, player.GlobalPosition().z);
                    for (int i = 0; i < context.Count; i++)
                    {
                        Unit u = WmcContext.UnitOf(context.Rows[i].Id);
                        if (u == null) continue;
                        GlobalPosition g = u.GlobalPosition();
                        box.Add(g.x, g.z);
                    }
                    WmcMap.Fit(box);
                    SetFit(1);
                    break;
                case 1:
                    WmcMap.All();
                    SetFit(2);
                    break;
                default:
                    WmcMap.Follow();
                    SetFit(0);
                    break;
            }
        }

        private void SetFit(int step)
        {
            if (fitButton == null || step == fitStep) return;
            fitStep = step;
            fitButton.SetText(FitLabels[step]);
            fitButton.WithTooltip(FitTips[step]);
        }

        private void OnTab(int tab)
        {
            WmcNameField.BlurAny();
            AvKit.Popup.CloseAny();
            nextRefresh = 0f;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            foreach (Image img in button.GetComponentsInChildren<Image>(true))
                if (img.gameObject != button.gameObject) return img;
            return button.GetComponent<Image>();
        }

        /// <summary>The context's rows and scope now, bezel or not (the room reads it).</summary>
        public void FillContext() => Fill();

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
            if (shell == null || pages == null) return;
            Fill();
            RefreshHeader();
            int page = shell.Page;
            IWmcPage p = page >= 0 && page < pages.Length ? pages[page] : null;
            if (p != null)
            {
                if (page != shownPage) p.Shown(context);
                p.Refresh(context);
            }
            shownPage = page;
            bool fresh = WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f;
            shell.WriteStatus(p?.Alert, context.Map.Prompt(context.ScopeLabel), fresh ? WingToast.Last : p?.Hint ?? "");
        }

        /// <summary>Title and chips, each rebuilt only when its inputs change (no strings per refresh in steady state).</summary>
        private void RefreshHeader()
        {
            // The map follows you again (the game's own controls, or FOLLOW): FIT starts over.
            if (fitStep > 0 && Time.unscaledTime - fitAt > 1f && WmcMap.Following) SetFit(0);
            int airborne = 0;
            for (int i = 0; i < context.Count; i++)
            {
                var duty = (MemberDuty)context.Rows[i].Duty;
                if (duty != MemberDuty.Grounded && duty != MemberDuty.Settled) airborne++;
            }
            int max = WingService.MaxMembers;
            int pending = !context.Client && SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0;
            if (Changed(0, context.Count * 100000 + max * 1000 + airborne * 100 + pending * 4 + (context.Client ? 2 : 0) + (context.Stale ? 1 : 0))
                && shell.DataBar?.State != null)
            {
                shell.DataBar.State.text = WmcHeader.Title(context.Count, max, airborne, pending, context.Client, context.Stale, out string st);
                shell.DataBar.State.color = st == "danger" ? AvTheme.Alert : titleColor;
            }
            WingVitals v = WingVitals.Of(context.Rows, context.Count);
            int fuelKey = v.Count * 100000 + Percent(v.MinFuel) * 100 + (v.BingoSlot + 1) * 10 + v.JokerSlot + 1;
            if (Changed(1, fuelKey)) Chip(0, WmcHeader.Fuel(v, out string s0), s0);
            int ammoKey = v.Count * 100000 + Percent(v.MinAmmo) * 100 + v.WinchesterSlot + 1;
            if (Changed(2, ammoKey)) Chip(1, WmcHeader.Ammo(v, out string s1), s1);
            int hostiles = context.Wing != null && !context.Client ? context.Wing.HostilesNear() : -1;
            if (Changed(3, (hostiles + 1) * 100 + v.DefendingSlot + 1)) Chip(2, WmcHeader.Threat(v, hostiles, out string s2), s2);
            if (Changed(4, (int)context.Map.Mode * 2 + (context.Client ? 1 : 0)))
                Chip(3, WmcHeader.Mode(context.Map.Mode, context.Client, out string s3), s3);
        }

        private static int Percent(float f) => float.IsNaN(f) ? -1 : Mathf.RoundToInt(f * 100f);

        private bool Changed(int slot, int key)
        {
            if (headerKeys[slot] == key) return false;
            headerKeys[slot] = key;
            return true;
        }

        private void Chip(int i, string text, string state) => shell.DataBar?.SetChip(i, text, state);
    }
}
