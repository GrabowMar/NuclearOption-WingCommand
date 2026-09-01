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
    internal static partial class WmcScreen
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
        private static WingButton holdButton;
        private static WingButton escortButton;
        private static WingButton freeButton;
        private static WingButton cargoButton;
        private static WingButton landButton;
        private static WingButton jamButton;
        private static readonly WingButton[] preferenceButtons =
            new WingButton[WingWeaponPreferences.All.Length];


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
                nextRefresh = Time.unscaledTime + WingBrain.Interval(0.2f);
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
            jamButton = null;

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

        private static readonly Column[] RosterColumns =
        {
            new Column("CALLSIGN", 26f, 100f),
            new Column("STATE", 128f, 58f),
            new Column("WPN", 188f, 30f),
            new Column("SLOT ERR", 220f, 52f, rightAligned: true),
            new Column("FUEL  AMMO", 276f, 70f, rightAligned: true),
        };

        /// <summary>The two-column header the Wing tab's pick list uses.</summary>
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
            WingFormation.Shape = FormationShapes.CycleCore(WingFormation.Shape, direction);
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
