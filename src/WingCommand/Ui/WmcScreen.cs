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
    /// <summary>
    /// "WMC" — a native MFD screen on the maximised map, alongside BDF / MAP / HUD.
    ///
    /// The game's bezel columns each carry six buttons but only three configured screens,
    /// so the fourth slot is free. Registering an <see cref="MFDScreen"/> there and calling
    /// <c>VirtualMFD.SetupButtons()</c> lights the button up and labels it, and the game
    /// then drives show/hide exactly as it does for its own screens.
    ///
    /// The panel is built from scratch rather than cloned: the stock HUD OPTIONS hierarchy
    /// is not something this mod can safely dissect, whereas building known widgets and
    /// borrowing only the font and theme colours produces a predictable result that still
    /// matches the game's look.
    /// </summary>
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

        /// <summary>Height of a single-line label block: hint lines, status lines, readouts.</summary>
        private const float LineHeight = Space4;

        private const float GutterWidth = 62f;
        private const float ArrowWidth = 34f;

        private const float StatusStripHeight = AvTokens.StatusStripHeight;
        private const string HoverPrompt = "Hover a control to see what it does.";

        /// <summary>
        /// Roster rows visible at once, on every page that lists the flight.
        ///
        /// Three, matching the default MaxWingSize, rather than the four this used to
        /// reserve. With four tabs sharing one bezel the page has to earn its height back
        /// somewhere, and a permanently empty fourth row was the cheapest 32 pixels
        /// available. A larger configured wing still pages.
        /// </summary>
        private const int RosterRowsPerPage = 3;
        private const int SquadronRowsPerPage = 9;

        private enum Page
        {
            Tactical,
            Supply,
            Loadout,
            Wing,
        }

        private const int PageCount = 4;

        private static MFDScreen screen;
        private static bool tacticalPauseActive;

        // Indexed by Page, so adding a tab is a matter of building one more root rather
        // than adding a third parallel set of fields to every lifecycle method.
        private static readonly RectTransform[] pageRoots = new RectTransform[PageCount];
        private static readonly WingButton[] pageTabs = new WingButton[PageCount];
        private static readonly float[] pageHeights = new float[PageCount];

        /// <summary>Each page's status line, which doubles as its tooltip strip.</summary>
        private static readonly TMP_Text[] statusLabels = new TMP_Text[PageCount];

        /// <summary>
        /// The height every page is drawn at.
        ///
        /// One height for all four, not each page sized to its own content. Sizing them
        /// individually meant the panel grew and shrank under the tab strip every time you
        /// changed page — Supply and Tactical differ by nearly a third of the panel — so
        /// the control you were reaching for moved before you got to it, and the whole
        /// bezel appeared to twitch on every tab press. A stable frame is worth more than
        /// the whitespace it costs the shorter pages.
        /// </summary>
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
        private static readonly WingButton[] preferenceButtons =
            new WingButton[WingWeaponPreferences.All.Length];


        /// <summary>
        /// The pilot the Wing tab is inspecting.
        ///
        /// Pilot-centric rather than aircraft-centric now that the Wing tab lists the whole
        /// squadron, including people who are not currently flying. The airframe dossier on
        /// that page is whatever the inspected pilot is flying, if anything. Separate from
        /// the command selection the Tactical page uses, so inspecting a pilot never changes
        /// who the next order goes to.
        /// </summary>
        private static WingPilot inspectPilot;

        /// <summary>
        /// Which page of the flight the Loadout and Wing tabs are showing.
        ///
        /// Shared by both, and separate from the Tactical page's own cursor: those two
        /// inspect one aircraft at a time and should not jump about because the command
        /// page happened to be scrolled somewhere else.
        /// </summary>
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

        /// <summary>Map icon clicks become command selection only on the active WMC page.</summary>
        public static bool TacticalCommandModeActive =>
            screen != null && screen.isActive && page == Page.Tactical;

        // ------------------------------------------------------------------- lifecycle

        /// <summary>
        /// Called each frame from the manager. Installs lazily rather than patching
        /// <c>VirtualMFD.Start</c>, so it does not depend on plugin/scene ordering.
        /// </summary>
        public static void Tick(WingRegistry wing)
        {
            if (gaveUp || !GameAccess.MfdAvailable || !Plugin.Settings.UseMfdPanel.Value) return;

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!screen.isActive)
            {
                if (tacticalPauseActive)
                {
                    tacticalPauseActive = false;
                    if (Time.timeScale < 0.5f && Time.timeScale > 0f)
                    {
                        Time.timeScale = 1f;
                    }
                }

                // The screen can be closed with a list open or a name half typed — the bezel
                // button does not ask this code first. Neither may survive into a panel the
                // player cannot see: an open popup would still be holding the pointer, and a
                // focused field would still be holding the keyboard off the aircraft.
                if (WingKeyboardGuard.Captured)
                {
                    WingKeyboardGuard.Defocus();
                    WingKeyboardGuard.ForceRelease();
                }
                AvKit.Popup.CloseAny();
                return;
            }

            if (Plugin.Settings.TacticalPauseInSingleplayer.Value && GameManager.gameState == GameState.SinglePlayer)
            {
                if (!tacticalPauseActive && Time.timeScale > 0.5f)
                {
                    tacticalPauseActive = true;
                    Time.timeScale = 0.25f;
                }
            }

            // The status strip is the one part of the panel that answers the pointer, and a
            // fifth of a second is plainly visible as lag on something that should feel
            // attached to the cursor. Rather than repaint the whole page every frame, watch
            // for the hovered control changing and bring the next refresh forward when it
            // does — which covers arriving on a control and leaving one equally.
            string tooltip = WingButton.HoveredTooltip;
            if (!ReferenceEquals(tooltip, lastTooltip))
            {
                lastTooltip = tooltip;
                nextRefresh = 0f;
            }

            // Refreshing rebuilds a formatted string per roster row; at frame rate that is
            // pure garbage for numbers a reader cannot follow that fast.
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingBrain.Interval(0.2f);
                Refresh(wing);
            }
        }

        /// <summary>Forget the screen when the mission ends; a new one is built next time.</summary>
        public static void Reset()
        {
            if (tacticalPauseActive)
            {
                tacticalPauseActive = false;
                if (Time.timeScale < 0.5f && Time.timeScale > 0f)
                {
                    Time.timeScale = 1f;
                }
            }

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
            maneuverButtons = null;
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

            for (int i = 0; i < preferenceButtons.Length; i++) preferenceButtons[i] = null;

            pylonRows.Clear();
            airframeTiles.Clear();
            airframePrevButton = null;
            airframeNextButton = null;
            airframePageLabel = null;
            loadoutStatusLabel = null;
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
            shopTemplateButton = null;
            editingTemplateId = null;
            pylonPage = 0;
            inspectPage = 0;

            // The rename field may have been focused when the mission ended, and a field
            // destroyed while focused never fires the deselect that gives the keyboard back.
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
                Plugin.Logger.LogInfo("WMC screen installed on " + (left ? "left" : "right") +
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

        // --------------------------------------------------------------------- building

        private static MFDScreen Build(MFDScreen template, Button bezelButton)
        {
            WingUi.Font = FindFont(template);

            var root = new GameObject("WMC_Screen", typeof(RectTransform), typeof(Image));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(template.transform.parent, worldPositionStays: false);

            // Inherit placement from a working screen so the panel lands where the game
            // expects, then let VirtualMFD drive localPosition for show/hide.
            // Anchors and scale come from a working stock screen; position does not.
            // VirtualMFD.showPos is Vector3.zero and MFDScreen.ShowScreen assigns it straight
            // to localPosition, so a screen has no remembered home — it is placed by its
            // parent and its anchors, and any anchoredPosition written here is overwritten
            // the next time the panel is opened. MfdPanelDock reparents this screen into the
            // left column, which is placement vanilla will not undo.
            var templateRt = (RectTransform)template.transform;
            rt.anchorMin = templateRt.anchorMin;
            rt.anchorMax = templateRt.anchorMax;
            rt.pivot = templateRt.pivot;
            rt.localScale = templateRt.localScale;

            Image bg = root.GetComponent<Image>();
            bg.sprite = WingUi.PanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.SetParent(rt, worldPositionStays: false);
            Stretch(contentRt);

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

            // Supply reads top to bottom in the order the questions are actually asked: what
            // can I afford and is there room, then buying one, then conscripting one that is
            // already flying, then the holdback knob that only matters once you care about
            // what the AI is doing with the rest of the stock.
            RectTransform supplyRoot = pageRoots[(int)Page.Supply];
            float supplyY = y;
            supplyY = AddSupplyStatus(supplyRoot, supplyY);
            supplyY = AddPilotSelection(supplyRoot, supplyY);
            supplyY = AddShop(supplyRoot, supplyY);
            supplyY = AddAssignment(supplyRoot, supplyY);

            float loadoutY = AddLoadoutPage(pageRoots[(int)Page.Loadout], y);
            float wingY = AddWingPage(pageRoots[(int)Page.Wing], y);

            // Each page's content, plus the room the pinned status strip needs under it.
            const float stripBlock = StatusStripHeight + Space2;
            pageHeights[(int)Page.Tactical] = Mathf.Abs(tacticalY) + stripBlock + Pad;
            pageHeights[(int)Page.Supply] = Mathf.Abs(supplyY) + stripBlock + Pad;
            pageHeights[(int)Page.Loadout] = Mathf.Abs(loadoutY) + stripBlock + Pad;
            pageHeights[(int)Page.Wing] = Mathf.Abs(wingY) + stripBlock + Pad;

            // One frame for all four pages. Unified to shared AvTokens.PanelHeight.
            panelHeight = AvTokens.PanelHeight;
            for (int i = 0; i < PageCount; i++)
                panelHeight = Mathf.Max(panelHeight, pageHeights[i]);

            // Placed only once the tallest page is known, so the strip lands in the same
            // spot on every tab rather than wherever that page's content happened to end.
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

        /// <summary>Centred green title over chip rail and rule, matching unified avionics contract.</summary>
        /// <summary>
        /// The hard top strip: a filled WMC tag, the flight's live state, and three chips.
        ///
        /// This replaces a centred title, a subtitle and a separate chip rail — three rows
        /// that between them said "WING COMMAND" twice. The id tag carries the panel's
        /// identity in one 30px row, and the space that buys goes to the metric strip
        /// below it, which is information the pilot actually reads.
        /// </summary>
        private static float AddTitle(RectTransform parent, float y)
        {
            float inner = PanelWidth - Pad * 2f;

            var bar = new Rect(Pad, y, inner, AvTokens.TitleBarHeight + 2f);
            dataBar = AvStyled.TopBar(parent, bar, "WMC", 3);
            y -= bar.height + Space2;

            // Funds and minimum flight fuel sit above the tabs because every page needs
            // them: they used to live inside Supply and Tactical respectively, so checking
            // one meant leaving the page you were working on.
            var metrics = new Rect(Pad, y, inner, 58f);
            AvStyled.Box(parent, metrics, "metrics");
            float half = inner * 0.5f;
            fundsMetric = AvStyled.MetricCell(parent, new Rect(Pad, y, half, 58f), "SQUADRON FUNDS", "CR");
            fuelMetric = AvStyled.MetricCell(parent, new Rect(Pad + half, y, half, 58f), "FLIGHT FUEL", "% MIN");
            Rule(parent, new Rect(Pad + half, y, 1f, 58f), WingUi.BorderSubtle);

            return y - 58f - Space2;
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

            // Deliberately not resized per page: the panel keeps one frame so that changing
            // tab does not move every control on the page below it.
            if (panelRect != null)
                panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);

            // The pointer is almost always on the tab that was just pressed, and that tab
            // is about to be covered by a different page's content.
            WingButton.ClearTooltip();

            // A popup belongs to the page under it. Left open across a tab change it would
            // come back on top of that page's controls the next time it was shown, with its
            // scrim still eating every click. Dropping focus first unwinds the keyboard
            // guard through the field's own deselect rather than the forced path.
            WingKeyboardGuard.Defocus();
            AvKit.Popup.CloseAny();

            // Leaving Tactical stops the map intercepting wing-icon clicks, so the icons
            // have to lose their command-selection bracket at the same moment.
            WingCommandManager manager = WingCommandManager.Instance;
            if (manager != null)
            {
                foreach (WingMember member in manager.Wing.Members)
                    WingMarkers.Repaint(member.Aircraft);
            }
        }

        /// <summary>
        /// Section heading with a rule running out to the right of it, which is how the
        /// stock panels separate their groups.
        /// </summary>
        /// <summary>
        /// A section heading, and the tick that ties it back to the spine.
        ///
        /// The spine plus a tick per section replaces the frame each block used to carry.
        /// Four hairlines around every group made a page of equally-weighted boxes with
        /// nothing standing out; one stroke down the page and a mark per section says the
        /// same thing about grouping and leaves the emphasis for what matters.
        /// </summary>
        private static float Heading(RectTransform parent, float y, string text)
        {
            AvStyled.SpineTick(parent, SpineX + 3f, y - 8f);
            return WingUi.Heading(parent, y, text, PanelWidth);
        }

        /// <summary>Where the spine sits: inside the panel frame, outside the content column.</summary>
        private const float SpineX = 5f;

        /// <summary>
        /// A value with an arrow on either side of it.
        ///
        /// The arrows are drawn quiet: they page through a list rather than doing anything,
        /// and at full accent weight they read as loudly as the choice they are scrolling.
        /// Both take the row's full height as their click target even though they are drawn
        /// inset, so the small visual arrow is not also a small thing to hit.
        /// </summary>
        private static WingButton[] Stepper(RectTransform parent, float x, float y, float w,
                                            out TMP_Text valueLabel,
                                            Action onPrev, Action onNext,
                                            string tooltip = null)
        {
            Panel(parent, new Rect(x, y, w, RowHeight), RowColor());

            // Inset by a pixel so the arrow's own frame does not double up on the box it
            // sits in, but still tall enough to be an easy thing to hit — the old arrows
            // were 26 by 24 inside a 30-pixel row and hard to land on.
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

        /// <summary>The dim row label in the left gutter of the engagement block.</summary>
        private static void Gutter(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, GutterWidth - Gap, RowHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);

        /// <summary>
        /// One column header on a list. Kept as data rather than five near-identical calls,
        /// so a header and the cell under it cannot drift apart by a pixel at a time.
        /// </summary>
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

        // Four columns, not seven. WPN duplicated the weapon row in the engagement block,
        // SLOT ERR was a formed-up indicator the WING tab already carries, and squeezing
        // both in clipped their two-word headers and drove the fuel/ammo readout into the
        // REL button. What is left is what a glance at the flight actually asks: who, doing
        // what, with how much fuel and ammo. Every header is one word so none of them wrap.
        private static readonly Column[] RosterColumns =
        {
            new Column("CALLSIGN", 26f, 108f),
            new Column("STATE", 138f, 86f),
            new Column("FUEL", 224f, 52f, rightAligned: true),
            new Column("AMMO", 280f, 40f, rightAligned: true),
        };

        /// <summary>The two-column header over the Wing tab's squadron list.</summary>
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

        /// <summary>A page-turn arrow at one end of a list's footer strip.</summary>
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

        /// <summary>
        /// One cell of the order grid. Carries the panel's body size now that the order
        /// names are single words — the ten-pixel type this used to need was a symptom of
        /// labels like "Deliver Cargo" fighting a third of the panel for room.
        /// </summary>
        private static WingButton GridButton(RectTransform parent, string text, float x, float y,
                                             float w, Action onClick) =>
            WingUi.Button(parent, text, new Rect(x, y, w, RowHeight), FontSmall, onClick);

        /// <summary>
        /// The panel's one feedback channel, given a place of its own on every page.
        ///
        /// Everything the panel says back to the player arrives here — what an armed point
        /// order is waiting for, what the current rules of engagement actually mean, and
        /// now what whichever control the pointer is resting on will do. It was ten-pixel
        /// grey text floating under the order grid with nothing marking it as a distinct
        /// region, which is a poor home for the only place the panel ever answers you.
        ///
        /// Two lines, because that is what the explanations need: most orders take a short
        /// sentence to say what they do and a second clause to say who they do it to, and
        /// a one-line strip was silently clipping the half that made them different from
        /// each other.
        /// </summary>
        /// <summary>
        /// Place the strip at a fixed distance from the bottom of the panel, the same on
        /// every page.
        ///
        /// Pinned rather than flowing after the page's content, because the panel is now
        /// one height for all four tabs and a strip that simply followed the last control
        /// would sit two thirds of the way down the shorter pages, floating in the middle
        /// of nothing. Pinned, the dead space that uniform sizing costs the short pages
        /// falls between the content and the strip, where it reads as margin — and the one
        /// line that answers the pointer is in the same place on every tab.
        /// </summary>
        private static void PinStatusStrip(RectTransform parent, float y, Page page)
        {
            float w = PanelWidth - Pad * 2f;

            TMP_Text label = AvStyled.StatusStrip(parent, new Rect(Pad, y, w, StatusStripHeight));

            statusLabels[(int)page] = label;
        }

        /// <summary>
        /// Put the hovered control's description on the page's status line, falling back to
        /// whatever that page normally reports.
        ///
        /// Called for every page on every refresh rather than only the one being drawn,
        /// because the tooltip is the only thing on the panel that changes without the
        /// player pressing anything.
        /// </summary>
        private static void RefreshStatusStrip(Page page, string fallback)
        {
            TMP_Text label = statusLabels[(int)page];
            if (label == null) return;

            string tooltip = WingButton.HoveredTooltip;
            bool hovering = !string.IsNullOrEmpty(tooltip);

            label.text = hovering ? "> " + tooltip : "> " + fallback;
            label.color = hovering ? WingUi.TextPrimary : Dim();
        }

        /// <summary>
        /// A note displayed across the whole of an empty list area. Switched on and off
        /// by the refresh when the list it belongs to is empty.
        /// </summary>
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

        /// <summary>A line of explanatory text under a heading, at the panel's hint weight.</summary>
        private static TMP_Text Hint(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

        private static float Triple(RectTransform parent, float y, float w,
                                    string leftText, string leftHint, Action leftAction,
                                    string middleText, string middleHint, Action middleAction,
                                    string rightText, string rightHint, Action rightAction)
        {
            GridButton(parent, leftText, Pad, y, w, leftAction).WithTooltip(leftHint);
            GridButton(parent, middleText, Pad + w + Gap, y, w, middleAction)
                .WithTooltip(middleHint);
            GridButton(parent, rightText, Pad + (w + Gap) * 2f, y, w, rightAction)
                .WithTooltip(rightHint);
            return y - (RowHeight + Gap);
        }

        /// <summary>
        /// The data bar and the two display metrics.
        ///
        /// Refreshed for every page rather than only the visible one, because these sit
        /// above the tab strip and stay on screen whichever page is showing.
        /// </summary>
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
                // The flight's *minimum* fuel, not its average: the wingman closest to bingo
                // is the one that decides when the flight has to turn for home.
                float lowest = 1f;
                bool any = false;
                if (wing != null)
                {
                    for (int i = 0; i < wing.Members.Count; i++)
                    {
                        float f = wing.Members[i].Fuel;
                        if (f <= 0f) continue;
                        if (!any || f < lowest) lowest = f;
                        any = true;
                    }
                }

                bool bingo = any && lowest <= WingTuning.BingoFuel;
                fuelMetric.Set(
                    any ? Mathf.RoundToInt(lowest * 100f).ToString() : "--",
                    any ? "BINGO AT " + Mathf.RoundToInt(WingTuning.BingoFuel * 100f) + "%" : "NO FLIGHT",
                    any ? lowest : 0f,
                    bingo ? WingUi.Alert : any ? WingUi.RailEmerald : WingUi.Disabled);
            }
        }

        // -------------------------------------------------------------------- refreshing

        /// <summary>
        /// Repaint the page the player is actually looking at.
        ///
        /// Only the visible page is refreshed. With two tabs that distinction was academic;
        /// with four it is not, because rebuilding the requisition catalogue walks the
        /// faction's whole supply dictionary and would otherwise be paid five times a second
        /// while the player was reading the flight roster.
        /// </summary>
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
                    // Refreshed after the catalogue, because selecting or exhausting a shop
                    // row can change which reserve action is valid for the current airframe.
                    RefreshReserve();
                    RefreshStatusStrip(Page.Supply, HoverPrompt);
                    break;

                case Page.Loadout:
                    RefreshLoadoutPage();
                    RefreshStatusStrip(Page.Loadout, HoverPrompt);
                    break;

                case Page.Wing:
                    RefreshWingPage(wing);
                    RefreshStatusStrip(Page.Wing, HoverPrompt);
                    break;

                default:
                    RefreshTactical(wing);
                    break;
            }
        }

        // ------------------------------------------------------- shared roster plumbing

        private static AircraftDefinition DefinitionOf(WingMember member) =>
            member != null && member.Aircraft != null ? member.Aircraft.definition : null;

        /// <summary>
        /// The <c>&lt;</c> page <c>&gt;</c> strip under the squadron list.
        ///
        /// A roster of pilots is larger than the three rows a page shows, so without this
        /// the rest of the squadron would be hidden with nothing on screen to say so. Both
        /// arrows go dead on a single page rather than looking available and doing nothing.
        /// </summary>
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

            private static void Turn(int direction) =>
                inspectPage = Mathf.Max(0, inspectPage + direction);

            /// <summary>Clamp against the live list and return the first visible index.</summary>
            public int Refresh(int count)
            {
                int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)SquadronRowsPerPage));
                inspectPage = Mathf.Clamp(inspectPage, 0, pages - 1);

                // Nothing to page through reads better as a blank strip than as
                // "page 1 of 1"; a single page keeps the count but drops the arrows.
                if (label != null)
                    label.text = count == 0
                        ? ""
                        : pages == 1
                            ? count + (count == 1 ? " pilot" : " pilots")
                            : "squadron page " + (inspectPage + 1) + " of " + pages;

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

        /// <summary>
        /// One row of the squadron list.
        ///
        /// Unlike the Tactical page's <see cref="RosterRow"/> this lists people, not
        /// aircraft, and its state signal is a colour rail rather than a second column: rank
        /// drives the left badge and right rail, and a lost pilot's whole row washes red with
        /// a KIA mark so the widow of a five-strong squadron is unmissable. Clicking a row
        /// inspects it; selecting a lost pilot only shows their record.
        /// </summary>
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

                // A red wash that sits over the fill (so it reads as a mark) but under the
                // labels (so the record stays readable). Shown only for lost pilots.
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

        /// <summary>
        /// Press once to arm, press again to confirm — the panel's one idiom for a control
        /// that cannot be taken back.
        ///
        /// Held per control rather than globally, so arming the roster's REL does not also
        /// arm the reserve's RELEASE. The subject is carried alongside the timer because
        /// what was armed matters as much as when: selecting a different airframe between
        /// the two presses has to disarm, or the confirmation belongs to something the
        /// player is no longer looking at.
        /// </summary>
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

        // The widgets themselves live in WingUi, which is also where the aircraft-recovery
        // prompt draws from. These are the page's local names for them.

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

        /// <summary>
        /// Locate the button's highlight image by mirroring the path a working screen uses
        /// on its own button, falling back to any non-Button image on the target.
        /// </summary>
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

        // ------------------------------------------------------------------- styling

        private static Color Green() => WingUi.Green;

        private static Color Warning() => WingUi.Warning;

        private static Color Alert() => WingUi.Alert;

        private static Color Friendly() => WingUi.Friendly;

        private static Color WingColor() => WingMarkers.MemberColor;

        private static Color Dim() => WingUi.Dim;

        private static Color RowColor() => WingUi.Grey;
        private static Color MemberFrameColor() => WingColor().WithAlpha(0.58f);
        private static Color FrameColor() => WingUi.FrameColor;

        /// <summary>
        /// A rank's accent: the greener and more senior the pilot, the warmer and brighter
        /// the marker gets. Veterans and aces use the two accents the stock HUD already has a
        /// place for rather than inventing a new colour family, so the panel stays on the
        /// game's palette.
        /// </summary>
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

        /// <summary>The one-letter mark put in a pilot's rank slot.</summary>
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

        /// <summary>The wingman flying this pilot, or null when they are on the ground.</summary>
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

        // ----------------------------------------------------------------------- text

        /// <summary>
        /// A round figure with thousands separators: funds, prices, kilograms.
        ///
        /// Four- and five-digit numbers are common on this panel — a mid-game funds balance,
        /// an airframe price, a loadout's mass — and "20640" is read a digit at a time where
        /// "20,640" is read at a glance.
        /// </summary>
        private static string Grouped(float amount) =>
            Mathf.RoundToInt(amount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);


        /// <summary>
        /// The order column, which names the target when there is one.
        ///
        /// With targets distributed across the wing, "ENGAGE" on four rows says nothing
        /// about who went after what. The target's own designation is the useful thing to
        /// read here, and it pairs with the amber marks on the map and HUD.
        /// </summary>
        private static string ShortOrder(WingMember m)
        {
            // What it is actually doing outranks what it was told to do. Null means it is
            // flying the order, so fall through to naming that.
            string behaviour = WingBehaviourLabels.Label(m.Behaviour.BehaviourId);
            if (behaviour != null) return behaviour;

            // Splash 'Em keeps its own name rather than borrowing the target's. The
            // column is too narrow for both, and which of the two target orders a wingman
            // is flying is the thing that cannot be read anywhere else on this page - the
            // map already draws an amber line to the unit either way.
            if (m.Order == WingOrder.FireForEffect)
                return WingOrderCatalog.ShortLabel(m.Order);

            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
                return AvTheme.Truncate(assigned.definition != null ? assigned.definition.code : assigned.unitName, 8);

            return WingOrderCatalog.ShortLabel(m.Order);
        }
    }
}
