using System;
using System.Collections.Generic;
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
    internal static class WmcScreen
    {
        private const float PanelWidth = 430f;

        // The panel's measurements are WingUi's, not its own. They were duplicated here as
        // literals, which is how the page ended up laid out on 2/6/10/16/18/20/22/26/34
        // as well as the 4/12/30 it thought it was using — a block that looked deliberately
        // separated from the one above it and a block that had simply been nudged were
        // indistinguishable in the source and on screen.
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
        private const float FontTitle = WingUi.FontTitle;

        /// <summary>Height of a single-line label block: hint lines, status lines, readouts.</summary>
        private const float LineHeight = Space4;

        /// <summary>
        /// The left column the engagement and fit rows hang their names in.
        ///
        /// Both blocks used a local <c>gutter</c> constant of their own, and they agreed by
        /// luck rather than by construction.
        /// </summary>
        private const float GutterWidth = 62f;

        /// <summary>Width of a page-turn arrow, on every list that has one.</summary>
        private const float ArrowWidth = 34f;

        /// <summary>Two lines of hint text plus the padding around them.</summary>
        private const float StatusStripHeight = 38f;

        /// <summary>Shown on a status line when the pointer is not explaining anything.</summary>
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

        private enum Page
        {
            Tactical,
            Supply,
            Loadout,
            Wing,
        }

        private const int PageCount = 4;

        private static MFDScreen screen;

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
        private static WingButton rosterPrevButton;
        private static WingButton rosterNextButton;
        private static TMP_Text shapeLabel;
        private static TMP_Text summaryLabel;
        private static TMP_Text rosterPageLabel;
        private static TMP_Text commandStatusLabel;
        private static WingButton holdButton;
        private static WingButton escortButton;
        private static WingButton freeButton;
        private static WingButton cargoButton;
        private static WingButton landButton;
        private static readonly WingButton[] preferenceButtons =
            new WingButton[WingWeaponPreferences.All.Length];

        // --- Loadout page ---
        //
        // The page is a template editor now, not a picker. Nothing on it reports the flight:
        // what a wingman in the air is carrying is fixed and is reported on WING, and a
        // control the player cannot act on took a third of a page that now has pylons to
        // draw.
        private static TMP_Text loadoutAirframeLabel;
        private static TMP_Text loadoutStatusLabel;
        private static TMP_Text templateLabel;
        private static TMP_InputField templateNameField;
        private static TMP_Text templateSummaryLabel;
        private static WingButton templateSelectButton;
        private static WingButton templateNewButton;
        private static WingButton templateCopyButton;
        private static WingButton templateDeleteButton;
        private static RectTransform pylonArea;
        private static WingButton pylonPrevButton;
        private static WingButton pylonNextButton;
        private static TMP_Text pylonPageLabel;
        private static readonly List<PylonRow> pylonRows = new List<PylonRow>();

        /// <summary>The list the popup is currently showing, rebuilt on each open.</summary>
        private static readonly List<WingUi.PopupEntry> popupEntries =
            new List<WingUi.PopupEntry>();

        private static readonly List<WingLoadoutCatalog.StoreOption> storeScratch =
            new List<WingLoadoutCatalog.StoreOption>();

        private static WingUi.Popup loadoutPopup;
        private static WingUi.Popup shopTemplatePopup;

        /// <summary>
        /// Which template the editor is working on, by id.
        ///
        /// An id rather than the record, because the record can be deleted from underneath
        /// this — by the delete button, or by a config edit between missions — and a stale
        /// object reference would keep an editor open on a template that no longer exists.
        /// </summary>
        private static string editingTemplateId;

        /// <summary>Which page of the airframe's pylons the editor is showing.</summary>
        private static int pylonPage;

        /// <summary>
        /// Pylons drawn at once. Four rather than the roster's three: this list is the point
        /// of the page, and most airframes fit in one or two pages of four.
        /// </summary>
        private const int PylonRowsPerPage = 4;

        // --- Wing page ---
        private static readonly List<PickRow> wingRows = new List<PickRow>();
        private static RectTransform wingRosterArea;
        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static TMP_Text pilotStatsLabel;
        private static TMP_Text pilotPersonaLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;
        private static TMP_Text airframeTypeLabel;
        private static TMP_Text airframeStateLabel;
        private static TMP_Text airframeOrderLabel;
        private static TMP_Text airframeLoadoutLabel;
        private static TMP_Text airframeWeaponsLabel;

        /// <summary>
        /// The wingman the Loadout and Wing tabs are inspecting.
        ///
        /// Deliberately separate from the command selection: those two pages are about one
        /// aircraft at a time, and borrowing the command scope for them would mean opening
        /// the Wing tab silently changed who the next order went to.
        /// </summary>
        private static WingMember focusMember;

        /// <summary>
        /// Which page of the flight the Loadout and Wing tabs are showing.
        ///
        /// Shared by both, and separate from the Tactical page's own cursor: those two
        /// inspect one aircraft at a time and should not jump about because the command
        /// page happened to be scrolled somewhere else.
        /// </summary>
        private static int inspectPage;

        private static RosterPager wingPager;

        private static readonly List<RosterRow> rosterRows = new List<RosterRow>();
        private static readonly List<ShopRow> shopRows = new List<ShopRow>();
        private static RectTransform shopArea;
        private static TMP_Text supplyFundsLabel;
        private static TMP_Text supplySquadronLabel;
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
        private static WingButton requisitionButton;
        private static AircraftDefinition selectedOffer;
        private static int shopPage;
        private static int rosterPage;

        /// <summary>
        /// Rows built for the shop. Four rather than six: the panel is sized to its content,
        /// and a page padded out with empty slots leaves a hole in the middle of the layout
        /// whenever the faction stocks fewer types than that.
        /// </summary>
        private const int ShopRows = 4;

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
            if (gaveUp || !GameAccess.MfdAvailable || !Plugin.Config2.UseMfdPanel.Value) return;

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!screen.isActive)
            {
                // The screen can be closed with a list open or a name half typed — the bezel
                // button does not ask this code first. Neither may survive into a panel the
                // player cannot see: an open popup would still be holding the pointer, and a
                // focused field would still be holding the keyboard off the aircraft.
                if (WingKeyboardGuard.Captured)
                {
                    WingKeyboardGuard.Defocus();
                    WingKeyboardGuard.ForceRelease();
                }
                WingUi.Popup.CloseAny();
                return;
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
                nextRefresh = Time.unscaledTime + 0.2f;
                Refresh(wing);
            }
        }

        /// <summary>Forget the screen when the mission ends; a new one is built next time.</summary>
        public static void Reset()
        {
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
            rosterPrevButton = null;
            rosterNextButton = null;
            shapeLabel = null;
            summaryLabel = null;
            rosterPageLabel = null;
            commandStatusLabel = null;
            rosterRows.Clear();
            shopRows.Clear();
            shopArea = null;
            supplyFundsLabel = null;
            supplySquadronLabel = null;
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
            requisitionButton = null;
            selectedOffer = null;
            shopPage = 0;
            rosterPage = 0;
            holdButton = null;
            escortButton = null;
            freeButton = null;
            cargoButton = null;
            landButton = null;

            for (int i = 0; i < preferenceButtons.Length; i++) preferenceButtons[i] = null;

            pylonRows.Clear();
            loadoutAirframeLabel = null;
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
            loadoutPopup = null;
            shopTemplatePopup = null;
            shopTemplateButton = null;
            editingTemplateId = null;
            pylonPage = 0;
            wingPager = null;
            inspectPage = 0;

            // The rename field may have been focused when the mission ended, and a field
            // destroyed while focused never fires the deselect that gives the keyboard back.
            WingKeyboardGuard.ForceRelease();

            wingRows.Clear();
            wingRosterArea = null;
            pilotIdentityLabel = null;
            pilotRankLabel = null;
            pilotStatsLabel = null;
            pilotPersonaLabel = null;
            pilotXpBar = null;
            pilotBackgroundLabel = null;
            airframeTypeLabel = null;
            airframeStateLabel = null;
            airframeOrderLabel = null;
            airframeLoadoutLabel = null;
            airframeWeaponsLabel = null;
            focusMember = null;

            lastTooltip = null;
            gaveUp = false;
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                List<Button> buttons = GameAccess.GetLeftButtons(mfd);
                List<MFDScreen> screens = GameAccess.GetLeftScreens(mfd);
                bool left = true;

                if (!TryClaimSlot(buttons, screens, out int slot))
                {
                    // Fall back to the right column if the left one is fully configured.
                    buttons = GameAccess.GetRightButtons(mfd);
                    screens = GameAccess.GetRightScreens(mfd);
                    left = false;

                    if (!TryClaimSlot(buttons, screens, out slot))
                    {
                        Fail("no free bezel button on either column");
                        return;
                    }
                }

                MFDScreen template = FindTemplate(screens) ??
                                     FindTemplate(GameAccess.GetLeftScreens(mfd)) ??
                                     FindTemplate(GameAccess.GetRightScreens(mfd));
                if (template == null) return;

                screen = Build(template, buttons[slot]);
                if (screen == null) return;

                // Free slots are null entries in the list, not indices past its end.
                while (screens.Count <= slot) screens.Add(null);
                screens[slot] = screen;

                mfd.SetupButtons();

                // SetupButtons only ever disables buttons — it never re-enables one it
                // turned off on the first pass, so the newly claimed button needs it back.
                Button bezel = buttons[slot];
                bezel.enabled = true;
                bezel.interactable = true;

                // An unused bezel button may have no handler wired in the scene at all.
                // If this one is bare, route it to the same method the configured ones use.
                if (bezel.onClick.GetPersistentEventCount() == 0)
                {
                    VirtualMFD owner = mfd;
                    bool onLeft = left;
                    bezel.onClick.AddListener(() =>
                    {
                        if (onLeft) owner.PressLeftButton(bezel);
                        else owner.PressRightButton(bezel);
                    });
                }

                // The game only shows bezel buttons while the map is up; match that.
                screen.CloseScreen(Screen.width * (left ? Vector3.left : Vector3.right));

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

        /// <summary>
        /// Find a bezel button with no screen behind it. The stock lists are the same
        /// length as the button lists, with unused entries left null — those are the "-"
        /// buttons the game disables during setup.
        /// </summary>
        private static bool TryClaimSlot(List<Button> buttons, List<MFDScreen> screens, out int slot)
        {
            slot = -1;
            if (buttons == null || screens == null) return false;

            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == null) continue;
                if (i >= screens.Count || screens[i] == null)
                {
                    slot = i;
                    return true;
                }
            }
            return false;
        }

        private static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            foreach (MFDScreen s in screens)
            {
                if (s != null && s.transform.parent != null) return s;
            }
            return null;
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
            var templateRt = (RectTransform)template.transform;
            rt.anchorMin = templateRt.anchorMin;
            rt.anchorMax = templateRt.anchorMax;
            rt.pivot = templateRt.pivot;
            rt.localScale = templateRt.localScale;
            rt.anchoredPosition = templateRt.anchoredPosition;

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
            supplyY = AddShop(supplyRoot, supplyY);
            supplyY = AddAssignment(supplyRoot, supplyY);
            supplyY = AddReserve(supplyRoot, supplyY);

            float loadoutY = AddLoadoutPage(pageRoots[(int)Page.Loadout], y);
            float wingY = AddWingPage(pageRoots[(int)Page.Wing], y);

            // Each page's content, plus the room the pinned status strip needs under it.
            const float stripBlock = StatusStripHeight + Space2;
            pageHeights[(int)Page.Tactical] = Mathf.Abs(tacticalY) + stripBlock + Pad;
            pageHeights[(int)Page.Supply] = Mathf.Abs(supplyY) + stripBlock + Pad;
            pageHeights[(int)Page.Loadout] = Mathf.Abs(loadoutY) + stripBlock + Pad;
            pageHeights[(int)Page.Wing] = Mathf.Abs(wingY) + stripBlock + Pad;

            // One frame for all four pages. See panelHeight.
            panelHeight = 0f;
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
            s.aircraftOnly = true;
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

        /// <summary>Centred green title over a rule, as on BOSCALI / TARGET SELECTION / HUD OPTIONS.</summary>
        private static float AddTitle(RectTransform parent, float y)
        {
            Label(parent, "WING COMMAND", new Rect(Pad, y, PanelWidth - Pad * 2f, Space6),
                  Green(), FontTitle, FontStyles.Normal, TextAlignmentOptions.Center);
            return y - Space6 - Space2;
        }

        /// <summary>
        /// One row of four, rather than the two rows a 2x2 grid would need.
        ///
        /// Four short words fit across the panel at this size, and a single row keeps the
        /// tabs where a two-tab player already expects them — the page below simply gains
        /// two more places to go rather than moving down the screen.
        ///
        /// The strip sits on a rule and the tabs are drawn in their own style: dim and flat
        /// when they are somewhere else you could go, filled and underlined onto that rule
        /// when they are the page you are on. Previously all four were ordinary buttons, so
        /// the only thing separating "the page I am reading" from "a page I could open" was
        /// a white frame — which is also exactly what the button under the mouse pointer
        /// looked like, on this strip and everywhere else on the panel.
        /// </summary>
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
            WingUi.Popup.CloseAny();

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
        private static float Heading(RectTransform parent, float y, string text) =>
            WingUi.Heading(parent, y, text, PanelWidth);

        /// <summary>
        /// The three standing choices that shape a fight, in one labelled block: what a
        /// wingman may shoot, which of its own weapons it reaches for, and where it sits.
        ///
        /// Grouped under one heading with a left gutter rather than given a heading each.
        /// Three headings and a hint line cost sixty pixels of a panel that now shares its
        /// bezel with four tabs, and they were labelling three rows that all answer the
        /// same question — how does this flight fight. The per-choice explanations moved to
        /// the status line at the foot of the page, where only the one being changed is
        /// shown and it has the width to be a sentence.
        /// </summary>
        private static float AddEngagementSection(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ENGAGEMENT");

            float left = Pad + GutterWidth;
            float w = PanelWidth - Pad - left;

            // Rules of engagement: three rungs, so three buttons. They are an escalation
            // rather than a toggle — each answers "the leader is being shot at" differently,
            // which is the whole reason there are three of them. Wing-wide.
            Gutter(parent, y, "ROE");
            float roeWidth = (w - Gap * 2f) / 3f;
            // Each rung explains itself from the same source the status line already used
            // for the standing hint, so the hovered description and the resting one cannot
            // drift apart.
            holdButton = Button(parent, "DEFEND", new Rect(left, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Hold))
                         .WithTooltip("DEFEND - " + RoeRules.Hint(WingRoe.Hold));
            escortButton = Button(parent, "ESCORT",
                                  new Rect(left + roeWidth + Gap, y, roeWidth, RowHeight),
                                  () => SetRoe(WingRoe.Escort))
                           .WithTooltip("ESCORT - " + RoeRules.Hint(WingRoe.Escort));
            freeButton = Button(parent, "FREE",
                                new Rect(left + (roeWidth + Gap) * 2f, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Free))
                         .WithTooltip("FREE - " + RoeRules.Hint(WingRoe.Free));
            y -= RowHeight + Gap;

            // Weapon preference. Unlike the two rows around it this one is scoped to the
            // current selection, which is what makes a mixed flight possible: two wingmen
            // holding their missiles for aircraft while the third works the ground.
            Gutter(parent, y, "WEAPON");
            float preferenceWidth = (w - Gap * (preferenceButtons.Length - 1)) / preferenceButtons.Length;
            for (int i = 0; i < preferenceButtons.Length; i++)
            {
                WingWeaponPreference preference = WingWeaponPreferences.All[i];
                preferenceButtons[i] = WingUi.Button(
                    parent, WingWeaponPreferences.Label(preference),
                    new Rect(left + (preferenceWidth + Gap) * i, y, preferenceWidth, RowHeight),
                    FontSmall,
                    () => WingCommandManager.Instance?.SetWeaponPreference(preference))
                    .WithTooltip(WingWeaponPreferences.Label(preference) + " - " +
                                 WingWeaponPreferences.Hint(preference) +
                                 " Applies to the selected wingmen only.");
            }
            y -= RowHeight + Gap;

            Gutter(parent, y, "FORM");
            Stepper(parent, left, y, w, out shapeLabel, () => CycleShape(-1), () => CycleShape(1),
                    OrderHint.Form);

            return y - (RowHeight + Gap);
        }

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

        private static void SetRoe(WingRoe roe)
        {
            WingRegistry wing = Wing();
            if (wing == null) return;

            wing.Roe = roe;
            WingCommandManager.Instance?.Toast("ROE: " + roe.ToString().ToUpperInvariant());
        }

        private static float AddSummary(RectTransform parent, float y)
        {
            // SELECT ALL is what this row is for — the label beside it only reports. It is
            // drawn as the row's primary action so it can be found without being read.
            const float actionWidth = 88f;
            summaryLabel = Label(parent, "",
                                 new Rect(Pad, y, PanelWidth - Pad * 2f - actionWidth - Space2,
                                          RowHeight),
                                 Friendly(), FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
            WingUi.Button(parent, "SELECT ALL",
                          new Rect(PanelWidth - Pad - actionWidth, y, actionWidth, RowHeight),
                          FontSmall, UiButtonStyle.Primary,
                          () => WingCommandManager.Instance?.SelectAllMembers())
                .WithTooltip(OrderHint.SelectAll);
            return y - RowHeight - Gap;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");

            // Column headers, so the numbers in each row are readable without guessing.
            float w = PanelWidth - Pad * 2f;
            y = ColumnHeaders(parent, y, RosterColumns);

            float h = RowPitch * RosterRowsPerPage;

            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, w, h));

            y -= h + Gap;

            rosterPrevButton = Pager(parent, y, "<", () => TurnRosterPage(-1));
            rosterPageLabel = PagerLabel(parent, y);
            rosterNextButton = Pager(parent, y, ">", () => TurnRosterPage(1));
            return y - RowHeight - Gap;
        }

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

        private static readonly Column[] RosterColumns =
        {
            new Column("CALLSIGN", 26f, 100f),
            new Column("STATE", 128f, 58f),
            new Column("WPN", 188f, 30f),
            new Column("SLOT ERR", 220f, 52f, rightAligned: true),
            new Column("FUEL  AMMO", 276f, 70f, rightAligned: true),
        };

        /// <summary>The two-column header the Loadout and Wing tabs' pick lists share.</summary>
        private static readonly Column[] InspectColumns =
        {
            new Column("CALLSIGN", 26f, 120f),
            new Column("CARRYING", 160f, PanelWidth - Pad * 2f - 160f - Space3,
                       rightAligned: true),
        };

        private static readonly Column[] WingInspectColumns =
        {
            new Column("CALLSIGN", 26f, 120f),
            new Column("STATE", 160f, PanelWidth - Pad * 2f - 160f - Space3, rightAligned: true),
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
        /// The nine scoped orders, in three columns.
        ///
        /// Two columns became three when Splash 'Em made the set nine. A fifth row of
        /// pairs would have cost this page more height than the two new tabs left it, where
        /// a three-by-three grid holds all nine in three rows and hands 34 pixels back. It
        /// reads better as well: rejoin and the two target orders, then the autonomous and
        /// positional ones, then the three that end a sortie.
        /// </summary>
        private static float AddActions(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ORDERS - SELECTED SCOPE");
            float w = (PanelWidth - Pad * 2f - Gap * 2f) / 3f;

            y = Triple(parent, y, w,
                "Form Up", OrderHint.Rejoin, () => Order(WingAction.Rejoin),
                "Attack", OrderHint.Attack, () => Order(WingAction.AttackMyTarget),
                "Splash 'Em", OrderHint.FireForEffect, () => Order(WingAction.FireForEffect));

            y = Triple(parent, y, w,
                "Engage", OrderHint.Engage, () => Order(WingAction.Engage),
                "Disengage", OrderHint.Disengage, () => Order(WingAction.FallBack),
                "Hold Here", OrderHint.HoldHere,
                () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.OrbitHere));

            GridButton(parent, "Return To Base", Pad, y, w,
                       () => Order(WingAction.ReturnToBase)).WithTooltip(OrderHint.ReturnToBase);

            // Deliver Cargo arms a drop point, and says on the status line that pressing it
            // again falls back to the stock supply route.
            cargoButton = GridButton(parent, "Deliver Cargo", Pad + w + Gap, y, w,
                                     () => WingCommandManager.Instance?.RequestCargoRun())
                          .WithTooltip(OrderHint.DeliverCargo);
            landButton = GridButton(parent, "Land Here", Pad + (w + Gap) * 2f, y, w,
                                    () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.LandHere))
                         .WithTooltip(OrderHint.LandHere);
            y -= RowHeight + Gap;

            return y;
        }

        /// <summary>
        /// What each control does, in the two lines the status strip has room for.
        ///
        /// Kept together rather than written at each call site: these are the panel's
        /// documentation, they have to stay consistent in voice and length, and several of
        /// them are the only place a distinction is ever explained — Attack versus Fire For
        /// Effect, or Disengage versus Return To Base, are not differences a four-word
        /// button label can carry.
        /// </summary>
        private static class OrderHint
        {
            public const string Rejoin =
                "FORM UP - break off and return to formation on the leader. Cancels any " +
                "attack, hold or route the selected wingmen are flying.";

            public const string Attack =
                "ATTACK - send the selection after the target you have locked. Targets are " +
                "shared out across the scope so several wingmen do not chase one contact.";

            public const string FireForEffect =
                "SPLASH 'EM - empty everything that will bear on your locked target. " +
                "Expends ordnance freely; use it to finish something, not to open on it.";

            public const string Engage =
                "ENGAGE - hunt independently within the rules of engagement. The wingman " +
                "picks its own targets and does not come back until told to.";

            public const string Disengage =
                "DISENGAGE - break contact and run for the nearest friendly base or ship, " +
                "defending itself on the way. Not a landing order.";

            public const string HoldHere =
                "HOLD HERE - then click the map. The selection orbits that point and " +
                "defends itself, but starts nothing.";

            public const string ReturnToBase =
                "RETURN TO BASE - fly home and land. The airframe and its fit go back into " +
                "the wing reserve, ready to be requisitioned again.";

            public const string DeliverCargo =
                "DELIVER CARGO - then click a drop point, or press again to use the stock " +
                "supply route. Only wingmen actually carrying a load can take this.";

            public const string LandHere =
                "LAND HERE - then click the map. Puts a rotary wingman on the ground at " +
                "that spot rather than routing it to an airbase.";

            public const string SelectAll =
                "Put every wingman in the command scope, so the next order goes to the " +
                "whole flight.";

            public const string Release =
                "Discharge this wingman from the wing for good. It flies home, gives its " +
                "airframe back and stops using a squadron slot. Press once to arm, again " +
                "to confirm.";

            public const string Roe =
                "Rules of engagement, wing-wide: how far a wingman may go on its own " +
                "initiative before it needs telling.";

            public const string Weapon =
                "Which weapons the selected wingmen reach for first. Scoped, so a mixed " +
                "flight can split between the air and the ground.";

            public const string Form =
                "The formation shape wingmen fly when they are formed up on the leader.";

            public const string Requisition =
                "Buy the selected airframe. It launches from a friendly base with the fit " +
                "chosen on LOADOUT and flies out to join the wing.";

            public const string Fit =
                "What the next one of these launches with: its standard fit, or one of the " +
                "templates you have built for it on LOADOUT.";

            public const string OverLimit =
                "Permission to requisition past the mission's AI aircraft cap, at a " +
                "surcharge. Changes nothing while the squadron still has room.";

            public const string AssignSelected =
                "Conscript the friendly AI aircraft selected on the map into your wing. " +
                "Press twice to confirm the fee.";

            public const string ReserveHold =
                "Take this airframe out of the faction pool and keep it for the wing, so " +
                "the AI cannot spend it.";

            public const string ReserveRelease =
                "Give this airframe back to the faction pool. Press once to arm, again to " +
                "confirm.";

            public const string Pager = "Show the rest of the list.";
        }

        private static void Order(WingAction action) =>
            WingCommandManager.Instance?.Execute(action, wholeWing: false);

        /// <summary>
        /// One cell of the order grid. Smaller type than the rest of the page, because a
        /// third of the panel has to hold the longest order name without clipping it.
        /// </summary>
        private static WingButton GridButton(RectTransform parent, string text, float x, float y,
                                             float w, Action onClick) =>
            WingUi.Button(parent, text, new Rect(x, y, w, RowHeight), FontMicro, onClick);

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

            Panel(parent, new Rect(Pad, y, w, StatusStripHeight), FrameColor());
            TMP_Text label = Label(parent, "",
                                   new Rect(Pad + Space2, y, w - Space4, StatusStripHeight),
                                   Dim(), FontMicro, FontStyles.Normal,
                                   TextAlignmentOptions.Left);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Truncate;

            statusLabels[(int)page] = label;
            if (page == Page.Tactical) commandStatusLabel = label;
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

            label.text = hovering ? tooltip : fallback;
            label.color = hovering ? Friendly() : Dim();
        }

        /// <summary>
        /// The three numbers that gate every control on this page, kept permanently on
        /// screen.
        ///
        /// The squadron count is the important one. A mission's AI aircraft limit is
        /// routinely zero once the player's own presence is subtracted from it, and until
        /// now the only sign of that was a toast that said "Squadron at capacity (0 of 0)"
        /// and then vanished — leaving a shop whose buttons did nothing for no visible
        /// reason.
        /// </summary>
        private static float AddSupplyStatus(RectTransform parent, float y)
        {
            float half = (PanelWidth - Pad * 2f) * 0.5f;

            supplyFundsLabel = Label(parent, "", new Rect(Pad, y, half, LineHeight),
                                     Friendly(), FontSmall, FontStyles.Normal,
                                     TextAlignmentOptions.Left);
            supplySquadronLabel = Label(parent, "", new Rect(Pad + half, y, half, LineHeight),
                                        Friendly(), FontSmall, FontStyles.Normal,
                                        TextAlignmentOptions.Right);
            return y - LineHeight - Space2;
        }

        private static float AddAssignment(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ACTIVE AIRCRAFT ASSIGNMENT");
            Hint(parent, y,
                 "Select friendly AI on the map, then press twice to confirm the fee.");
            y -= LineHeight + Space1;

            WingUi.Button(parent, "Assign Selected",
                          new Rect(Pad, y, PanelWidth - Pad * 2f, RowHeight),
                          FontBody, UiButtonStyle.Primary,
                          () => WingCommandManager.Instance?.AddSelectedFromMap())
                .WithTooltip(OrderHint.AssignSelected);
            return y - (RowHeight + Gap);
        }

        private static float AddReserve(RectTransform parent, float y)
        {
            y = Heading(parent, y, "WING RESERVE");

            const float actionWidth = 104f;

            // RELEASE hands an airframe back to the AI pool and cannot be undone from here;
            // HOLD only takes one out of it. Drawing them at the same weight, side by side,
            // either side of a counter is how the destructive one got pressed by mistake.
            reserveReleaseButton = WingUi.Button(
                parent, "RELEASE", new Rect(Pad, y, actionWidth, RowHeight),
                FontBody, UiButtonStyle.Danger, ReleaseSelectedReserve)
                .WithTooltip(OrderHint.ReserveRelease);
            reserveLabel = Label(
                parent, "",
                new Rect(Pad + actionWidth + Gap, y,
                         PanelWidth - Pad * 2f - (actionWidth + Gap) * 2f, RowHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            reserveHoldButton = WingUi.Button(
                parent, "HOLD",
                new Rect(PanelWidth - Pad - actionWidth, y, actionWidth, RowHeight),
                FontBody, UiButtonStyle.Primary, HoldSelectedReserve)
                .WithTooltip(OrderHint.ReserveHold);
            y -= RowHeight + Space1;

            reserveHintLabel = Label(parent, "",
                  new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Center);
            return y - LineHeight - Space2;
        }

        /// <summary>A line of explanatory text under a heading, at the panel's hint weight.</summary>
        private static TMP_Text Hint(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

        private static void HoldSelectedReserve()
        {
            bool held = WingSupplyReserve.Hold(selectedOffer, out string reason);
            WingCommandManager.Instance?.Toast(held
                ? selectedOffer.unitName + " held for the wing (" + WingSupplyReserve.Count +
                  "/" + WingSupplyReserve.Capacity + ")"
                : reason);
        }

        /// <summary>
        /// Hand an airframe back to the faction pool, on the second press.
        ///
        /// The same arm-then-confirm the roster's REL and the assignment fee use. Releasing
        /// is not undoable from this panel — the AI may spend the airframe the moment it is
        /// back in the pool — and it sat one button-width from HOLD, which does the
        /// opposite.
        /// </summary>
        private static void ReleaseSelectedReserve()
        {
            AircraftDefinition definition = selectedOffer;
            if (definition == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            if (!reserveRelease.IsArmedFor(definition))
            {
                reserveRelease.Arm(definition);
                WingCommandManager.Instance?.Toast(
                    "Press RELEASE again to give " + definition.unitName +
                    " back to faction stock");
                return;
            }

            reserveRelease.Clear();
            bool released = WingSupplyReserve.Release(
                definition, out bool wasOwned, out string reason);
            WingCommandManager.Instance?.Toast(released
                ? definition.unitName + (wasOwned ? " ownership released" : " released") +
                  " to faction stock"
                : reason);
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

        private static readonly Confirmation reserveRelease = new Confirmation();

        private static void TurnRosterPage(int direction)
        {
            rosterPage = Mathf.Max(0, rosterPage + direction);
        }

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

            switch (page)
            {
                case Page.Supply:
                    RefreshSupplyStatus();
                    RefreshShop();
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

        private static void RefreshTactical(WingRegistry wing)
        {
            WingCommandManager manager = WingCommandManager.Instance;

            if (shapeLabel != null)
                shapeLabel.text = FormationShapes.Pretty(Plugin.Config2.Shape.Value);

            if (summaryLabel != null)
                summaryLabel.text = "COMMAND: " + (manager?.Selection.Summary(wing) ?? "ALL") +
                                    "   ·   WING " + wing.Count + "/" + WingRegistry.WingLimitLabel;

            holdButton?.SetLatched(wing.Roe == WingRoe.Hold);
            escortButton?.SetLatched(wing.Roe == WingRoe.Escort);
            freeButton?.SetLatched(wing.Roe == WingRoe.Free);

            // A scope whose members disagree lights nothing, rather than lighting the first
            // one's choice and inviting the player to believe the whole scope shares it.
            WingWeaponPreference? shared = manager?.ScopeWeaponPreference();
            for (int i = 0; i < preferenceButtons.Length; i++)
                preferenceButtons[i]?.SetLatched(shared == WingWeaponPreferences.All[i]);

            if (manager != null)
            {
                List<WingMember> scope = manager.Commands.Scope(wholeWing: false);
                bool canCargo = false;
                bool canLand = false;
                foreach (WingMember member in scope)
                {
                    canCargo |= WingOrderCatalog.CanApply(member, WingOrder.DeliverCargo);
                    canLand |= WingOrderCatalog.CanApply(member, WingOrder.LandHere);
                }
                cargoButton?.SetEnabled(canCargo);
                landButton?.SetEnabled(canLand);
            }

            // The map has first claim on this line: an armed point order or a pending
            // assignment fee is a live instruction, and the engagement hints are not. A
            // hovered control outranks both, because it is the one the player is asking
            // about right now — RefreshStatusStrip resolves that.
            RefreshStatusStrip(Page.Tactical,
                manager != null && manager.MapStatusIsNotice
                    ? manager.MapStatus
                    : EngagementHint(wing, shared));

            RefreshRoster(wing);
        }

        /// <summary>
        /// What the two engagement rows currently mean, in one sentence.
        ///
        /// The rules of engagement line is the important half and comes first; the weapon
        /// preference is only mentioned when it is doing something, so an ordinary AUTO
        /// flight reads exactly as it did before this control existed.
        /// </summary>
        private static string EngagementHint(WingRegistry wing, WingWeaponPreference? shared)
        {
            string hint = RoeRules.Hint(wing.Roe);

            if (shared == null) return hint + "  ·  Weapon preference varies across the selection.";
            if (shared.Value == WingWeaponPreference.Auto) return hint;

            return hint + "  ·  " + WingWeaponPreferences.Hint(shared.Value);
        }

        private static void RefreshRoster(WingRegistry wing)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(wing.Count / (float)RosterRowsPerPage));
            rosterPage = Mathf.Clamp(rosterPage, 0, pages - 1);
            if (rosterPageLabel != null)
                rosterPageLabel.text = "flight page " + (rosterPage + 1) + " of " + pages;

            rosterPrevButton?.SetEnabled(rosterPage > 0);
            rosterNextButton?.SetEnabled(rosterPage < pages - 1);

            SyncRosterRows(RosterRowsPerPage);
            int first = rosterPage * RosterRowsPerPage;

            for (int i = 0; i < rosterRows.Count; i++)
            {
                int index = first + i;
                if (index < wing.Count) rosterRows[i].Bind(wing.Members[index]);
                else rosterRows[i].Hide();
            }
        }

        /// <summary>Keep the inspection focus on an aircraft that still exists.</summary>
        private static void PruneFocus(WingRegistry wing)
        {
            if (focusMember != null && wing.Contains(focusMember)) return;

            focusMember = wing.Count > 0 ? wing.Members[0] : null;
        }

        /// <summary>Refresh the concrete three-airframe wing reserve.</summary>
        private static void RefreshReserve()
        {
            if (reserveLabel == null) return;

            if (!WingSupplyReserve.HasFaction)
            {
                reserveLabel.text = "NO FACTION";
                reserveLabel.color = Dim();
                if (reserveHintLabel != null)
                    reserveHintLabel.text = "Join a faction to use the wing reserve.";
                reserveReleaseButton?.SetEnabled(false);
                reserveHoldButton?.SetEnabled(false);
                return;
            }

            reserveLabel.text = "" + WingSupplyReserve.Count + " / " +
                                WingSupplyReserve.Capacity;
            reserveLabel.color = Friendly();

            if (reserveHintLabel != null)
            {
                reserveHintLabel.text = WingSupplyReserve.Count >= WingSupplyReserve.Capacity
                    ? "FULL - RELEASE a selected row before holding another."
                    : WingSupplyReserve.Count > 0
                        ? "Select a row to RELEASE it, or HOLD another from faction stock."
                        : "Select an airframe, then HOLD it to protect it from AI.";
            }

            bool host = WingSupplyReserve.IsHost;
            bool selected = selectedOffer != null;

            // Selecting a different airframe disarms: the confirmation is for the thing
            // that was named when the first press happened, not for whatever is selected
            // when the second one lands.
            bool armed = reserveRelease.IsArmedFor(selectedOffer);

            reserveReleaseButton?.SetLatched(armed);
            reserveReleaseButton?.SetText(armed ? "SURE?" : "RELEASE");
            reserveReleaseButton?.SetEnabled(
                host && selected && WingSupplyReserve.CountOf(selectedOffer) > 0);
            reserveHoldButton?.SetEnabled(
                host && selected && WingSupplyReserve.Count < WingSupplyReserve.Capacity &&
                WingSupplyReserve.FactionStockOf(selectedOffer) > 0);
        }

        private static void SyncRosterRows(int needed)
        {
            while (rosterRows.Count < needed && rosterArea != null)
            {
                int index = rosterRows.Count;
                rosterRows.Add(new RosterRow(rosterArea, index));
            }
        }

        /// <summary>
        /// The shop: a row per airframe the faction has in stock, then the two controls that
        /// decide what a purchase costs and whether it is allowed.
        ///
        /// A fixed set of rows is built once and rebound each refresh, the same way the
        /// roster works — building UI objects on a timer is how a screen like this starts
        /// costing frames. The list is paged because the panel is sized to its content and an
        /// unbounded catalogue would run off the display.
        /// </summary>
        private static float AddShop(RectTransform parent, float y)
        {
            if (!Plugin.Config2.ShopEnabled.Value) return y;

            // Its own popup rather than the Loadout page's: each is parented to the page it
            // covers, so a list left open on one tab cannot draw over another.
            shopTemplatePopup = new WingUi.Popup(parent, PanelWidth);

            y = Heading(parent, y, "AIRFRAME REQUISITION");

            // Page controls. The faction usually has more airframes in stock than fit on a
            // panel sized to its content, and silently showing only the cheapest few hid most
            // of the catalogue. Both arrows go dead on a single page rather than looking
            // available and doing nothing.
            shopPrevButton = Pager(parent, y, "<", () => TurnPage(-1));
            shopPageLabel = PagerLabel(parent, y);
            shopNextButton = Pager(parent, y, ">", () => TurnPage(1));
            y -= RowHeight + Gap;

            var area = new GameObject("ShopArea", typeof(RectTransform));
            shopArea = area.GetComponent<RectTransform>();
            shopArea.SetParent(parent, worldPositionStays: false);

            float height = ShopRows * RowPitch;
            Place(shopArea, new Rect(Pad, y, PanelWidth - Pad * 2f, height));

            for (int i = 0; i < ShopRows; i++)
                shopRows.Add(new ShopRow(shopArea, i));

            y -= height + Space2;

            // The detail line gets the full width to itself. It used to share a row with the
            // requisition button and print the pricing formula to fit — "31 x 1.5^0 = 31" —
            // which is a thing to decode rather than a thing to read.
            offerDetailLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                     Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;

            // What is actually being bought. A requisition carries a loadout, and a
            // price/stock breakdown that named only the airframe would be describing half
            // the purchase.
            //
            // The fit is chosen here rather than on LOADOUT, which is the other half of this
            // rework: LOADOUT builds templates, this decides which one the money is being
            // spent on. It was a sentence telling the player to go to another tab, which is
            // a poor substitute for the control the other tab was hiding.
            const float fitGutter = 34f;
            shopTemplateButton = WingUi.Button(
                parent, "",
                new Rect(Pad + fitGutter, y, PanelWidth - Pad * 2f - fitGutter, RowHeight),
                FontSmall, UiButtonStyle.Default, OpenShopTemplatePicker)
                .WithTooltip(OrderHint.Fit);
            Label(parent, "FIT", new Rect(Pad, y, fitGutter - Gap, RowHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);

            // Where the list drops from: directly under the button that opens it.
            shopTemplateRowY = y - RowHeight;
            shopTemplateRowX = Pad + fitGutter;
            shopTemplateRowWidth = PanelWidth - Pad * 2f - fitGutter;
            y -= RowHeight + Space1;

            offerLoadoutLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + Space1;

            // REQUISITION is the reason this page exists and is drawn as such; the
            // over-limit permission beside it is a modifier on that purchase and reads a
            // rank quieter until it is switched on, at which point it latches lit.
            const float buyWidth = 130f;
            float exceedWidth = PanelWidth - Pad * 2f - Gap - buyWidth;
            exceedLimitButton = WingUi.Button(parent, "", new Rect(Pad, y, exceedWidth, RowHeight),
                                              FontBody, UiButtonStyle.Quiet, ToggleExceedLimit)
                                 .WithTooltip(OrderHint.OverLimit);
            requisitionButton = WingUi.Button(parent, "REQUISITION",
                                              new Rect(PanelWidth - Pad - buyWidth, y,
                                                       buyWidth, RowHeight),
                                              FontBody, UiButtonStyle.Primary,
                                              RequisitionSelected)
                                 .WithTooltip(OrderHint.Requisition);
            y -= RowHeight + Gap;
            return y;
        }

        private static void TurnPage(int direction)
        {
            shopPage += direction;
            if (shopPage < 0) shopPage = 0;
        }

        /// <summary>
        /// Grant or withdraw permission to requisition past the mission's AI aircraft cap.
        ///
        /// Permission rather than a purchase mode: it changes nothing while the squadron has
        /// room, and only then does the surcharge apply. Keeping the button live even when it
        /// cannot be used is deliberate — a rank refusal the player can read beats a greyed
        /// control that never says why.
        /// </summary>
        private static void ToggleExceedLimit()
        {
            if (!WingShop.MeetsExceedLimitRank)
            {
                WingCommandManager.Instance?.Toast(
                    "Requisitioning past the squadron limit requires rank " + WingShop.ExceedLimitRank);
                return;
            }

            WingShop.ExceedLimit = !WingShop.ExceedLimit;
            WingCommandManager.Instance?.Toast(WingShop.ExceedLimit
                ? "Over-limit requisition allowed at " +
                  WingShop.ExceedLimitMultiplier.ToString("0.##") + "x list price"
                : "Over-limit requisition disallowed");
        }

        private static void RequisitionSelected()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            bool bought = WingShop.Buy(selectedOffer, out string why, out float paid);
            WingCommandManager.Instance?.Toast(bought
                ? selectedOffer.unitName + " requisitioned for " + Mathf.RoundToInt(paid) +
                  " - departing friendly base"
                : why);
        }

        /// <summary>Rebind the shop rows and the allocation header.</summary>
        private static void RefreshShop()
        {
            if (!Plugin.Config2.ShopEnabled.Value || shopRows.Count == 0) return;

            IReadOnlyList<WingShop.Offer> offers = WingShop.Catalogue();

            // Clamp here rather than in TurnPage: stock runs out and the catalogue shrinks
            // under the player, so the page has to be re-validated against what is actually
            // on offer each time rather than only when a button is pressed.
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopRows));
            if (shopPage >= pages) shopPage = pages - 1;
            if (shopPage < 0) shopPage = 0;

            if (shopPageLabel != null)
            {
                shopPageLabel.text = offers.Count == 0
                    ? "nothing in stock for your airframe"
                    : "page " + (shopPage + 1) + " of " + pages + "   (" + offers.Count + " types)";
            }

            shopPrevButton?.SetEnabled(shopPage > 0);
            shopNextButton?.SetEnabled(shopPage < pages - 1);

            int first = shopPage * ShopRows;

            for (int i = 0; i < shopRows.Count; i++)
            {
                int index = first + i;
                if (index < offers.Count) shopRows[i].Bind(offers[index]);
                else shopRows[i].Hide();
            }

            ValidateSelectedOffer(offers);
            RefreshOfferDetail(offers);
        }

        /// <summary>
        /// Drop a selection the catalogue no longer contains.
        ///
        /// Stock runs out under the player, and both the Supply and Loadout tabs act on this
        /// one selection — so it is re-checked against what is actually on offer wherever it
        /// is read, not only where it is set.
        /// </summary>
        private static void ValidateSelectedOffer(IReadOnlyList<WingShop.Offer> offers)
        {
            if (selectedOffer == null) return;

            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Definition == selectedOffer) return;
            }
            selectedOffer = null;
        }

        /// <summary>
        /// The selected airframe in one plain sentence, and the two controls that act on it.
        /// </summary>
        private static void RefreshOfferDetail(IReadOnlyList<WingShop.Offer> offers)
        {
            WingShop.PurchaseQuote quote = WingShop.Quote(selectedOffer);
            bool overLimit = quote.OverLimit;

            if (exceedLimitButton != null)
            {
                exceedLimitButton.SetText(
                    "OVER LIMIT  x" + WingShop.ExceedLimitMultiplier.ToString("0.##"));
                exceedLimitButton.SetLatched(WingShop.ExceedLimit);
            }

            if (offerDetailLabel != null)
            {
                if (selectedOffer == null)
                {
                    offerDetailLabel.text = "Select an airframe.";
                    offerDetailLabel.color = Dim();
                }
                else
                {
                    float cost = quote.Price;
                    int reservedCount = WingSupplyReserve.CountOf(selectedOffer);
                    int ownedCount = WingSupplyReserve.OwnedOf(selectedOffer);
                    int stock = 0;
                    for (int i = 0; i < offers.Count; i++)
                    {
                        if (offers[i].Definition != selectedOffer) continue;
                        stock = offers[i].Stock;
                        break;
                    }

                    offerDetailLabel.text = quote.CanBuy
                        ? UiTheme.Truncate(selectedOffer.unitName, 18) +
                          "  ·  " + Mathf.RoundToInt(cost) + " funds" +
                          (overLimit ? " (over limit)" : "") +
                          "  ·  " + stock + " available" +
                          (ownedCount > 0 ? "  ·  " + ownedCount + " owned reserve" :
                           reservedCount > 0 ? "  ·  held in wing reserve" : "")
                        : "UNAVAILABLE  ·  " + quote.Reason;
                    offerDetailLabel.color = quote.CanBuy ? Friendly() : Warning();
                }
            }

            if (offerLoadoutLabel != null)
            {
                if (selectedOffer == null)
                {
                    offerLoadoutLabel.text = "";
                }
                else
                {
                    // A recovered airframe launches with the fit it came home with, so the
                    // planned loadout is not what the next one of these will carry. Saying
                    // which of the two applies is the difference between a breakdown and a
                    // guess.
                    WingLoadoutChoice fit = WingLoadoutBook.PlannedFor(selectedOffer);
                    bool fromReserve = false;

                    if (WingSupplyReserve.PeekLoadout(selectedOffer,
                                                      out WingLoadoutChoice stored))
                    {
                        fit = stored;
                        fromReserve = true;
                    }

                    offerLoadoutLabel.text = fromReserve
                        ? "This one comes out of the reserve as recovered - the fit above " +
                          "applies to the next new airframe."
                        : "Build fits on LOADOUT; choose one here.";
                    offerLoadoutLabel.color = Dim();
                }
            }

            RefreshShopTemplateButton();
            requisitionButton?.SetEnabled(quote.CanBuy);
        }

        /// <summary>
        /// The fit the next requisition of the selected airframe will launch with.
        ///
        /// Reports the plan, not the reserve. A recovered airframe keeps what it came home
        /// with and the button cannot change that, so the line beneath it says so instead of
        /// this control lying about which of the two is about to be spent.
        /// </summary>
        private static void RefreshShopTemplateButton()
        {
            if (shopTemplateButton == null) return;

            if (selectedOffer == null)
            {
                shopTemplateButton.SetText("-");
                shopTemplateButton.SetEnabled(false);
                shopTemplateButton.SetLatched(false);
                return;
            }

            WingLoadoutChoice planned = WingLoadoutBook.PlannedFor(selectedOffer);

            // A template deleted since the order was placed falls back to the standard fit,
            // which is what Build would do with it anyway. Saying so here beats printing the
            // name of something that no longer exists.
            if (planned.IsTemplate && !WingLoadoutTemplates.Exists(planned.TemplateId))
            {
                WingLoadoutBook.Plan(selectedOffer, planned.WithTemplate(null));
                planned = WingLoadoutBook.PlannedFor(selectedOffer);
            }

            shopTemplateButton.SetText(
                UiTheme.Truncate(WingLoadoutCatalog.Label(selectedOffer, planned), 34)
                       .ToUpperInvariant());
            shopTemplateButton.SetEnabled(true);
            shopTemplateButton.SetLatched(planned.IsTemplate);
        }

        /// <summary>
        /// Choose what the next one of these flies with: the standard fit, or a template.
        ///
        /// The standard fit is always first and always available, because it is the answer
        /// for a player who has never opened LOADOUT and the one fit no airframe can refuse.
        /// </summary>
        private static void OpenShopTemplatePicker()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            WingLoadoutChoice planned = WingLoadoutBook.PlannedFor(selectedOffer);
            IReadOnlyList<LoadoutTemplateRecord> mine = WingLoadoutTemplates.For(selectedOffer);

            // Null stands for the standard fit in the parallel id list, so the pick handler
            // is one branch rather than an index offset to keep straight.
            var ids = new List<string>(mine.Count + 1) { null };
            popupEntries.Clear();
            popupEntries.Add(new WingUi.PopupEntry(
                "STANDARD FIT", "as issued", !planned.IsTemplate));

            for (int i = 0; i < mine.Count; i++)
            {
                ids.Add(mine[i].Id);
                popupEntries.Add(new WingUi.PopupEntry(
                    UiTheme.Truncate(mine[i].Name, 24),
                    FittedCount(mine[i]) + " fitted",
                    planned.TemplateId == mine[i].Id));
            }

            if (mine.Count == 0)
                WingCommandManager.Instance?.Toast(
                    "No templates for " + selectedOffer.unitName + " - build one on LOADOUT");

            AircraftDefinition target = selectedOffer;
            shopTemplatePopup?.Show(
                new Rect(shopTemplateRowX, shopTemplateRowY, shopTemplateRowWidth, 0f),
                popupEntries, index =>
            {
                if (index < 0 || index >= ids.Count) return;

                // Re-read rather than reusing the captured choice: the popup was open across
                // frames and the plan may have moved under it.
                WingLoadoutChoice current = WingLoadoutBook.PlannedFor(target);
                WingLoadoutBook.Plan(target, current.WithTemplate(ids[index]));
            });
        }

        /// <summary>Funds, wing size, and how much of the mission's AI aircraft cap is left.</summary>
        private static void RefreshSupplyStatus()
        {
            if (supplyFundsLabel == null) return;

            int wing = WingCommandManager.Instance?.Wing?.Count ?? 0;
            supplyFundsLabel.text = "FUNDS " + Mathf.RoundToInt(WingShop.Allocation) +
                                    "   ·   WING " + wing + " / " + WingRegistry.WingLimitLabel;

            WingShop.SquadronState squadron = WingShop.Squadron();
            string text = "SQUADRON " + squadron.Active + " / " + squadron.Limit;
            if (Plugin.Config2.DisableWingSizeLimit.Value)
                text += "  ·  WING NO LIMIT DOES NOT BYPASS THIS CAP";

            if (!squadron.AtCapacity)
            {
                supplySquadronLabel.text = text;
                supplySquadronLabel.color = Friendly();
                return;
            }

            // At capacity is the state that silently disables the whole shop, so it says both
            // that it is the reason and what can be done about it.
            if (WingShop.ExceedLimit && WingShop.MeetsExceedLimitRank)
            {
                supplySquadronLabel.text = text + "  ·  OVER LIMIT x" +
                                           WingShop.ExceedLimitMultiplier.ToString("0.##");
            }
            else if (!WingShop.MeetsExceedLimitRank)
            {
                supplySquadronLabel.text = text + "  ·  FULL, RANK " + WingShop.ExceedLimitRank +
                                           " TO EXCEED";
            }
            else
            {
                supplySquadronLabel.text = text + "  ·  FULL";
            }
            supplySquadronLabel.color = Warning();
        }
        /// <summary>One purchasable airframe: name, stock, price, buy.</summary>
        private sealed class ShopRow
        {
            private readonly GameObject go;
            private readonly TMP_Text name, stock, price;
            private readonly Image selectionRule;
            private readonly Image fill;
            private readonly WingButton hit;
            private AircraftDefinition bound;

            public ShopRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Shop" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, RowHeight), RowColor());

                // The whole row selects, marked by a lit edge, exactly as the flight roster
                // on the Tactical page works. A per-row SELECT button spent a sixth of the
                // width restating what clicking the row would obviously do.
                //
                // Which left nothing at all saying the row could be clicked: a catalogue of
                // outlined boxes reads as a printed table until you happen to click one.
                // The row now lights under the pointer, which is the whole of the cue.
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), RowColor());
                hit = HitButton(rt, new Rect(0f, 0f, width, RowHeight), () =>
                {
                    if (bound != null) selectedOffer = bound;
                });

                name  = Label(rt, "", new Rect(Space2 + 2f, 0f, 170f, RowHeight), Friendly(),
                              FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
                stock = Label(rt, "", new Rect(186f, 0f, 90f, RowHeight), Dim(), FontSmall,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                price = Label(rt, "", new Rect(280f, 0f, width - 280f - Space3, RowHeight), Dim(),
                              FontBody, FontStyles.Normal, TextAlignmentOptions.Right);

                go.SetActive(false);
            }

            public void Bind(WingShop.Offer offer)
            {
                bound = offer.Definition;
                if (!go.activeSelf) go.SetActive(true);

                // The price shown is the price charged, surcharge included — the row is where
                // the number is read, so it is where the real one belongs.
                float cost = WingShop.CurrentPriceOf(offer.Definition);
                bool affordable = WingShop.Allocation >= cost;
                bool selected = selectedOffer == offer.Definition;

                name.text = UiTheme.Truncate(offer.Name, 22);
                stock.text = offer.Stock + " available";
                price.text = Mathf.RoundToInt(cost).ToString();

                // Grey the price when it cannot be met, so the constraint reads at a glance
                // rather than only on a failed press.
                price.color = affordable ? Accent() : Warning();
                name.color = !affordable ? Dim() : selected ? Green() : Friendly();
                selectionRule.color = selected ? Green() : RowColor();

                // The row's resting fill carries selection; the hit target adds the pointer
                // on top of whatever that is, so hovering a selected row reads as both.
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

        /// <summary>One line of the roster: slot, name, order, slot error, release button.</summary>
        private sealed class RosterRow
        {
            private readonly GameObject go;
            private readonly TMP_Text slot, name, order, preference, error, reserves;
            private readonly Image selectionRule;
            private readonly Image fill;
            private readonly WingButton hit;
            private readonly WingButton release;
            private WingMember bound;

            /// <summary>
            /// Which wingman, if any, has had its REL pressed once and is waiting to have it
            /// pressed again.
            ///
            /// Static, so arming one row disarms every other: two rows both offering to
            /// discharge a wingman on the next click is worse than none.
            /// </summary>
            private static readonly Confirmation memberRelease = new Confirmation();

            /// <summary>Drop the armed wingman when the mission ends, with everything else.</summary>
            public static void Disarm() => memberRelease.Clear();

            public RosterRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Row" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());

                const float releaseWidth = 42f;
                hit = HitButton(rt, new Rect(0f, 0f, width - releaseWidth - Space2, RowHeight), () =>
                {
                    if (bound == null) return;
                    bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    WingCommandManager.Instance?.SelectMember(bound, toggle);
                });

                slot  = Label(rt, "", new Rect(6f, 0f, 18f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                name  = Label(rt, "", new Rect(26f, 0f, 100f, RowHeight), WingColor(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(128f, 0f, 58f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                preference = Label(rt, "", new Rect(188f, 0f, 30f, RowHeight), Dim(), FontMicro,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                error = Label(rt, "", new Rect(220f, 0f, 52f, RowHeight), Dim(), FontBody,
                              FontStyles.Normal, TextAlignmentOptions.Right);
                reserves = Label(rt, "", new Rect(276f, 0f, 70f, RowHeight), Dim(), FontSmall,
                              FontStyles.Normal, TextAlignmentOptions.Right);

                // REL discharges a wingman for good, and it sat one row-width from the row
                // you click to select one, in the same green as the orders. It is now drawn
                // as the destructive control it is, and it asks twice — the same
                // press-again-to-confirm the Supply page already uses for its assignment
                // fee, so the panel only has the one idiom for "this one is going to cost
                // you something".
                release = WingUi.Button(rt, "REL",
                                        new Rect(width - releaseWidth - 6f, -1f, releaseWidth,
                                                 RowHeight - 2f),
                                        FontSmall, UiButtonStyle.Danger, ConfirmRelease)
                                .WithTooltip(OrderHint.Release);
            }

            /// <summary>Arm on the first press, discharge on the second.</summary>
            private void ConfirmRelease()
            {
                if (bound == null) return;

                if (memberRelease.IsArmedFor(bound))
                {
                    WingMember going = bound;
                    memberRelease.Clear();
                    WingCommandManager.Instance?.RemoveMember(going);
                    return;
                }

                memberRelease.Arm(bound);
                WingCommandManager.Instance?.Toast(
                    "Press REL again to release " + bound.Name + " from the wing");
            }

            public void Bind(WingMember m)
            {
                bool memberChanged = bound != m;
                bound = m;
                if (!go.activeSelf) go.SetActive(true);

                bool selected = WingCommandManager.Instance?.Selection.Contains(m) ?? true;

                bool armed = memberRelease.IsArmedFor(m);
                release?.SetLatched(armed);
                release?.SetText(armed ? "SURE?" : "REL");

                // Just the slot number. The filled/hollow circles this used to draw are not
                // in the MFD font, so every row rendered the same tofu box and the marker
                // said nothing — while the lit edge and the green callsign beside it were
                // already showing selection perfectly well.
                if (memberChanged)
                {
                    slot.text = m.Slot.ToString();
                    name.text = UiTheme.Truncate(m.Name, 16);
                }
                slot.color = selected ? Green() : Dim();
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();

                // Selection is the row's resting state; the pointer only adds to it. Until
                // now nothing at all happened when the mouse crossed a row, so a roster that
                // is the panel's main control surface looked exactly like a readout.
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
                order.text = ShortOrder(m);

                // The weapon preference gets its own narrow column rather than being
                // appended to the state text. Sharing that cell would have truncated the
                // order — which is the more important of the two — the moment anything but
                // AUTO was selected.
                preference.text = WingWeaponPreferences.ShortLabel(m.WeaponPreference);
                preference.color = m.WeaponPreference == WingWeaponPreference.Auto
                    ? Dim()
                    : Accent();

                // Fuel and stores are aggregate queries over every tank/station. Sample
                // each once so binding one row does not walk both collections twice.
                float fuel = m.Fuel;
                int ammo = m.Ammo;
                reserves.text = Mathf.RoundToInt(fuel * 100f) + "%  " + ammo;
                reserves.color = fuel <= Plugin.Config2.BingoFuel.Value || ammo <= 0
                    ? new Color(1f, 0.55f, 0.2f)
                    : Dim();

                error.text = ErrorText(m);
                error.color = !m.IsPanicking && m.Order == WingOrder.Formation &&
                              m.SlotError > 0f && m.SlotError < 250f
                    ? Accent()
                    : Dim();
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }

            private static string ErrorText(WingMember m)
            {
                if (m.DeliveryPending) return "WAIT";
                if (m.IsPanicking || m.Order != WingOrder.Formation) return "-";
                if (m.SlotError <= 0f) return "...";
                return m.SlotError < 10000f
                    ? m.SlotError.ToString("F0") + " m"
                    : (m.SlotError / 1000f).ToString("F1") + " km";
            }
        }

        // ----------------------------------------------------------------- loadout page

        /// <summary>
        /// Where loadout templates are built: a store on every pylon, saved under a name.
        ///
        /// The page used to offer bulk-generated role and factory fits. Those paths proved
        /// unreliable, so templates now start empty and every store is chosen explicitly.
        /// The old flight list is also gone because it reported something the player could
        /// not act on from here and the WING tab already says it.
        ///
        /// What is left is a workshop, and nothing on it is per-mission. A template is a
        /// standing preference kept in the config file, and this page never touches the
        /// aircraft in the air or the funds in the bank. Choosing which template the next
        /// requisition of a type flies with is a purchase decision and belongs, with the
        /// price and the stock count, on SUPPLY.
        /// </summary>
        private static float AddLoadoutPage(RectTransform parent, float y)
        {
            loadoutPopup = new WingUi.Popup(parent, PanelWidth);

            y = Heading(parent, y, "TEMPLATE");

            // The airframe is the Supply tab's selection, still shared. A template is only
            // meaningful for one airframe — the pylons are the airframe's — so picking the
            // aircraft on either page and configuring it here keeps one thread running
            // through both.
            float w = PanelWidth - Pad * 2f;
            Stepper(parent, Pad, y, w, out loadoutAirframeLabel,
                    () => CycleOffer(-1), () => CycleOffer(1), LoadoutHint.Airframe);
            y -= RowHeight + Gap;

            float left = Pad + GutterWidth;
            float inner = PanelWidth - Pad - left;

            // The selector, then the three things that can be done to the list it selects
            // from. Square buttons: at this width a word would have to be abbreviated past
            // the point of being read, and the status strip explains each on hover.
            const float actionWidth = 30f;
            const float actionBlock = (actionWidth + Gap) * 3f;

            Gutter(parent, y, "TEMPLATE");
            float selectWidth = inner - actionBlock;
            templateSelectButton = WingUi.Button(parent, "", new Rect(left, y, selectWidth, RowHeight),
                                                 FontSmall, UiButtonStyle.Default,
                                                 () => OpenTemplatePicker(left, y, selectWidth))
                                        .WithTooltip(LoadoutHint.Select);
            templateLabel = null;

            float actionX = left + selectWidth + Gap;
            templateNewButton = WingUi.Button(parent, "+", new Rect(actionX, y, actionWidth, RowHeight),
                                              FontBody, UiButtonStyle.Default, NewTemplate)
                                     .WithTooltip(LoadoutHint.New);
            templateCopyButton = WingUi.Button(parent, "C",
                                               new Rect(actionX + actionWidth + Gap, y,
                                                        actionWidth, RowHeight),
                                               FontBody, UiButtonStyle.Default, CopyTemplate)
                                      .WithTooltip(LoadoutHint.Copy);
            templateDeleteButton = WingUi.Button(parent, "X",
                                                 new Rect(actionX + (actionWidth + Gap) * 2f, y,
                                                          actionWidth, RowHeight),
                                                 FontBody, UiButtonStyle.Danger, DeleteTemplate)
                                        .WithTooltip(LoadoutHint.Delete);
            y -= RowHeight + Gap;

            // Renaming is only offered where the keyboard can actually be held off the
            // aircraft. On a build where that fails the field is replaced by a readout, and
            // templates keep the numbered names they are created with — a name is worth
            // having, but not at the price of typing one into the flight controls.
            Gutter(parent, y, "NAME");
            float nameWidth = inner;

            if (WingKeyboardGuard.Available)
            {
                templateNameField = WingUi.InputField(
                    parent, new Rect(left, y, nameWidth, RowHeight),
                    WingLoadoutTemplates.MaxNameLength, RenameTemplate, LoadoutHint.Name);
            }
            else
            {
                Panel(parent, new Rect(left, y, nameWidth, RowHeight), RowColor());
                templateLabel = Label(parent, "",
                                      new Rect(left + Space2, y, nameWidth - Space4, RowHeight),
                                      Dim(), FontBody, FontStyles.Normal,
                                      TextAlignmentOptions.Left);
            }

            y -= RowHeight + Gap;

            y = Heading(parent, y, "PYLONS");
            y = ColumnHeaders(parent, y, PylonColumns);

            var area = new GameObject("PylonArea", typeof(RectTransform));
            pylonArea = area.GetComponent<RectTransform>();
            pylonArea.SetParent(parent, worldPositionStays: false);

            float areaHeight = RowPitch * PylonRowsPerPage;
            Place(pylonArea, new Rect(Pad, y, PanelWidth - Pad * 2f, areaHeight));
            pylonAreaY = y;

            for (int i = 0; i < PylonRowsPerPage; i++) pylonRows.Add(new PylonRow(pylonArea, i));
            y -= areaHeight + Gap;

            pylonPrevButton = Pager(parent, y, "<", () => TurnPylonPage(-1));
            pylonPageLabel = PagerLabel(parent, y);
            pylonNextButton = Pager(parent, y, ">", () => TurnPylonPage(1));
            y -= RowHeight + Gap;

            templateSummaryLabel = Label(parent, "",
                                         new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                         Dim(), FontMicro, FontStyles.Normal,
                                         TextAlignmentOptions.Left);
            y -= LineHeight + Space1;

            loadoutStatusLabel = Label(parent, "",
                                       new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                       Dim(), FontMicro, FontStyles.Normal,
                                       TextAlignmentOptions.Left);
            return y - (LineHeight + Space1);
        }

        /// <summary>Where the pylon list starts, so a popup can be dropped onto a row.</summary>
        private static float pylonAreaY;

        private static readonly Column[] PylonColumns =
        {
            new Column("PYLON", 8f, 150f),
            new Column("STORE", 162f, PanelWidth - Pad * 2f - 162f - Space3, rightAligned: true),
        };

        /// <summary>A fixed-height area that roster rows are laid out inside.</summary>
        private static RectTransform RosterViewport(RectTransform parent, string name, float y)
        {
            var area = new GameObject(name, typeof(RectTransform));
            RectTransform rt = area.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, new Rect(Pad, y, PanelWidth - Pad * 2f, RowPitch * RosterRowsPerPage));
            return rt;
        }

        /// <summary>Step the shared airframe selection through the requisition catalogue.</summary>
        private static void CycleOffer(int direction)
        {
            IReadOnlyList<WingShop.Offer> offers = WingShop.Catalogue();
            if (offers.Count == 0)
            {
                WingCommandManager.Instance?.Toast("Nothing in stock for your airframe");
                return;
            }

            int index = -1;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Definition != selectedOffer) continue;
                index = i;
                break;
            }

            index = index < 0 ? 0 : (index + direction + offers.Count) % offers.Count;
            selectedOffer = offers[index].Definition;

            // A template belongs to one airframe's pylons, so changing the airframe cannot
            // keep editing the old one.
            editingTemplateId = null;
            pylonPage = 0;
        }

        // -------------------------------------------------------------- template editing

        /// <summary>
        /// The template being edited, re-resolved every time it is asked for.
        ///
        /// Deliberately not cached. The record can vanish between one refresh and the next —
        /// deleted here, or dropped by a config edit — and every caller on this page has to
        /// cope with null anyway, so there is no reading of it that a stale reference makes
        /// safer.
        /// </summary>
        private static LoadoutTemplateRecord EditingTemplate()
        {
            if (selectedOffer == null) return null;

            LoadoutTemplateRecord record = WingLoadoutTemplates.ById(editingTemplateId);
            if (record != null && record.AirframeKey == selectedOffer.jsonKey) return record;

            // Fall to the airframe's first template rather than leaving the editor blank
            // beside a list that has something in it.
            IReadOnlyList<LoadoutTemplateRecord> mine = WingLoadoutTemplates.For(selectedOffer);
            if (mine.Count == 0)
            {
                editingTemplateId = null;
                return null;
            }

            editingTemplateId = mine[0].Id;
            return mine[0];
        }

        private static void OpenTemplatePicker(float x, float y, float width)
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            IReadOnlyList<LoadoutTemplateRecord> mine = WingLoadoutTemplates.For(selectedOffer);
            if (mine.Count == 0)
            {
                WingCommandManager.Instance?.Toast(
                    "No templates for " + selectedOffer.unitName + " yet - press + to make one");
                return;
            }

            // Copied out of the scratch list the store hands back, because the popup's pick
            // callback runs long after this method returns and that list is reused.
            var ids = new List<string>(mine.Count);
            popupEntries.Clear();
            for (int i = 0; i < mine.Count; i++)
            {
                ids.Add(mine[i].Id);
                popupEntries.Add(new WingUi.PopupEntry(
                    UiTheme.Truncate(mine[i].Name, 24),
                    FittedCount(mine[i]) + " fitted",
                    mine[i].Id == editingTemplateId));
            }

            loadoutPopup?.Show(new Rect(x, y - RowHeight, width, 0f), popupEntries, index =>
            {
                if (index < 0 || index >= ids.Count) return;
                editingTemplateId = ids[index];
                pylonPage = 0;
                SyncNameField();
            });
        }

        private static void NewTemplate()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            if (WingLoadoutCatalog.PylonCount(selectedOffer) == 0)
            {
                WingCommandManager.Instance?.Toast(
                    selectedOffer.unitName + "'s hardpoints cannot be read on this build");
                return;
            }

            LoadoutTemplateRecord created = WingLoadoutTemplates.Create(
                selectedOffer, WingLoadoutTemplates.NextDefaultName(selectedOffer), null);

            if (created == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + WingLoadoutTemplates.MaxPerAirframe +
                    " templates");
                return;
            }

            editingTemplateId = created.Id;
            pylonPage = 0;
            SyncNameField();
        }

        private static void CopyTemplate()
        {
            LoadoutTemplateRecord source = EditingTemplate();
            if (source == null)
            {
                WingCommandManager.Instance?.Toast("Nothing to copy");
                return;
            }

            LoadoutTemplateRecord copy = WingLoadoutTemplates.Duplicate(source);
            if (copy == null)
            {
                WingCommandManager.Instance?.Toast(
                    "That airframe already has " + WingLoadoutTemplates.MaxPerAirframe +
                    " templates");
                return;
            }

            editingTemplateId = copy.Id;
            SyncNameField();
        }

        private static void DeleteTemplate()
        {
            LoadoutTemplateRecord doomed = EditingTemplate();
            if (doomed == null)
            {
                WingCommandManager.Instance?.Toast("Nothing to delete");
                return;
            }

            string name = doomed.Name;
            WingLoadoutTemplates.Delete(doomed);
            editingTemplateId = null;
            pylonPage = 0;
            SyncNameField();

            WingCommandManager.Instance?.Toast(
                "Deleted " + name + ". Anything already flying keeps its fit.");
        }

        private static void RenameTemplate(string name)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null) return;

            WingLoadoutTemplates.Rename(template, name);

            // The store trims and defaults the name, so the field is put back in step with
            // what was actually saved rather than what was typed.
            SyncNameField();
        }

        /// <summary>
        /// Put the rename field back in step with the template it is editing.
        ///
        /// Called on every change of template rather than from the refresh loop: writing to
        /// the field five times a second would move the caret out from under anyone typing
        /// in it.
        /// </summary>
        private static void SyncNameField()
        {
            LoadoutTemplateRecord template = EditingTemplate();
            string name = template != null ? template.Name : "";

            if (templateNameField != null)
            {
                if (templateNameField.text != name)
                    templateNameField.SetTextWithoutNotify(name);
                templateNameField.interactable = template != null;
            }

            if (templateLabel != null)
            {
                templateLabel.text = template != null ? name : "-";
                templateLabel.color = template != null ? Friendly() : Dim();
            }
        }

        // ------------------------------------------------------------------ pylon list

        /// <summary>
        /// The pylons the editor draws, which is not quite the airframe's list of them.
        ///
        /// A hardpoint set that mirrors the one before it is folded away: the two cannot be
        /// armed differently, so showing both would double the length of the list without
        /// adding a decision to it. The hidden one is written whenever its partner is.
        /// </summary>
        private static readonly List<int> visiblePylons = new List<int>();

        private static void RebuildVisiblePylons()
        {
            visiblePylons.Clear();
            if (selectedOffer == null) return;

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                if (WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i)) continue;
                visiblePylons.Add(i);
            }
        }

        private static void TurnPylonPage(int direction) =>
            pylonPage = Mathf.Max(0, pylonPage + direction);

        /// <summary>
        /// Put a store on a pylon, and on its mirror.
        ///
        /// Writing the mirror here rather than at the point of building means a template's
        /// saved keys always describe every station the aircraft actually has, so anything
        /// reading it back — the summary line, another install — sees the real fit rather
        /// than one wing's worth of it.
        /// </summary>
        private static void SetStore(int pylon, string key)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null) return;

            WingLoadoutTemplates.SetMount(template, pylon, key);

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < count && WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i);
                 i++)
                WingLoadoutTemplates.SetMount(template, i, key);
        }

        private static void OpenStorePicker(int pylon, int rowIndex)
        {
            LoadoutTemplateRecord template = EditingTemplate();
            if (template == null)
            {
                WingCommandManager.Instance?.Toast("Make a template first");
                return;
            }

            WingLoadoutCatalog.OptionsFor(selectedOffer, pylon, storeScratch);
            if (storeScratch.Count <= 1)
            {
                WingCommandManager.Instance?.Toast(
                    WingLoadoutCatalog.PylonName(selectedOffer, pylon) + " takes no stores");
                return;
            }

            string current = template.KeyAt(pylon);
            var keys = new List<string>(storeScratch.Count);
            popupEntries.Clear();

            for (int i = 0; i < storeScratch.Count; i++)
            {
                WingLoadoutCatalog.StoreOption option = storeScratch[i];
                keys.Add(option.Key);
                popupEntries.Add(new WingUi.PopupEntry(
                    UiTheme.Truncate(option.Label, 26), StoreDetail(option),
                    option.Key == current));
            }

            // Dropped onto the row it belongs to, so the list appears where the player is
            // already looking rather than at a fixed spot on the page.
            float rowY = pylonAreaY - RowPitch * rowIndex - RowHeight;
            loadoutPopup?.Show(new Rect(Pad, rowY, PanelWidth - Pad * 2f, 0f), popupEntries,
                               index =>
            {
                if (index < 0 || index >= keys.Count) return;
                SetStore(pylon, keys[index]);
            });
        }

        /// <summary>The right-hand column of a store row: what it is and what it weighs.</summary>
        private static string StoreDetail(WingLoadoutCatalog.StoreOption option)
        {
            if (option.IsEmpty) return "";

            string tag = option.RoleTag;
            string ammo = option.Ammo > 1 ? "x" + option.Ammo : "";

            if (tag.Length == 0 && ammo.Length == 0) return "";
            if (tag.Length == 0) return ammo;
            return ammo.Length == 0 ? tag : tag + "  " + ammo;
        }

        // -------------------------------------------------------------------- refresh

        private static void RefreshLoadoutPage()
        {
            IReadOnlyList<WingShop.Offer> offers = WingShop.Catalogue();
            ValidateSelectedOffer(offers);

            if (selectedOffer == null && offers.Count > 0) selectedOffer = offers[0].Definition;

            if (loadoutAirframeLabel != null)
            {
                loadoutAirframeLabel.text = selectedOffer != null
                    ? UiTheme.Truncate(selectedOffer.unitName, 26)
                    : "NOTHING IN STOCK";
                loadoutAirframeLabel.color = selectedOffer != null ? Friendly() : Dim();
            }

            LoadoutTemplateRecord template = EditingTemplate();
            RebuildVisiblePylons();

            RefreshTemplateControls(template);
            RefreshPylonRows(template);
            RefreshLoadoutStatus(template);
        }

        private static void RefreshTemplateControls(LoadoutTemplateRecord template)
        {
            bool haveAirframe = selectedOffer != null;
            bool readable = haveAirframe && WingLoadoutCatalog.PylonCount(selectedOffer) > 0;
            int saved = haveAirframe ? WingLoadoutTemplates.CountFor(selectedOffer) : 0;

            if (templateSelectButton != null)
            {
                templateSelectButton.SetText(
                    template != null ? UiTheme.Truncate(template.Name, 22).ToUpperInvariant()
                    : saved > 0 ? "SELECT A TEMPLATE"
                    : "NO TEMPLATES");
                templateSelectButton.SetEnabled(saved > 0);
                templateSelectButton.SetLatched(template != null);
            }

            templateNewButton?.SetEnabled(readable &&
                                          saved < WingLoadoutTemplates.MaxPerAirframe);
            templateCopyButton?.SetEnabled(template != null &&
                                           saved < WingLoadoutTemplates.MaxPerAirframe);
            templateDeleteButton?.SetEnabled(template != null);
            // The name field is written only when the template underneath it changes, so
            // typing is never interrupted by the refresh loop.
            if (!ReferenceEquals(lastNamedTemplate, template))
            {
                lastNamedTemplate = template;
                SyncNameField();
            }

            RefreshTemplateSummary(template);
        }

        /// <summary>The template the name field was last written for. See SyncNameField.</summary>
        private static LoadoutTemplateRecord lastNamedTemplate;

        /// <summary>
        /// What the template adds up to: how many stations are loaded, what it weighs, and
        /// what it is for.
        ///
        /// The weight is the part worth having. Every other readout on this page is about
        /// one pylon, and the one thing a per-pylon editor makes easy to get wrong is
        /// hanging so much off an airframe that it cannot carry it.
        /// </summary>
        private static void RefreshTemplateSummary(LoadoutTemplateRecord template)
        {
            if (templateSummaryLabel == null) return;

            if (template == null)
            {
                templateSummaryLabel.text = "";
                return;
            }

            int fitted = 0;
            float mass = 0f;
            float air = 0f;
            float surface = 0f;

            int count = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = 0; i < count; i++)
            {
                string key = template.KeyAt(i);
                if (string.IsNullOrEmpty(key)) continue;

                WingLoadoutCatalog.StoreOption store =
                    WingLoadoutCatalog.StoreOn(selectedOffer, i, key);
                fitted++;
                mass += store.Mass;
                air += store.AntiAir;
                surface += store.AntiSurface;
            }

            string role = air <= 0f && surface <= 0f ? "unarmed"
                : air > surface * 1.5f ? "air to air"
                : surface > air * 1.5f ? "air to ground"
                : "multirole";

            templateSummaryLabel.text =
                fitted + " of " + count + " pylons  ·  " + Mathf.RoundToInt(mass) + " kg  ·  " +
                role;
            templateSummaryLabel.color = fitted == 0 ? Warning() : Dim();
        }

        private static void RefreshPylonRows(LoadoutTemplateRecord template)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(visiblePylons.Count /
                                                     (float)PylonRowsPerPage));
            pylonPage = Mathf.Clamp(pylonPage, 0, pages - 1);

            if (pylonPageLabel != null)
            {
                pylonPageLabel.text = visiblePylons.Count == 0
                    ? "no readable hardpoints"
                    : "pylon page " + (pylonPage + 1) + " of " + pages;
            }

            pylonPrevButton?.SetEnabled(pylonPage > 0);
            pylonNextButton?.SetEnabled(pylonPage < pages - 1);

            // Built once per refresh so every row asks the game the same question about the
            // same in-progress fit, and into a scratch loadout rather than a fresh one:
            // this runs five times a second, and the delivery path's BuildFromKeys has to
            // keep allocating because a Loadout handed to the spawner is kept by the
            // aircraft and must never be shared.
            Loadout inProgress = template != null
                ? WingLoadoutCatalog.FillScratch(selectedOffer, template.MountKeys)
                : null;

            int first = pylonPage * PylonRowsPerPage;
            for (int i = 0; i < pylonRows.Count; i++)
            {
                int slot = first + i;
                if (template == null || slot >= visiblePylons.Count)
                {
                    pylonRows[i].Hide();
                    continue;
                }

                int pylon = visiblePylons[slot];
                bool blocked = false;
                int represented = MirrorCount(pylon);
                for (int mirror = 0; mirror < represented && !blocked; mirror++)
                    blocked = WingLoadoutCatalog.IsPylonBlocked(
                        selectedOffer, pylon + mirror, inProgress);

                pylonRows[i].Bind(
                    pylon, i,
                    WingLoadoutCatalog.PylonName(selectedOffer, pylon),
                    WingLoadoutCatalog.StoreOn(selectedOffer, pylon, template.KeyAt(pylon)),
                    represented,
                    blocked);
            }
        }

        /// <summary>How many stations one visible row actually stands for.</summary>
        private static int MirrorCount(int pylon)
        {
            int count = 1;
            int total = WingLoadoutCatalog.PylonCount(selectedOffer);
            for (int i = pylon + 1;
                 i < total && WingLoadoutCatalog.MirrorsPrevious(selectedOffer, i);
                 i++)
                count++;
            return count;
        }

        private static void RefreshLoadoutStatus(LoadoutTemplateRecord template)
        {
            if (loadoutStatusLabel == null) return;

            if (selectedOffer == null)
            {
                loadoutStatusLabel.text = "No airframe your flight can formate on is in stock.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            if (!WingLoadoutCatalog.Available)
            {
                loadoutStatusLabel.text =
                    "Stock station data unreadable on this build - standard fit only.";
                loadoutStatusLabel.color = Warning();
                return;
            }

            if (WingLoadoutCatalog.PylonCount(selectedOffer) == 0)
            {
                loadoutStatusLabel.text =
                    UiTheme.Truncate(selectedOffer.unitName, 18) +
                    "'s hardpoints cannot be read; it flies its standard fit.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            if (template == null)
            {
                loadoutStatusLabel.text =
                    "Press + to start a template for " +
                    UiTheme.Truncate(selectedOffer.unitName, 18) + ".";
                loadoutStatusLabel.color = Dim();
                return;
            }

            // Says where the template is actually used, because nothing on this page applies
            // it: a player who builds one and never opens SUPPLY has changed nothing.
            loadoutStatusLabel.text =
                "Saved. Choose it on SUPPLY to fly the next " +
                UiTheme.Truncate(selectedOffer.unitName, 14) + " with it.";
            loadoutStatusLabel.color = Friendly();
        }

        private static int FittedCount(LoadoutTemplateRecord template) =>
            template == null ? 0 : CountFitted(template.MountKeys);

        private static int CountFitted(IReadOnlyList<string> keys)
        {
            if (keys == null) return 0;

            int fitted = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrEmpty(keys[i])) fitted++;
            }
            return fitted;
        }

        /// <summary>What each control on the Loadout tab says about itself on hover.</summary>
        private static class LoadoutHint
        {
            public const string Airframe =
                "Which airframe's pylons you are editing. Templates belong to one aircraft " +
                "type, because the hardpoints do.";

            public const string Select =
                "Switch between the templates saved for this airframe.";

            public const string New =
                "Start a new empty template and fit it pylon by pylon.";

            public const string Copy =
                "Copy this template, so a variation can be made without losing the original.";

            public const string Delete =
                "Delete this template for good. Aircraft already flying it keep their fit.";

            public const string Name =
                "Name the template. Flight controls are held off while you type here.";

            public const string Pylon =
                "Choose what hangs on this pylon. A symmetric pair is set together.";

            public const string Blocked =
                "Another store already fitted rules this pylon out. Clear that one to use it.";

            public const string BlockedFitted =
                "Another store rules this fitted pylon out. Click to clear this pylon.";

        }

        /// <summary>
        /// One pylon: what it is called, what is on it, and a click to change that.
        ///
        /// The whole row opens the store list, the way every other list on this panel is
        /// selected by its row rather than by a button inside it. A blocked pylon still
        /// draws its name — knowing the station exists and why it cannot be used is the
        /// point — but goes inert and says so on hover.
        /// </summary>
        private sealed class PylonRow
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly TMP_Text name;
            private readonly TMP_Text store;
            private readonly WingButton hit;

            public PylonRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Pylon" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = go.GetComponent<Image>();
                fill.color = WingUi.CardFill;
                fill.raycastTarget = false;
                Outline(rt, new Rect(0f, 0f, width, RowHeight), FrameColor());

                name = Label(rt, "", new Rect(Space2, 0f, 150f, RowHeight), Friendly(), FontSmall,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                store = Label(rt, "", new Rect(162f, 0f, width - 162f - Space2, RowHeight),
                              Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);

                hit = HitButton(rt, new Rect(0f, 0f, width, RowHeight), null);
                go.SetActive(false);
            }

            public void Bind(int pylon, int rowIndex, string pylonName,
                             WingLoadoutCatalog.StoreOption fitted, int mirrors, bool blocked)
            {
                if (!go.activeSelf) go.SetActive(true);

                // A mirrored pair says so, so the player is not left wondering why the list
                // is shorter than the aircraft looks.
                name.text = mirrors > 1
                    ? UiTheme.Truncate(pylonName, 20) + "  x" + mirrors
                    : UiTheme.Truncate(pylonName, 24);

                if (blocked)
                {
                    store.text = "BLOCKED";
                    store.color = Warning();
                    name.color = Dim();

                    // A newly selected store elsewhere can block a station that was already
                    // fitted. Keep that row actionable so the conflicting store can be
                    // cleared instead of trapping the template in an invalid state.
                    bool canClear = !fitted.IsEmpty;
                    hit.SetAction(canClear ? () => SetStore(pylon, null) : (Action)null);
                    hit.SetEnabled(canClear);
                    hit.WithTooltip(canClear ? LoadoutHint.BlockedFitted : LoadoutHint.Blocked);
                    hit.SetRowHighlight(fill, WingUi.CardFill,
                                        canClear ? WingUi.CardFillHover : WingUi.CardFill);
                    return;
                }

                bool empty = fitted.IsEmpty;
                store.text = empty ? "— EMPTY —" : UiTheme.Truncate(fitted.Label, 24);
                store.color = empty ? Dim() : Friendly();
                name.color = Friendly();

                hit.SetEnabled(true);
                hit.WithTooltip(LoadoutHint.Pylon);
                hit.SetAction(() => OpenStorePicker(pylon, rowIndex));
                hit.SetRowHighlight(fill, WingUi.CardFill, WingUi.CardFillHover);
            }

            public void Hide()
            {
                if (go.activeSelf) go.SetActive(false);
            }
        }

        // -------------------------------------------------------------------- wing page

        /// <summary>
        /// Who is flying, how they are doing, and what state their aircraft is in.
        ///
        /// Read-only by design. Everything that can be changed about a wingman already has
        /// a control somewhere else, and duplicating those here would give the player two
        /// places to look for the same switch.
        /// </summary>
        private static float AddWingPage(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");
            y = ColumnHeaders(parent, y, WingInspectColumns);

            wingRosterArea = RosterViewport(parent, "WingRoster", y);
            y -= RowPitch * RosterRowsPerPage + Gap;

            wingPager = new RosterPager(parent, y);
            y -= RowHeight + Gap;

            y = Heading(parent, y, "PILOT");
            float w = PanelWidth - Pad * 2f;

            const float portrait = 82f;
            const float portraitGap = Space3;
            Panel(parent, new Rect(Pad, y, portrait, portrait), WingUi.CardFill);
            Outline(parent, new Rect(Pad, y, portrait, portrait), FrameColor());
            AddSprite(parent, "PilotPortrait", PilotPortrait.Sprite,
                      new Rect(Pad + 3f, y - 3f, portrait - 6f, portrait - 6f), Color.white);

            float dossierX = Pad + portrait + portraitGap;
            float dossierW = w - portrait - portraitGap;

            // The pilot's name is the one line on this page that is read first, so it is a
            // step up from the readouts beneath it rather than a pixel up from them.
            pilotIdentityLabel = Label(parent, "", new Rect(dossierX, y, dossierW, Space5), Green(), FontLead,
                                       FontStyles.Normal, TextAlignmentOptions.Left);
            float detailY = y - Space5;

            pilotRankLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                   FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            // Track first, fill second: the fill is resized every refresh, so it must be the
            // later sibling or a full bar would be drawn underneath its own background.
            Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(dossierX, detailY, dossierW, 3f), Accent());
            pilotXpBarWidth = dossierW;
            detailY -= Space3;

            pilotStatsLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Friendly(),
                                    FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;

            pilotPersonaLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight), Dim(),
                                      FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            y -= portrait + Space2;

            Label(parent, "BIO", new Rect(Pad, y, 34f, Space4), WingColor(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            pilotBackgroundLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 38f, Space6 + Space3),
                                         Friendly(), FontMicro, FontStyles.Italic,
                                         TextAlignmentOptions.TopLeft);
            pilotBackgroundLabel.enableWordWrapping = true;
            pilotBackgroundLabel.overflowMode = TextOverflowModes.Ellipsis;
            y -= Space6 + Space4;

            y = Heading(parent, y, "AIRFRAME");

            // A quiet aircraft silhouette turns the empty lower card into an airframe
            // dossier without competing with the live numbers drawn over it.
            Color ghost = WingColor();
            ghost.a = 0.075f;
            AddSprite(parent, "AirframeSilhouette", IconFactory.Get("airframe"),
                      new Rect(PanelWidth - Pad - 132f, y - 8f, 128f, 128f), ghost);

            airframeTypeLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                      FontLead, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeStateLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeOrderLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Friendly(),
                                       FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeLoadoutLabel = Label(parent, "", new Rect(Pad, y, w, LineHeight), Dim(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            airframeWeaponsLabel = Label(parent, "", new Rect(Pad, y, w, Space6 + Space2), Friendly(),
                                         FontMicro, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            airframeWeaponsLabel.enableWordWrapping = true;
            airframeWeaponsLabel.overflowMode = TextOverflowModes.Ellipsis;
            return y - (Space6 + Space2) - Space5;
        }

        private static void RefreshWingPage(WingRegistry wing)
        {
            SyncPickRows(wingRows, wingRosterArea);
            int first = wingPager != null ? wingPager.Refresh(wing) : 0;

            for (int i = 0; i < wingRows.Count; i++)
            {
                int index = first + i;
                if (index >= wing.Count)
                {
                    wingRows[i].Hide();
                    continue;
                }

                WingMember member = wing.Members[index];
                wingRows[i].Bind(member, ShortOrder(member));
            }

            WingMember focus = focusMember;
            if (focus == null)
            {
                SetWingDetail("NO WINGMEN ASSIGNED", "", "", "", 0f, "", "", "", "", "", "");
                return;
            }

            WingPilot crew = focus.Crew;
            string identity = crew != null
                ? crew.Callsign + "  ·  " + crew.Name
                : "UNASSIGNED CREW";

            string rank;
            float progress;
            if (crew == null)
            {
                rank = "";
                progress = 0f;
            }
            else
            {
                WingRank crewRank = crew.Rank;
                if (crewRank >= WingPilotRoster.TopRank)
                {
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + crew.Xp + "   MAX RANK";
                    progress = 1f;
                }
                else
                {
                    int floor = WingPilotRoster.XpForRank(crewRank);
                    int ceiling = WingPilotRoster.XpForRank(crewRank + 1);
                    rank = WingPilotRoster.RankName(crewRank) + "   XP " + crew.Xp + " / " + ceiling;
                    progress = ceiling > floor
                        ? Mathf.Clamp01((crew.Xp - floor) / (float)(ceiling - floor))
                        : 0f;
                }
            }

            Aircraft aircraft = focus.Aircraft;
            AircraftDefinition definition = DefinitionOf(focus);

            string type = definition != null
                ? UiTheme.Truncate(definition.unitName, 22) + "   SLOT " + focus.Slot
                : "AIRFRAME   SLOT " + focus.Slot;

            // These properties aggregate live component collections. Keep the values
            // coherent within this refresh and avoid repeating the same scans below.
            float fuel = focus.Fuel;
            int ammo = focus.Ammo;
            float integrity = focus.Integrity;
            string state =
                "FUEL " + Mathf.RoundToInt(fuel * 100f) + "%" +
                "   AMMO " + ammo +
                "   HULL " + Mathf.RoundToInt(integrity * 100f) + "%" +
                (focus.CanDeliverCargo ? "   CARGO " + focus.CargoAmmo : "");

            string order =
                "ORDER " + WingOrderCatalog.ShortLabel(focus.Order) +
                "   WEAPONS " + WingWeaponPreferences.Label(focus.WeaponPreference) +
                (focus.DeliveryPending ? "   (DEPARTING)" : "") +
                (focus.IsPanicking ? "   (DEFENSIVE)" : "");

            string loadout = focus.LoadoutKnown
                ? "LOADOUT " + WingLoadoutCatalog.Label(definition, focus.Loadout) +
                  " - fitted at requisition"
                : "LOADOUT as found - assigned mission aircraft keep their own fit";

            string stats = crew != null
                ? "COMBAT RECORD   " + crew.Kills + " KILL(S)   /   " + crew.Sorties + " SORTIE(S)"
                : "";
            string persona = crew != null
                ? "RADIO PROFILE   " + crew.Persona.ToString().ToUpperInvariant()
                : "";

            SetWingDetail(identity, rank, stats, persona, progress,
                          crew != null ? crew.Background : "", type, state, order, loadout,
                          WeaponManifest(aircraft));

            if (airframeStateLabel != null)
            {
                bool poor = fuel <= Plugin.Config2.BingoFuel.Value ||
                            ammo <= 0 || integrity < 0.75f;
                airframeStateLabel.color = poor ? Warning() : Friendly();
            }

            // Nothing on this page writes to the aircraft, so an unreachable one is worth
            // saying rather than worth disabling controls over.
            if (airframeTypeLabel != null && aircraft != null && !aircraft.LocalSim)
                airframeTypeLabel.text = type + "   (NOT LOCALLY SIMULATED)";
        }

        private static void SetWingDetail(string identity, string rank, string stats,
                                          string persona, float progress, string background,
                                          string type, string state, string order, string loadout,
                                          string weapons)
        {
            if (pilotIdentityLabel != null) pilotIdentityLabel.text = identity;
            if (pilotRankLabel != null) pilotRankLabel.text = rank;
            if (pilotStatsLabel != null) pilotStatsLabel.text = stats;
            if (pilotPersonaLabel != null) pilotPersonaLabel.text = persona;
            if (pilotBackgroundLabel != null) pilotBackgroundLabel.text = background;
            if (airframeTypeLabel != null)
            {
                airframeTypeLabel.text = type;
                airframeTypeLabel.color = Friendly();
            }
            if (airframeStateLabel != null) airframeStateLabel.text = state;
            if (airframeOrderLabel != null) airframeOrderLabel.text = order;
            if (airframeLoadoutLabel != null) airframeLoadoutLabel.text = loadout;
            if (airframeWeaponsLabel != null) airframeWeaponsLabel.text = weapons;

            if (pilotXpBar != null)
                pilotXpBar.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(1f, pilotXpBarWidth * Mathf.Clamp01(progress)), 3f);
        }

        /// <summary>A compact live inventory, grouped by the weapon definition on each station.</summary>
        private static string WeaponManifest(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return "WEAPONS   —";

            var names = new List<string>();
            var ammo = new List<int>();
            var stations = new List<int>();

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo) continue;

                string name = station.WeaponInfo != null ? station.WeaponInfo.name : "STORE";
                if (string.IsNullOrEmpty(name)) name = "STORE";
                name = name.Replace("(Clone)", "").Replace("_", " ").Trim().ToUpperInvariant();

                int index = names.IndexOf(name);
                if (index < 0)
                {
                    names.Add(name);
                    ammo.Add(Mathf.Max(0, station.Ammo));
                    stations.Add(1);
                }
                else
                {
                    ammo[index] += Mathf.Max(0, station.Ammo);
                    stations[index]++;
                }
            }

            if (names.Count == 0) return "WEAPONS   UNARMED";

            string result = "WEAPONS   ";
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) result += "   ·   ";
                string count = stations[i] > 1 ? stations[i] + "x " : "";
                result += count + UiTheme.Truncate(names[i], 20) + "  [" + ammo[i] + "]";
            }
            return result;
        }

        // ------------------------------------------------------- shared roster plumbing

        private static AircraftDefinition DefinitionOf(WingMember member) =>
            member != null && member.Aircraft != null ? member.Aircraft.definition : null;

        /// <summary>
        /// The <c>&lt;</c> page <c>&gt;</c> strip under an inspection roster.
        ///
        /// MaxWingSize goes to eight, and a page built for three rows would otherwise hide
        /// the rest of a large wing with nothing on screen to say so. Both arrows go dead on
        /// a single page rather than looking available and doing nothing.
        /// </summary>
        private sealed class RosterPager
        {
            private readonly WingButton prev;
            private readonly WingButton next;
            private readonly TMP_Text label;

            public RosterPager(RectTransform parent, float y)
            {
                prev = Pager(parent, y, "<", () => Turn(-1));
                label = PagerLabel(parent, y);
                next = Pager(parent, y, ">", () => Turn(1));
            }

            private static void Turn(int direction) =>
                inspectPage = Mathf.Max(0, inspectPage + direction);

            /// <summary>Clamp against the live roster and return the first visible index.</summary>
            public int Refresh(WingRegistry wing)
            {
                int pages = Mathf.Max(1, Mathf.CeilToInt(wing.Count / (float)RosterRowsPerPage));
                inspectPage = Mathf.Clamp(inspectPage, 0, pages - 1);

                if (label != null)
                    label.text = "flight page " + (inspectPage + 1) + " of " + pages;

                prev?.SetEnabled(inspectPage > 0);
                next?.SetEnabled(inspectPage < pages - 1);

                return inspectPage * RosterRowsPerPage;
            }
        }

        private static void SyncPickRows(List<PickRow> rows, RectTransform area)
        {
            if (area == null) return;
            while (rows.Count < RosterRowsPerPage) rows.Add(new PickRow(area, rows.Count));
        }

        /// <summary>
        /// Inspect one wingman on the Wing tab.
        ///
        /// It used to also move the requisition selection onto that wingman's airframe,
        /// because the Loadout tab listed the flight and clicking a VT-7 there meant "set up
        /// the next VT-7". That page no longer lists anyone, so the side effect had nothing
        /// left to be convenient for.
        /// </summary>
        private static void Focus(WingMember member) => focusMember = member;

        /// <summary>
        /// A compact selectable roster line, shared by the Loadout and Wing tabs.
        ///
        /// Deliberately not the Tactical page's <see cref="RosterRow"/>: that row carries
        /// five live columns and a release button, and clicking it changes the command
        /// scope. These two pages need one column of detail and a selection that means
        /// "show me this one".
        /// </summary>
        private sealed class PickRow
        {
            private readonly GameObject go;
            private readonly TMP_Text slot, name, detail;
            private readonly Image selectionRule;
            private readonly Image fill;
            private readonly WingButton hit;
            private WingMember bound;

            public PickRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * RowPitch;

                go = new GameObject("Pick" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                fill = Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());

                hit = HitButton(rt, new Rect(0f, 0f, width, RowHeight), () =>
                {
                    if (bound != null) Focus(bound);
                });

                slot = Label(rt, "", new Rect(6f, 0f, 18f, RowHeight), Dim(), FontBody,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                name = Label(rt, "", new Rect(26f, 0f, 120f, RowHeight), WingColor(), FontBody,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, "", new Rect(150f, 0f, width - 150f - Space3, RowHeight), Dim(),
                               FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);

                go.SetActive(false);
            }

            public void Bind(WingMember member, string detailText)
            {
                bound = member;
                if (!go.activeSelf) go.SetActive(true);

                bool selected = focusMember == member;
                slot.text = member.Slot.ToString();
                slot.color = selected ? Green() : Dim();
                name.text = UiTheme.Truncate(member.Name, 18);
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();
                hit?.SetRowHighlight(fill,
                                     selected ? WingUi.CardFillSelected : WingUi.CardFill,
                                     WingUi.CardFillHover);
                detail.text = detailText;
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ UI helpers

        private static WingRegistry Wing() => WingCommandManager.Instance?.Wing;


        private static void CycleShape(int direction)
        {
            Plugin.Config2.Shape.Value = FormationShapes.CycleCore(Plugin.Config2.Shape.Value, direction);
        }

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

        private static Image Rule(RectTransform parent, Rect rect) => WingUi.Rule(parent, rect);

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

                // ---------------------------------------------------------------------- colours

        private static Color Green() => WingUi.Green;

        private static Color Grey() => WingUi.Grey;

        private static Color Accent() => WingUi.Green;

        private static Color Warning() => WingUi.Warning;

        private static Color Friendly() => WingUi.Friendly;

        private static Color WingColor() => WingMarkers.MemberColor;

        private static Color Dim() => WingUi.Dim;

        private static Color RowColor() => WingUi.Grey;
        private static Color MemberFrameColor() => WingColor().WithAlpha(0.58f);
        private static Color FrameColor() => WingUi.FrameColor;

        // ----------------------------------------------------------------------- text


        /// <summary>
        /// The order column, which names the target when there is one.
        ///
        /// With targets distributed across the wing, "ENGAGE" on four rows says nothing
        /// about who went after what. The target's own designation is the useful thing to
        /// read here, and it pairs with the amber marks on the map and HUD.
        /// </summary>
        private static string ShortOrder(WingMember m)
        {
            if (m.DeliveryPending) return "DEPT";
            if (m.IsPanicking) return "DEFENSIVE";

            // Splash 'Em keeps its own name rather than borrowing the target's. The
            // column is too narrow for both, and which of the two target orders a wingman
            // is flying is the thing that cannot be read anywhere else on this page - the
            // map already draws an amber line to the unit either way.
            if (m.Order == WingOrder.FireForEffect)
                return WingOrderCatalog.ShortLabel(m.Order);

            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
                return UiTheme.Truncate(assigned.definition != null ? assigned.definition.code : assigned.unitName, 8);

            return WingOrderCatalog.ShortLabel(m.Order);
        }
    }
}
