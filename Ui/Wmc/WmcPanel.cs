using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Spec WMC rebuild §bezel shell: the WMC bezel panel on the maximized map — the 0.9 four tabs TACTICAL · SUPPLY ·
    /// LOADOUT · WING on an <see cref="AvScreen"/> (display glass included), claimed through <see cref="BezelRegistry.Wmc"/>,
    /// refreshed at 5 Hz. The header's metric tiles change with the tab; ROOM opens the planning room from every tab.</summary>
    internal sealed class WmcPanel : IWingService
    {
        public const int TabTactical = 0, TabSupply = 1, TabLoadout = 2, TabWing = 3;
        public static readonly string[] TabLabels = { "TACTICAL", "SUPPLY", "LOADOUT", "WING" };

        private static readonly string[] PendingTabs =
        {
            null,
            "SUPPLY: the 0.9 shop on the 1.0 ledger (pilot, airframe, fit, base, requisition). Arrives in a later update.",
            "LOADOUT: templates, hardpoints and livery. Arrives in a later update.",
            "WING: the squadron roster and the pilot dossier. Arrives in a later update.",
        };

        public static WmcPanel Instance { get; private set; }
        public string Name => "WMC";

        private readonly Dictionary<string, AvButton> controls = new Dictionary<string, AvButton>();
        private readonly WmcContext context = new WmcContext();
        private readonly WmcMapOverlay overlay = new WmcMapOverlay();
        private readonly int[] headerKeys = { -1, -1, -1, -1 };
        private string profileShown;
        private IWmcPage[] pages;
        private WmcTactical tactical;
        private WmcMetricRow metrics;
        private MFDScreen screen;
        private Button bezelButton;
        private GameObject root;
        private RectTransform content;
        private AvScreen shell;
        private float nextAttempt, nextRefresh;
        private bool gaveUp;

        public WmcPanel()
        {
            Instance = this;
            context.Map.Placed += overlay.Ping;
        }

        public bool Visible => screen != null && screen.isActive && DynamicMap.mapMaximized;
        /// <summary>TACTICAL is the page on show (an unarmed right-click MOVE is TACTICAL's only; spec WMC rebuild).</summary>
        public bool TacticalShowing => Visible && shell != null && shell.Page == TabTactical;
        public int Page => shell != null ? shell.Page : -1;
        public int Sub => tactical != null ? tactical.Sub : -1;
        public IReadOnlyDictionary<string, AvButton> Controls => controls;
        public WmcContext Context => context;
        public WmcMapOverlay Overlay => overlay;
        public WmcTactical Tactical => tactical;
        /// <summary>Labels that would still spill out of their box (the automation's text-fit audit).</summary>
        public int Overflow => content != null ? WmcKit.Overflow(content) : 0;

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
            // Review P5 I4: the room places map orders too; a mode armed there stays armed.
            context.Map.Update(context, Visible || (WmcRoom.Instance != null && WmcRoom.Instance.IsOpen));
            overlay.Tick(context, Visible);
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

        /// <summary>Latch a tab (automation; the tab bar calls <see cref="AvScreen.SetPage"/> itself). A tab not built yet stays
        /// where it is.</summary>
        public void Show(int tab)
        {
            if (shell == null || tab < 0 || tab >= TabLabels.Length || pages[tab] == null) return;
            shell.SetPage(tab);
            nextRefresh = 0f;
        }

        /// <summary>Press a control by id as a click would (automation). A TACTICAL control shows TACTICAL and its sub-page
        /// first. False when there is none or it is hidden; a disabled button ignores the click itself.</summary>
        public bool Press(string id)
        {
            if (id != null && id.StartsWith("tac.", StringComparison.Ordinal) && tactical != null)
            {
                Show(TabTactical);
                tactical.ShowSubFor(id);
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
            metrics = null;
            profileShown = null;
            for (int i = 0; i < headerKeys.Length; i++) headerKeys[i] = -1;
            controls.Clear();
            context.Selection.Clear();
            context.Inspected = 0u;
            context.Map.Disarm();
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
            content = (RectTransform)contentObject.transform;
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            // Keys are the panel's own (they change per tab); the toolkit's metric row gets none.
            shell = AvScreen.Build(content, "WMC", TabLabels,
                new[] { new[] { "", "" }, new[] { "", "" }, new[] { "", "" } }, 4, AvTokens.PanelWidth, height, OnTab);
            metrics = new WmcMetricRow(content, shell.Metrics);
            BuildRoomButton();

            tactical = new WmcTactical(controls);
            pages = new IWmcPage[] { tactical, null, null, null };
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null)
                {
                    shell.Tabs[i].SetEnabled(false);
                    shell.Tabs[i].WithTooltip(PendingTabs[i]);
                    continue;
                }
                var page = (RectTransform)shell.CreatePage(i, "Wmc" + TabLabels[i]).transform;
                pages[i].Build(page, shell.Body);
            }
            for (int i = 0; i < shell.Tabs.Length; i++) controls["tab." + TabLabels[i].ToLowerInvariant()] = shell.Tabs[i];

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
            shell.SetPage(TabTactical);
            return s;
        }

        /// <summary>ROOM in the title row, where the page index sat (four labelled tabs need no "01/04").</summary>
        private void BuildRoomButton()
        {
            AvStyled.DataBar bar = shell.DataBar;
            if (bar?.PageIndex == null) return;
            RectTransform slot = bar.PageIndex.rectTransform;
            bar.PageIndex.gameObject.SetActive(false);
            Vector2 at = slot.anchoredPosition;
            AvButton room = AvStyled.Button(content, new Rect(at.x + 1f, at.y - 4f, slot.rect.width - 2f, 24f), "ROOM", "btn",
                () => WmcRoom.Instance?.Open(), AvButtonStyle.Quiet);
            room.WithTooltip("Open the planning room: plans, behaviour, squadron and workshop.");
            controls["hdr.room"] = room;
        }

        private void OnTab(int tab)
        {
            metrics?.SetKeys(WmcHeader.Keys(tab));
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
            if (context.Inspected != 0u && WingRows.IndexOf(context.Rows, context.Count, context.Inspected) < 0) context.Inspected = 0u;
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
                p.Metrics(context, metrics);
                p.Refresh(context);
            }
            bool fresh = WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f;
            shell.WriteStatus(p?.Alert, context.Map.Prompt(context.ScopeLabel), fresh ? WingToast.Last : p?.Hint ?? "");
        }

        /// <summary>Title and chips, each rebuilt only when its inputs change (no strings per refresh in steady state).</summary>
        private void RefreshHeader()
        {
            int airborne = 0;
            for (int i = 0; i < context.Count; i++)
            {
                var duty = (MemberDuty)context.Rows[i].Duty;
                if (duty != MemberDuty.Grounded && duty != MemberDuty.Settled) airborne++;
            }
            int max = WingService.MaxMembers;
            if (Changed(0, context.Count * 10000 + max * 100 + airborne) && shell.DataBar?.State != null)
                shell.DataBar.State.text = WmcHeader.Title(context.Count, max, airborne);
            int pending = !context.Client && SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0;
            if (Changed(1, context.Count * 1000 + pending * 10 + (context.Client ? 4 : 0) + (context.Stale ? 2 : 0)))
                Chip(0, WmcHeader.Link(context.Count, pending, context.Client, context.Stale, out string s0), s0);
            // Behaviour profiles arrive in R12; until then the chip names the scope element's doctrine pattern.
            string profile = context.Wing != null && !context.Client ? context.Wing.DoctrineOf(context.ScopeElement).PatternName : WmcText.Unknown;
            if (!ReferenceEquals(profile, profileShown) && profile != profileShown)
            {
                profileShown = profile;
                Chip(1, WmcHeader.Profile(profile, false, out string s1), s1);
            }
            bool offline = context.Client || !WingSupplyReserve.HasFaction;
            if (Changed(2, WingSupplyReserve.Count * 100 + WingSupplyReserve.Capacity * 2 + (offline ? 1 : 0)))
                Chip(2, WmcHeader.Reserve(WingSupplyReserve.Count, WingSupplyReserve.Capacity, offline, out string s2), s2);
            if (Changed(3, (int)context.Map.Mode * 2 + (context.Client ? 1 : 0)))
                Chip(3, WmcHeader.Mode(context.Map.Mode, context.Client, out string s3), s3);
        }

        private bool Changed(int slot, int key)
        {
            if (headerKeys[slot] == key) return false;
            headerKeys[slot] = key;
            return true;
        }

        private void Chip(int i, string text, string state) => shell.DataBar?.SetChip(i, text, state);
    }
}
