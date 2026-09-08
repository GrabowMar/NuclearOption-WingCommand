using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Native WMC MFD registration using an available bezel slot and SetupButtons. Build known
    /// widgets with shared font/theme; native MFD lifecycle owns visibility.</summary>
    internal static partial class WmcScreen
    {
        private const float PanelWidth = AvTokens.PanelWidth;

        private const float Pad = WingUi.Pad;
        private const float RowHeight = WingUi.RowHeight;
        private const float Gap = WingUi.Gap;
        private const float RowPitch = WingUi.RowPitch;

        private const float Space1 = WingUi.Space1;
        private const float Space2 = WingUi.Space2;
        private const float Space3 = WingUi.Space3;
        private const float Space4 = WingUi.Space4;
        private const float Space5 = WingUi.Space5;
        private const float Space6 = WingUi.Space6;

        private const float FontMicro = WingUi.FontMicro;
        private const float FontSmall = WingUi.FontSmall;
        private const float FontBody = WingUi.FontBody;
        private const float FontLead = WingUi.FontLead;

        /// <summary>Single-line text-block height for hints, status, and readouts.</summary>
        private const float LineHeight = Space4;

        private const float GutterWidth = 62f;
        private const float ArrowWidth = 34f;
        private const float HeaderPagerArrowWidth = Space6 + Space1;
        private const float HeaderPagerLabelWidth = 40f;
        private const float HeaderPagerWidth = HeaderPagerArrowWidth * 2f + HeaderPagerLabelWidth;
        private const float HeaderPagerHeight = Space5;

        private const float StatusStripHeight = AvTokens.StatusStripHeight;

        /// <summary>Visible flight rows matching normal wing capacity; larger debug rosters
        /// paginate.</summary>
        private const int RosterRowsPerPage = 3;
        private const int SquadronRowsPerPage = 6;

        private enum Page
        {
            Tactical,
            Supply,
            Loadout,
            Wing,
        }

        private const int PageCount = 4;

        private static MFDScreen screen;
        private static TacticalPauseState tacticalPause;

        // Index roots by Page for shared lifecycle handling.
        private static readonly RectTransform[] pageRoots = new RectTransform[PageCount];
        private static readonly WingButton[] pageTabs = new WingButton[PageCount];
        private static readonly float[] pageHeights = new float[PageCount];

        /// <summary>Per-page status and hover-help text.</summary>
        private static readonly TMP_Text[] statusLabels = new TMP_Text[PageCount];

        /// <summary>Shared height across tabs so switching pages cannot move the bezel or
        /// controls.</summary>
        private static float panelHeight;

        private static Page page;
        private static RectTransform panelRect;
        private static RectTransform rosterArea;
        private static TMP_Text rosterEmptyLabel;
        private static WingButton rosterPrevButton;
        private static WingButton rosterNextButton;
        private static TMP_Text summaryLabel;
        private static AvStyled.DataBar dataBar;
        private static AvStyled.Metric fundsMetric;
        private static AvStyled.Metric fuelMetric;
        private static TMP_Text rosterPageLabel;
        private static WingButton holdButton;
        private static WingButton tightButton;
        private static WingButton freeButton;
        private static WingButton cargoButton;
        private static WingButton landButton;
        private static WingButton jamButton;
        private static WingButton seekAndDestroyButton;
        private static WingButton attackButton;
        private static WingButton holdHereButton;
        private static readonly WingButton[] preferenceButtons =
            new WingButton[WingWeaponPreferences.All.Length];


        /// <summary>Pilot inspection focus independent of tactical command selection; the dossier follows
        /// that pilot's current aircraft.</summary>
        private static WingPilot inspectPilot;

        /// <summary>Inspection-list page, independent of Tactical command-roster paging.</summary>
        private static int inspectPage;

        private static readonly List<RosterRow> rosterRows = new List<RosterRow>();
        private static readonly List<ShopAirframeTile> shopTiles = new List<ShopAirframeTile>();
        private static TMP_Text supplyFundsLabel;
        private static TMP_Text supplySquadronLabel;
        private static Image supplyPilotPortrait;
        private static Image supplyPilotRail;
        private static TMP_Text supplyPilotCountLabel;
        private static TMP_Text supplyPilotNameLabel;
        private static TMP_Text supplyPilotRankLabel;
        private static TMP_Text supplyPilotStatusLabel;
        private static WingButton supplyPilotPrev;
        private static WingButton supplyPilotNext;
        private static Image supplyDispatchRail;
        private static Image supplyDispatchIcon;
        private static TMP_Text supplyDispatchAirframeLabel;
        private static TMP_Text supplyDispatchStateLabel;
        private static RectTransform shopPager;
        private static TMP_Text shopPageLabel;
        private static WingButton shopPrevButton;
        private static WingButton shopNextButton;
        private static TMP_Text reserveLabel;
        private static TMP_Text reserveHintLabel;
        private static WingButton reserveReleaseButton;
        private static WingButton reserveHoldButton;
        private static TMP_Text offerDetailLabel;
        private static TMP_Text offerLoadoutLabel;
        private static WingButton shopTemplateButton;
        private static float shopTemplateRowY;
        private static float shopTemplateRowX;
        private static float shopTemplateRowWidth;
        private static WingButton exceedLimitButton;
        private static WingButton fullFuelButton;
        private static WingButton requisitionButton;
        private static WingButton launchNearestButton;
        private static WingButton launchAnyButton;
        private static readonly List<LaunchBaseRow> launchRows = new List<LaunchBaseRow>();
        private static RectTransform launchPager;
        private static TMP_Text launchPageLabel;
        private static WingButton launchPrevButton;
        private static WingButton launchNextButton;
        private static int launchPage;
        private static AircraftDefinition selectedOffer;
        private static int shopPage;
        private static int rosterPage;
        private static string lastTooltip;
        private static float nextAttempt;
        private static float nextRefresh;
        private static bool gaveUp;

        /// <summary>Whether WMC currently owns tactical member clicks.</summary>
        public static bool TacticalCommandModeActive =>
            Plugin.Settings.UseMfdPanel.Value && Plugin.Settings.MapCommandEnabled.Value &&
            DynamicMap.mapMaximized && screen != null && screen.isActive && page == Page.Tactical;

        // Panel lifecycle.

        /// <summary>Lazily install from the manager update without relying on VirtualMFD.Start
        /// ordering.</summary>
        public static void Tick(WingRegistry wing)
        {
            bool enabled = !gaveUp && GameAccess.MfdAvailable && Plugin.Settings.UseMfdPanel.Value;
            bool visible = enabled && screen != null && screen.isActive && DynamicMap.mapMaximized;
            UpdateTacticalPause(visible && Plugin.Settings.TacticalPauseInSingleplayer.Value &&
                                GameManager.gameState == GameState.SinglePlayer);

            if (!enabled)
            {
                if (screen != null && screen.isActive)
                    screen.CloseScreen(screen.transform.localPosition);
                ReleasePanelInput();
                return;
            }

            if (screen == null)
            {
                ReleasePanelInput();
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!visible)
            {
                // Close popups and release text focus when native bezel actions hide the panel.
                ReleasePanelInput();
                return;
            }

            // Refresh immediately when hover help changes; keep other page work on its normal cadence.
            string tooltip = WingButton.HoveredTooltip;
            if (!ReferenceEquals(tooltip, lastTooltip))
            {
                lastTooltip = tooltip;
                nextRefresh = 0f;
            }

            // Throttle formatted roster refreshes to avoid per-frame string allocation for slowly
            // changing readouts.
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
                Refresh(wing);
            }
        }

        /// <summary>Clear mission screen state for fresh installation next mission.</summary>
        public static void Reset()
        {
            UpdateTacticalPause(shouldPause: false);
            ReleasePanelInput();

            BezelRegistry.Release(BezelRegistry.Wmc);
            screen = null;
            page = Page.Tactical;
            panelRect = null;

            for (int i = 0; i < PageCount; i++)
            {
                pageRoots[i] = null;
                pageTabs[i] = null;
                pageHeights[i] = 0f;
                statusLabels[i] = null;
            }

            panelHeight = 0f;
            WingButton.ClearTooltip();

            RosterRow.Disarm();
            rosterArea = null;
            rosterEmptyLabel = null;
            rosterPrevButton = null;
            rosterNextButton = null;
            summaryLabel = null;
            dataBar = null;
            fundsMetric = null;
            fuelMetric = null;
            doctrineTitleLabel = null;
            doctrineProfileLabel = null;
            doctrineRulesLabel = null;
            doctrineWeaponsLabel = null;
            formationButtons = null;
            formationWingmenDots.Clear();
            formationVectorLines.Clear();
            rosterPageLabel = null;
            rosterRows.Clear();
            shopTiles.Clear();
            launchRows.Clear();
            liveryLabel = null;
            supplyFundsLabel = null;
            supplySquadronLabel = null;
            supplyPilotPortrait = null;
            supplyPilotRail = null;
            supplyPilotCountLabel = null;
            supplyPilotNameLabel = null;
            supplyPilotRankLabel = null;
            supplyPilotStatusLabel = null;
            supplyPilotPrev = null;
            supplyPilotNext = null;
            supplyDispatchRail = null;
            supplyDispatchIcon = null;
            supplyDispatchAirframeLabel = null;
            supplyDispatchStateLabel = null;
            shopPager = null;
            shopPageLabel = null;
            shopPrevButton = null;
            shopNextButton = null;
            reserveLabel = null;
            reserveHintLabel = null;
            reserveReleaseButton = null;
            reserveHoldButton = null;
            offerDetailLabel = null;
            offerLoadoutLabel = null;
            exceedLimitButton = null;
            fullFuelButton = null;
            requisitionButton = null;
            launchNearestButton = null;
            launchAnyButton = null;
            launchPager = null;
            launchPageLabel = null;
            launchPrevButton = null;
            launchNextButton = null;
            launchPage = 0;
            selectedOffer = null;
            shopPage = 0;
            rosterPage = 0;
            holdButton = null;
            tightButton = null;
            freeButton = null;
            cargoButton = null;
            landButton = null;
            jamButton = null;
            seekAndDestroyButton = null;
            attackButton = null;
            holdHereButton = null;

            for (int i = 0; i < preferenceButtons.Length; i++) preferenceButtons[i] = null;

            pylonRows.Clear();
            airframeTiles.Clear();
            airframePager = null;
            airframePrevButton = null;
            airframeNextButton = null;
            airframePageLabel = null;
            loadoutStatusLabel = null;
            loadoutProfileTitle = null;
            loadoutProfileRail = null;
            loadoutProfileIcon = null;
            templateLabel = null;
            templateNameField = null;
            templateSummaryLabel = null;
            templateSelectButton = null;
            templateNewButton = null;
            templateCopyButton = null;
            templateDeleteButton = null;
            pylonArea = null;
            pylonPrevButton = null;
            pylonNextButton = null;
            pylonPageLabel = null;
            pylonEmptyCard = null;
            pylonEmptyLabel = null;
            loadoutPopup = null;
            shopTemplatePopup = null;
            customPilotsPopup = null;
            shopTemplateButton = null;
            editingTemplateId = null;
            pylonPage = 0;
            inspectPage = 0;

            // Force keyboard release if a focused field was destroyed without deselection.
            WingKeyboardGuard.ForceRelease();

            pilotRows.Clear();
            pilotRosterArea = null;
            pilotEmptyLabel = null;
            pilotPager = null;
            pilotIdentityLabel = null;
            pilotRankLabel = null;
            pilotStatsLabel = null;
            pilotPersonaLabel = null;
            pilotXpBar = null;
            pilotBackgroundLabel = null;
            pilotPortrait = null;
            pilotPortraitFrame = null;
            pilotCardRail = null;
            pilotKiaOverlay = null;
            pilotSkillIcons.Clear();
            airframeCardRail = null;
            airframeTypeLabel = null;
            airframeStateLabel = null;
            airframeOrderLabel = null;
            airframeLoadoutLabel = null;
            airframeWeaponsLabel = null;
            airframeSilhouette = null;
            inspectPilot = null;

            lastTooltip = null;
            gaveUp = false;
        }

        private static void UpdateTacticalPause(bool shouldPause)
        {
            float scale = tacticalPause.Update(shouldPause, Time.timeScale,
                                               Plugin.Settings.TacticalPauseScale.Value);
            if (scale != Time.timeScale) Time.timeScale = scale;
        }

        private static void ReleasePanelInput()
        {
            if (WingKeyboardGuard.Captured)
            {
                WingKeyboardGuard.Defocus();
                WingKeyboardGuard.ForceRelease();
            }
            AvKit.Popup.CloseAny();
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(BezelRegistry.Wmc, preferLeft: true, mfd,
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

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Wmc);
                    return;
                }

                MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
                MfdPresentation.Register(screen, screen.displayPanel.transform as RectTransform,
                    new Vector2(PanelWidth, panelHeight), buttons[slot], left);
                Plugin.LogVerbose("WMC screen installed on " + (left ? "left" : "right") +
                                      " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("WMC screen install failed: " + e);
            }
        }

        private static void Fail(string reason)
        {
            gaveUp = true;
            screen = null;
            Plugin.Logger.LogWarning(
                "Could not install the WMC MFD screen (" + reason +
                "). The radial menu and hotkeys still work; there is no fallback panel.");
        }

        // Panel construction.

        private static MFDScreen Build(MFDScreen template, Button bezelButton)
        {
            WingUi.Font = FindFont(template);

            var root = new GameObject("WMC_Screen", typeof(RectTransform));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(template.transform.parent, worldPositionStays: false);

            // Copy working screen anchors and scale, letting native show/hide own localPosition. Fit
            // only child content in vanilla and defer layout to Boscali when present.
            var templateRt = (RectTransform)template.transform;
            rt.anchorMin = templateRt.anchorMin;
            rt.anchorMax = templateRt.anchorMax;
            rt.pivot = templateRt.pivot;
            rt.localScale = templateRt.localScale;

            // Parent background with controls so native CloseScreen hides both.
            var content = new GameObject("Content", typeof(RectTransform), typeof(Image));
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.SetParent(rt, worldPositionStays: false);
            Stretch(contentRt);
            Image bg = content.GetComponent<Image>();
            bg.sprite = WingUi.PanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;

            // The shared frame fades towards its foot; keep scenery out of the reading surface.
            Image backing = Rule(contentRt, new Rect(), AvTheme.Ground.WithAlpha(1f));
            Stretch(backing.rectTransform);
            backing.rectTransform.offsetMin = new Vector2(Space2, Space2);
            backing.rectTransform.offsetMax = new Vector2(-Space2, -Space2);

            float y = -Pad;
            y = AddTitle(contentRt, y);
            y = AddTabs(contentRt, y);

            pageRoots[(int)Page.Tactical] = PageRoot(contentRt, "TacticalPage");
            pageRoots[(int)Page.Supply] = PageRoot(contentRt, "SupplyPage");
            pageRoots[(int)Page.Loadout] = PageRoot(contentRt, "LoadoutPage");
            pageRoots[(int)Page.Wing] = PageRoot(contentRt, "WingPage");

            RectTransform tacticalRoot = pageRoots[(int)Page.Tactical];
            float tacticalY = y;
            tacticalY = AddSummary(tacticalRoot, tacticalY);
            tacticalY = AddRosterArea(tacticalRoot, tacticalY);
            tacticalY = AddEngagementSection(tacticalRoot, tacticalY);
            tacticalY = AddActions(tacticalRoot, tacticalY);

            // Build Supply in decision order: funds/capacity, pilot, purchase, then active-aircraft
            // assignment.
            RectTransform supplyRoot = pageRoots[(int)Page.Supply];
            float supplyY = y;
            supplyY = AddSupplyStatus(supplyRoot, supplyY);
            supplyY = AddPilotSelection(supplyRoot, supplyY);
            supplyY = AddShop(supplyRoot, supplyY);
            supplyY = AddAssignment(supplyRoot, supplyY);

            float loadoutY = AddLoadoutPage(pageRoots[(int)Page.Loadout], y);
            float wingY = AddWingPage(pageRoots[(int)Page.Wing], y);

            // Include pinned status-strip clearance in content height accounting.
            const float stripBlock = StatusStripHeight + Space2;
            pageHeights[(int)Page.Tactical] = Mathf.Abs(tacticalY) + stripBlock + Pad;
            pageHeights[(int)Page.Supply] = Mathf.Abs(supplyY) + stripBlock + Pad;
            pageHeights[(int)Page.Loadout] = Mathf.Abs(loadoutY) + stripBlock + Pad;
            pageHeights[(int)Page.Wing] = Mathf.Abs(wingY) + stripBlock + Pad;

            // Use the shared fixed height for every tab.
            panelHeight = AvTokens.PanelHeight;
            for (int i = 0; i < PageCount; i++)
                panelHeight = Mathf.Max(panelHeight, pageHeights[i]);

            // Pin status to one bottom position across all tabs.
            float stripY = -(panelHeight - Pad - StatusStripHeight);
            for (int i = 0; i < PageCount; i++)
                PinStatusStrip(pageRoots[i], stripY, (Page)i);

            panelRect = rt;
            rt.sizeDelta = new Vector2(PanelWidth, panelHeight);

            MFDScreen s = root.AddComponent<MFDScreen>();
            s.shortName = "WMC";
            s.displayPanel = content;
            s.aircraftOnly = false;
            s.label = FindLabel(bezelButton);
            s.highlight = FindHighlight(bezelButton, template);

            if (s.label == null)
            {
                UnityEngine.Object.Destroy(root);
                Fail("could not find the bezel button label");
                return null;
            }

            SetPage(Page.Tactical);

            return s;
        }

        private static RectTransform PageRoot(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Stretch(rt);
            return rt;
        }

        /// <summary>Build the WMC identity/state bar and three chips above shared funds and fuel
        /// metrics.</summary>
        private static float AddTitle(RectTransform parent, float y)
        {
            float inner = PanelWidth - Pad * 2f;

            var bar = new Rect(Pad, y, inner, AvTokens.TitleBarHeight + 2f);
            dataBar = AvStyled.TopBar(parent, bar, "WMC", 3);
            y -= bar.height + Space2;

            // Keep funds and minimum flight fuel visible above tabs on every page.
            const float metricHeight = 72f;
            var metrics = new Rect(Pad, y, inner, metricHeight);
            AvStyled.Box(parent, metrics, "metrics");
            float half = inner * 0.5f;
            fundsMetric = AvStyled.MetricCell(parent, new Rect(Pad, y, half, metricHeight), "SQUADRON FUNDS", "CR");
            fuelMetric = AvStyled.MetricCell(parent, new Rect(Pad + half, y, half, metricHeight), "FLIGHT FUEL", "% MIN");
            fundsMetric.Caption.color = fuelMetric.Caption.color = Dim();
            Rule(parent, new Rect(Pad + half, y, 1f, metricHeight), WingUi.BorderSubtle);

            return y - metricHeight - Space2;
        }

        private static float AddTabs(RectTransform parent, float y)
        {
            float w = (PanelWidth - Pad * 2f - Gap * (PageCount - 1)) / PageCount;

            pageTabs[(int)Page.Tactical] = Tab(parent, "TACTICAL", Page.Tactical, Pad, y, w);
            pageTabs[(int)Page.Supply] = Tab(parent, "SUPPLY", Page.Supply, Pad + w + Gap, y, w);
            pageTabs[(int)Page.Loadout] = Tab(parent, "LOADOUT", Page.Loadout,
                                              Pad + (w + Gap) * 2f, y, w);
            pageTabs[(int)Page.Wing] = Tab(parent, "WING", Page.Wing, Pad + (w + Gap) * 3f, y, w);

            y -= WingUi.TabHeight;
            Rule(parent, new Rect(Pad, y, PanelWidth - Pad * 2f, 1f), FrameColor());
            return y - Space3;
        }

        private static WingButton Tab(RectTransform parent, string text, Page target,
                                      float x, float y, float w) =>
            WingUi.Button(parent, text, new Rect(x, y, w, WingUi.TabHeight), FontSmall,
                          UiButtonStyle.Tab, () => SetPage(target));

        private static void SetPage(Page next)
        {
            page = next;

            for (int i = 0; i < PageCount; i++)
            {
                bool active = i == (int)next;
                if (pageRoots[i] != null) pageRoots[i].gameObject.SetActive(active);
                pageTabs[i]?.SetLatched(active);
            }

            // Keep panel size fixed when switching tabs.
            if (panelRect != null)
                panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);

            // Clear the previous page's hover text on tab changes.
            WingButton.ClearTooltip();

            // Defocus fields before closing page-owned popups so keyboard capture unwinds and hidden
            // scrims cannot consume clicks.
            WingKeyboardGuard.Defocus();
            AvKit.Popup.CloseAny();
            nextRefresh = 0f;

            // Remove command brackets immediately when leaving Tactical input mode.
            WingCommandManager manager = WingCommandManager.Instance;
            if (manager != null)
            {
                foreach (WingMember member in manager.Wing.Members)
                    WingMarkers.Repaint(member.Aircraft);
            }
        }

        /// <summary>Draw a shared section heading with a tick on the panel spine.</summary>
        private static float Heading(RectTransform parent, float y, string text)
        {
            AvStyled.SpineTick(parent, SpineX + 3f, y - 8f);
            return AvKit.Heading(parent, y, text, PanelWidth, WingUi.TextPrimary, WingUi.BorderSubtle);
        }

        /// <summary>Spine position inside the frame and outside the content column.</summary>
        private const float SpineX = 5f;

        /// <summary>Value with quiet previous/next arrows; retain generous click targets despite compact
        /// visuals.</summary>
        private static WingButton[] Stepper(RectTransform parent, float x, float y, float w,
                                            out TMP_Text valueLabel,
                                            Action onPrev, Action onNext,
                                            string tooltip = null)
        {
            Panel(parent, new Rect(x, y, w, RowHeight), RowColor());

            // Inset arrows to avoid doubled borders while preserving nearly full row-height targets.
            const float arrow = Space6 + Space1;
            WingButton prev = WingUi.Button(parent, "<",
                                            new Rect(x + 1f, y - 1f, arrow, RowHeight - 2f),
                                            FontBody, UiButtonStyle.Quiet, onPrev);
            WingButton next = WingUi.Button(parent, ">",
                                            new Rect(x + w - arrow - 1f, y - 1f,
                                                     arrow, RowHeight - 2f),
                                            FontBody, UiButtonStyle.Quiet, onNext);

            valueLabel = Label(parent, "",
                               new Rect(x + Space6 + Space2, y, w - (Space6 + Space2) * 2f, RowHeight),
                               Friendly(), FontBody, FontStyles.Normal, TextAlignmentOptions.Center);

            prev.WithTooltip(tooltip);
            next.WithTooltip(tooltip);
            return new[] { prev, next };
        }

        /// <summary>Dim engagement-row gutter label.</summary>
        private static void Gutter(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, GutterWidth - Gap, RowHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);

        /// <summary>Shared column geometry for headers and cells.</summary>
        private struct Column
        {
            public string Text;
            public float X;
            public float Width;
            public bool RightAligned;

            public Column(string text, float x, float width, bool rightAligned = false)
            {
                Text = text;
                X = x;
                Width = width;
                RightAligned = rightAligned;
            }
        }

        // Compact identity/state/fuel/ammunition columns; other details have dedicated controls or
        // dossier readouts.
        private static readonly Column[] RosterColumns =
        {
            new Column("PLANE", 28f, 58f),
            new Column("CALLSIGN", 90f, 90f),
            new Column("STATE", 184f, 62f),
            new Column("FUEL", 250f, 38f, rightAligned: true),
            new Column("AMMO", 292f, 38f, rightAligned: true),
        };

        /// <summary>Pilot-list header geometry.</summary>
        private static readonly Column[] PilotColumns =
        {
            new Column("CALLSIGN", 30f, 88f),
            new Column("STATUS", PanelWidth - Pad * 2f - 18f - 84f, 84f, rightAligned: true),
        };

        private static float ColumnHeaders(RectTransform parent, float y, Column[] columns)
        {
            foreach (Column column in columns)
            {
                Label(parent, column.Text, new Rect(Pad + column.X, y, column.Width, Space4),
                      Dim(), FontMicro, FontStyles.Normal,
                      column.RightAligned ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            }
            return y - Space4;
        }

        /// <summary>Footer page-turn arrow.</summary>
        private static WingButton Pager(RectTransform parent, float y, string glyph, Action onClick)
        {
            float x = glyph == "<" ? Pad : PanelWidth - Pad - ArrowWidth;
            return WingUi.Button(parent, glyph, new Rect(x, y, ArrowWidth, RowHeight),
                                 FontBody, UiButtonStyle.Quiet, onClick)
                         .WithTooltip(OrderHint.Pager);
        }

        private static TMP_Text PagerLabel(RectTransform parent, float y) =>
            Label(parent, "",
                  new Rect(Pad + ArrowWidth + Gap, y,
                           PanelWidth - Pad * 2f - (ArrowWidth + Gap) * 2f, RowHeight),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);

        /// <summary>Compact header pager with quiet inset arrows, separate from full-width list
        /// footers.</summary>
        private static RectTransform HeaderPager(RectTransform parent, float y, Action onPrevious,
                                                 Action onNext, out WingButton previous,
                                                 out TMP_Text label, out WingButton next)
        {
            float x = PanelWidth - Pad - HeaderPagerWidth;
            var go = new GameObject("HeaderPager", typeof(RectTransform));
            var root = go.GetComponent<RectTransform>();
            root.SetParent(parent, worldPositionStays: false);
            Place(root, new Rect(x, y, HeaderPagerWidth, HeaderPagerHeight));

            Panel(root, new Rect(0f, 0f, HeaderPagerWidth, HeaderPagerHeight), RowColor());
            Outline(root, new Rect(0f, 0f, HeaderPagerWidth, HeaderPagerHeight), FrameColor());

            previous = WingUi.Button(root, "<",
                                     new Rect(1f, -1f, HeaderPagerArrowWidth, HeaderPagerHeight - 2f),
                                     FontBody, UiButtonStyle.Quiet, onPrevious)
                             .WithTooltip("Show the previous page.");
            label = Label(root, "", new Rect(HeaderPagerArrowWidth, 0f,
                                               HeaderPagerLabelWidth, HeaderPagerHeight),
                          Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            next = WingUi.Button(root, ">",
                                 new Rect(HeaderPagerWidth - HeaderPagerArrowWidth - 1f,
                                          -1f, HeaderPagerArrowWidth, HeaderPagerHeight - 2f),
                                 FontBody, UiButtonStyle.Quiet, onNext)
                         .WithTooltip("Show the next page.");

            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>Update header pager bounds and page fraction consistently.</summary>
        private static void RefreshHeaderPager(RectTransform pager, WingButton previous, TMP_Text label,
                                               WingButton next, int page, int pageCount)
        {
            bool multiPage = pageCount > 1;
            pager?.gameObject.SetActive(multiPage);
            previous?.gameObject.SetActive(multiPage);
            next?.gameObject.SetActive(multiPage);
            if (label == null) return;

            label.gameObject.SetActive(multiPage);

            if (!multiPage)
            {
                label.text = string.Empty;
                return;
            }

            label.text = PageFraction(page, pageCount);
            previous?.SetEnabled(page > 0);
            next?.SetEnabled(page < pageCount - 1);
        }

        private static string PageFraction(int page, int pageCount) =>
            (page + 1) + " / " + pageCount;

        /// <summary>Shared list-footer count and pagination text.</summary>
        private static string PageSummary(int count, int page, int pageCount,
                                          string singular, string plural)
        {
            if (count <= 0) return string.Empty;

            string noun = count == 1 ? singular : plural;
            return pageCount == 1
                ? count + " " + noun
                : "PAGE " + PageFraction(page, pageCount) + "  ·  " + count + " " + noun;
        }

        /// <summary>Order-grid button using standard body text.</summary>
        private static WingButton GridButton(RectTransform parent, string text, float x, float y,
                                             float w, Action onClick) =>
            GridButton(parent, text, x, y, w, onClick, UiButtonStyle.Default);

        private static WingButton GridButton(RectTransform parent, string text, float x, float y,
                                             float w, Action onClick, UiButtonStyle style) =>
            WingUi.Button(parent, text, new Rect(x, y, w, RowHeight), FontSmall, style, onClick);

        /// <summary>Pin each page's two-line status/hover strip at the same bottom position, independent
        /// of content height.</summary>
        private static void PinStatusStrip(RectTransform parent, float y, Page page)
        {
            float w = PanelWidth - Pad * 2f;

            TMP_Text label = AvStyled.StatusStrip(parent, new Rect(Pad, y, w, StatusStripHeight));

            statusLabels[(int)page] = label;
        }

        /// <summary>Show current hover help or the page fallback in its status strip.</summary>
        private static void RefreshStatusStrip(Page page, string fallback)
        {
            TMP_Text label = statusLabels[(int)page];
            if (label == null) return;

            string tooltip = WingButton.HoveredTooltip;
            bool hovering = !string.IsNullOrEmpty(tooltip);

            label.text = hovering ? "> " + tooltip : "> " + fallback;
            label.color = hovering ? WingUi.TextPrimary : Dim();
        }

        /// <summary>Full-area empty-list message toggled during refresh.</summary>
        private static TMP_Text EmptyNote(RectTransform area, string text)
        {
            TMP_Text label = Label(area, text,
                                   new Rect(Space4, 0f, area.rect.width - Space4 * 2f,
                                            area.rect.height),
                                   Dim(), FontSmall, FontStyles.Normal,
                                   TextAlignmentOptions.Center);
            label.enableWordWrapping = true;
            label.gameObject.SetActive(false);
            return label;
        }

        /// <summary>Secondary explanatory line beneath a section heading.</summary>
        private static TMP_Text Hint(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

        /// <summary>Refresh persistent top-bar state and metrics independently of the selected
        /// tab.</summary>
        private static void RefreshDataBar(WingRegistry wing)
        {
            int count = wing?.Count ?? 0;

            if (dataBar != null)
            {
                dataBar.State.text = count == 0
                    ? "NO WING"
                    : "WING " + count + " / " + WingRegistry.WingLimitLabel;
                dataBar.State.color = count == 0 ? WingUi.Dim : WingUi.TextPrimary;

                dataBar.SetChip(0, count == 0 ? "NO LINK" : "LINKED " + count, count > 0);
                dataBar.SetChip(1, wing != null ? wing.Roe.ToString().ToUpperInvariant() : "ROE --",
                                wing != null && wing.Roe != WingRoe.Hold);
                dataBar.SetChip(2, WingShop.Allocation > 0f ? "SUPPLY" : "NO FUNDS",
                                WingShop.Allocation > 0f);
            }

            if (fundsMetric != null)
            {
                fundsMetric.Set(Grouped(WingShop.Allocation),
                                "HOLD " + WingSupplyReserve.Count + " / " + WingSupplyReserve.Capacity,
                                1f, WingUi.RailCyan);
            }

            if (fuelMetric != null)
            {
                // Report the lowest member fuel fraction, which determines the first bingo call.
                float lowest = 1f;
                bool any = false;
                if (wing != null)
                {
                    for (int i = 0; i < wing.Members.Count; i++)
                    {
                        WingMember member = wing.Members[i];
                        if (member == null || !member.Alive || member.Aircraft == null) continue;
                        float f = member.Fuel;
                        if (!any || f < lowest) lowest = f;
                        any = true;
                    }
                }

                float bingoThreshold = Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel;
                bool bingo = any && lowest <= bingoThreshold;
                fuelMetric.Set(
                    any ? Mathf.RoundToInt(lowest * 100f).ToString() : "--",
                    any ? "BINGO AT " + Mathf.RoundToInt(bingoThreshold * 100f) + "%" : "NO FLIGHT",
                    any ? lowest : 0f,
                    bingo ? WingUi.Alert : any ? WingUi.RailEmerald : WingUi.Disabled);
            }
        }

        // Panel refresh.

        /// <summary>Refresh only the visible page so hidden shop catalogue scans do not run while
        /// inspecting other tabs.</summary>
        private static void Refresh(WingRegistry wing)
        {
            PruneFocus(wing);

            RefreshDataBar(wing);

            switch (page)
            {
                case Page.Supply:
                    RefreshSupplyPilot();
                    RefreshSupplyStatus();
                    RefreshShop();
                    RefreshLaunchFrom();
                    // Refresh reserve after catalogue changes can alter selected-airframe actions.
                    RefreshReserve();
                    RefreshStatusStrip(Page.Supply, "Choose a pilot, airframe and launch base, then requisition.");
                    break;

                case Page.Loadout:
                    RefreshLoadoutPage();
                    RefreshStatusStrip(Page.Loadout, "Click a pylon to edit its stores. Select this fit on SUPPLY.");
                    break;

                case Page.Wing:
                    RefreshWingPage(wing);
                    RefreshStatusStrip(Page.Wing, "Select a pilot to inspect their record and assigned aircraft.");
                    break;

                default:
                    RefreshTactical(wing);
                    break;
            }
        }

        // Shared roster helpers.

        private static AircraftDefinition DefinitionOf(WingMember member) =>
            member != null && member.Aircraft != null ? member.Aircraft.definition : null;

        /// <summary>Pilot-list pager; disable arrows when only one page exists.</summary>
        private sealed class PilotPager
        {
            private readonly WingButton prev;
            private readonly WingButton next;
            private readonly TMP_Text label;

            public PilotPager(RectTransform parent, float y)
            {
                prev = Pager(parent, y, "<", () => Turn(-1));
                label = PagerLabel(parent, y);
                next = Pager(parent, y, ">", () => Turn(1));
            }

            private static void Turn(int direction)
            {
                inspectPage = Mathf.Max(0, inspectPage + direction);
                WingRegistry wing = Wing();
                if (wing != null) RefreshWingPage(wing);
            }

            /// <summary>Clamp page to current count and return its starting index.</summary>
            public int Refresh(int count)
            {
                int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)SquadronRowsPerPage));
                inspectPage = Mathf.Clamp(inspectPage, 0, pages - 1);

                if (label != null)
                    label.text = PageSummary(count, inspectPage, pages, "PILOT", "PILOTS");

                prev?.SetEnabled(inspectPage > 0);
                next?.SetEnabled(inspectPage < pages - 1);

                return inspectPage * SquadronRowsPerPage;
            }
        }

        private static void SyncPilotRows(List<PilotRow> rows, RectTransform area)
        {
            if (area == null) return;
            while (rows.Count < SquadronRowsPerPage) rows.Add(new PilotRow(area, rows.Count));
        }

        /// <summary>Pilot record row with rank styling and loss marking. Clicking inspects the person
        /// without assigning them, including lost pilots.</summary>
        private sealed class PilotRow
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly Image selectionRule;
            private readonly Image rankRail;
            private readonly Image kiaOverlay;
            private readonly TMP_Text slot, name, detail;
            private readonly WingButton hit;

            public PilotRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("PilotRow" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());
                rankRail = Rule(rt, new Rect(width - 6f, 0f, 3f, RowHeight), Dim());

                // Place the loss wash above row fill but below readable labels.
                kiaOverlay = Rule(rt, new Rect(0f, 0f, width, RowHeight),
                                  new Color(Alert().r, Alert().g, Alert().b, 0.24f));
                kiaOverlay.gameObject.SetActive(false);

                hit = HitButton(rt, new Rect(0f, 0f, width, RowHeight), null);

                slot = Label(rt, "", new Rect(6f, 0f, 20f, RowHeight), Dim(), FontBody,
                             FontStyles.Bold, TextAlignmentOptions.Left);
                name = Label(rt, "", new Rect(30f, 0f, 118f, RowHeight), WingColor(), FontBody,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, "", new Rect(152f, 0f, width - 152f - 18f, RowHeight), Dim(),
                               FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);

                go.SetActive(false);
            }

            public void Bind(WingPilot pilot, bool selected, Action onPick)
            {
                if (!go.activeSelf) go.SetActive(true);

                bool kia = pilot.Lost;
                selectionRule.color = selected ? Green() : MemberFrameColor();
                rankRail.color = kia ? Alert() : RankColor(pilot.Rank);
                hit.SetAction(onPick);
                hit.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);

                slot.text = kia ? "†" : RankBadgeText(pilot.Rank);
                slot.color = kia ? Alert() : RankColor(pilot.Rank);
                name.text = AvTheme.Truncate(pilot.Callsign, 12);
                name.color = kia ? Alert() : selected ? Green() : Friendly();
                detail.text = kia ? "KIA" : WingPilotRoster.RankName(pilot.Rank);
                detail.color = kia ? Alert() : Dim();

                kiaOverlay.gameObject.SetActive(kia);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }

        /// <summary>Per-control arm/confirm state bound to subject and timeout. Changing subject disarms;
        /// RTB and reserve release do not share confirmation.</summary>
        private sealed class Confirmation
        {
            private const float ArmSeconds = 3f;

            private object subject;
            private float until;

            public bool IsArmedFor(object candidate) =>
                candidate != null && ReferenceEquals(subject, candidate) &&
                Time.unscaledTime <= until;

            public void Arm(object candidate)
            {
                subject = candidate;
                until = Time.unscaledTime + ArmSeconds;
            }

            public void Clear() => subject = null;
        }

        private static WingRegistry Wing() => WingCommandManager.Instance?.Wing;

        // Local aliases for shared WingUi widgets also used by recovery prompts.

        private static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                      Color color, float size, FontStyles style,
                                      TextAlignmentOptions align) =>
            WingUi.Label(parent, text, rect, color, size, style, align);

        private static Image Panel(RectTransform parent, Rect rect, Color color) =>
            WingUi.Panel(parent, rect, color);

        private static Image AddSprite(RectTransform parent, string name, Sprite sprite,
                                       Rect rect, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Image[] Outline(RectTransform parent, Rect rect, Color color) =>
            WingUi.Outline(parent, rect, color);

        private static Image Rule(RectTransform parent, Rect rect, Color color) =>
            WingUi.Rule(parent, rect, color);

        private static WingButton Button(RectTransform parent, string text, Rect rect, Action onClick) =>
            WingUi.Button(parent, text, rect, onClick);

        private static WingButton HitButton(RectTransform parent, Rect rect, Action onClick) =>
            WingUi.HitButton(parent, rect, onClick);

        private static void Place(RectTransform rt, Rect rect) => WingUi.Place(rt, rect);

        private static void Stretch(RectTransform rt) => WingUi.Stretch(rt);

        private static TMP_FontAsset FindFont(MFDScreen template)
        {
            TMP_Text any = template.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (any != null) return any.font;

            TMP_Text anywhere = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            return anywhere != null ? anywhere.font : null;
        }

        private static TextMeshProUGUI FindLabel(Button button)
        {
            return button == null
                ? null
                : button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        }

        /// <summary>Find highlight by matching a working bezel button path, then fall back to a non-Button
        /// image.</summary>
        private static Image FindHighlight(Button button, MFDScreen template)
        {
            if (button == null) return null;

            if (template != null && template.highlight != null)
            {
                string path = PathUnder(template.highlight.transform, out Transform root);
                if (root != null && !string.IsNullOrEmpty(path))
                {
                    Transform found = button.transform.Find(path);
                    if (found != null)
                    {
                        Image img = found.GetComponent<Image>();
                        if (img != null) return img;
                    }
                }
            }

            foreach (Image img in button.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (img.gameObject != button.gameObject) return img;
            }
            return button.GetComponent<Image>();
        }

        private static string PathUnder(Transform t, out Transform root)
        {
            root = null;
            var parts = new List<string>();

            Transform cursor = t;
            while (cursor != null && cursor.GetComponent<Button>() == null)
            {
                parts.Insert(0, cursor.name);
                cursor = cursor.parent;
            }

            root = cursor;
            return string.Join("/", parts.ToArray());
        }

        // Panel styling.

        private static Color Green() => WingUi.Green;

        private static Color Warning() => WingUi.Warning;

        private static Color Alert() => WingUi.Alert;

        private static Color Friendly() => WingUi.TextPrimary;

        private static Color WingColor() => WingMarkers.MemberColor;

        private static Color Dim() => WingUi.Dim;

        private static Color RowColor() => WingUi.Grey;
        private static Color MemberFrameColor() => WingColor().WithAlpha(0.58f);
        private static Color FrameColor() => WingUi.FrameColor;

        /// <summary>Map pilot rank to shared theme accents, increasing warmth and emphasis with
        /// seniority.</summary>
        private static Color RankColor(WingRank rank)
        {
            switch (rank)
            {
                case WingRank.Wingman: return AvTheme.RailReady;
                case WingRank.Veteran: return AvTheme.RailInfo;
                case WingRank.Ace:     return AvTheme.RailCaution;
                case WingRank.Legend:  return AvTheme.Unity(AvTokens.TextPrimary);
                default:               return AvTheme.Dim;
            }
        }

        /// <summary>Single-letter pilot rank marker.</summary>
        private static string RankBadgeText(WingRank rank)
        {
            switch (rank)
            {
                case WingRank.Wingman: return "W";
                case WingRank.Veteran: return "V";
                case WingRank.Ace:     return "A";
                case WingRank.Legend:  return "L";
                default:               return "R";
            }
        }

        /// <summary>Member flown by this pilot, or null while unassigned.</summary>
        private static WingMember FlyingMember(WingRegistry wing, WingPilot pilot)
        {
            if (wing == null || pilot == null) return null;
            for (int i = 0; i < wing.Count; i++)
            {
                WingMember member = wing.Members[i];
                if (member.Crew == pilot) return member;
            }
            return null;
        }

        // Display text.

        /// <summary>Round and group numeric readouts for funds, prices, and mass.</summary>
        private static string Grouped(float amount) =>
            Mathf.RoundToInt(amount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);


        /// <summary>Compact order status with designation where useful, distinguishing distributed
        /// attacks.</summary>
        private static string ShortOrder(WingMember m)
        {
            // Display active override behaviour before the retained order.
            string behaviour = WingBehaviourLabels.Label(m.Behaviour.BehaviourId);
            if (behaviour != null) return behaviour;

            // Keep Splash's distinct order label; the map already identifies its target and the column
            // cannot fit both.
            if (m.Order == WingOrder.FireForEffect)
                return WingOrderCatalog.ShortLabel(m.Order);

            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
                return AvTheme.Truncate(assigned.definition != null ? assigned.definition.code : assigned.unitName, 8);

            return WingOrderCatalog.ShortLabel(m.Order);
        }
    }
}
