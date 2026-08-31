using System;
using System.Collections.Generic;
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
        private const float Pad = 12f;
        private const float RowHeight = 30f;
        private const float Gap = 4f;

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
        private static readonly List<PickRow> loadoutRows = new List<PickRow>();
        private static RectTransform loadoutRosterArea;
        private static TMP_Text loadoutAirframeLabel;
        private static TMP_Text loadoutStatusLabel;
        private static TMP_Text loadoutFlightNote;
        private static TMP_Text cargoLabel;
        private static WingButton cargoPrevButton;
        private static WingButton cargoNextButton;
        private static readonly List<WingButton> presetButtons = new List<WingButton>();
        private static readonly List<WingLoadoutPreset> presetScratch =
            new List<WingLoadoutPreset>();

        /// <summary>Every preset a button could ever stand for, in selector order.</summary>
        private static readonly WingLoadoutPreset[] AllPresets =
        {
            WingLoadoutPreset.Standard,
            WingLoadoutPreset.AirToAir,
            WingLoadoutPreset.AirToGround,
            WingLoadoutPreset.Balanced,
            WingLoadoutPreset.Cargo,
        };

        // --- Wing page ---
        private static readonly List<PickRow> wingRows = new List<PickRow>();
        private static RectTransform wingRosterArea;
        private static TMP_Text pilotIdentityLabel;
        private static TMP_Text pilotRankLabel;
        private static Image pilotXpBar;
        private static float pilotXpBarWidth;
        private static TMP_Text pilotBackgroundLabel;
        private static TMP_Text airframeTypeLabel;
        private static TMP_Text airframeStateLabel;
        private static TMP_Text airframeOrderLabel;
        private static TMP_Text airframeLoadoutLabel;

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

        private static RosterPager loadoutPager;
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

            // Refreshing rebuilds a formatted string per roster row; at frame rate that is
            // pure garbage for numbers a reader cannot follow that fast.
            if (screen.isActive && Time.unscaledTime >= nextRefresh)
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
            }

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

            loadoutRows.Clear();
            presetButtons.Clear();
            loadoutRosterArea = null;
            loadoutAirframeLabel = null;
            loadoutStatusLabel = null;
            loadoutFlightNote = null;
            cargoLabel = null;
            cargoPrevButton = null;
            cargoNextButton = null;
            loadoutPager = null;
            wingPager = null;
            inspectPage = 0;

            wingRows.Clear();
            wingRosterArea = null;
            pilotIdentityLabel = null;
            pilotRankLabel = null;
            pilotXpBar = null;
            pilotBackgroundLabel = null;
            airframeTypeLabel = null;
            airframeStateLabel = null;
            airframeOrderLabel = null;
            airframeLoadoutLabel = null;
            focusMember = null;

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
            tacticalY = AddCommandStatus(tacticalRoot, tacticalY);
            tacticalY = AddDebug(tacticalRoot, tacticalY);

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
            supplyY = AddDebug(supplyRoot, supplyY);

            float loadoutY = AddLoadoutPage(pageRoots[(int)Page.Loadout], y);
            float wingY = AddWingPage(pageRoots[(int)Page.Wing], y);

            // Each page is sized to its own content rather than all four to the tallest.
            // Supply and Tactical differ by nearly a third of the panel, and sharing one
            // height left the shorter one framing a block of empty space.
            pageHeights[(int)Page.Tactical] = Mathf.Abs(tacticalY) + Pad;
            pageHeights[(int)Page.Supply] = Mathf.Abs(supplyY) + Pad;
            pageHeights[(int)Page.Loadout] = Mathf.Abs(loadoutY) + Pad;
            pageHeights[(int)Page.Wing] = Mathf.Abs(wingY) + Pad;

            panelRect = rt;
            rt.sizeDelta = new Vector2(PanelWidth, pageHeights[(int)Page.Tactical]);

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
            Label(parent, "WING COMMAND", new Rect(Pad, y, PanelWidth - Pad * 2f, 26f),
                  Green(), 18f, FontStyles.Normal, TextAlignmentOptions.Center);
            y -= 30f;

            Rule(parent, new Rect(Pad, y, PanelWidth - Pad * 2f, 1f));
            return y - 8f;
        }

        /// <summary>
        /// One row of four, rather than the two rows a 2x2 grid would need.
        ///
        /// Four short words fit across the panel at this size, and a single row keeps the
        /// tabs where a two-tab player already expects them — the page below simply gains
        /// two more places to go rather than moving down the screen.
        /// </summary>
        private static float AddTabs(RectTransform parent, float y)
        {
            float w = (PanelWidth - Pad * 2f - Gap * (PageCount - 1)) / PageCount;

            pageTabs[(int)Page.Tactical] = Tab(parent, "TACTICAL", Page.Tactical, Pad, y, w);
            pageTabs[(int)Page.Supply] = Tab(parent, "SUPPLY", Page.Supply, Pad + w + Gap, y, w);
            pageTabs[(int)Page.Loadout] = Tab(parent, "LOADOUT", Page.Loadout,
                                              Pad + (w + Gap) * 2f, y, w);
            pageTabs[(int)Page.Wing] = Tab(parent, "WING", Page.Wing, Pad + (w + Gap) * 3f, y, w);

            return y - RowHeight - 8f;
        }

        private static WingButton Tab(RectTransform parent, string text, Page target,
                                      float x, float y, float w) =>
            WingUi.Button(parent, text, new Rect(x, y, w, RowHeight), 11f, () => SetPage(target));

        private static void SetPage(Page next)
        {
            page = next;

            for (int i = 0; i < PageCount; i++)
            {
                bool active = i == (int)next;
                if (pageRoots[i] != null) pageRoots[i].gameObject.SetActive(active);
                pageTabs[i]?.SetLatched(active);
            }

            if (panelRect != null)
                panelRect.sizeDelta = new Vector2(PanelWidth, pageHeights[(int)next]);

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

            const float gutter = 62f;
            float left = Pad + gutter;
            float w = PanelWidth - Pad - left;

            // Rules of engagement: three rungs, so three buttons. They are an escalation
            // rather than a toggle — each answers "the leader is being shot at" differently,
            // which is the whole reason there are three of them. Wing-wide.
            Gutter(parent, y, "ROE");
            float roeWidth = (w - Gap * 2f) / 3f;
            holdButton = Button(parent, "DEFEND", new Rect(left, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Hold));
            escortButton = Button(parent, "ESCORT",
                                  new Rect(left + roeWidth + Gap, y, roeWidth, RowHeight),
                                  () => SetRoe(WingRoe.Escort));
            freeButton = Button(parent, "FREE",
                                new Rect(left + (roeWidth + Gap) * 2f, y, roeWidth, RowHeight),
                                () => SetRoe(WingRoe.Free));
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
                    11f,
                    () => WingCommandManager.Instance?.SetWeaponPreference(preference));
            }
            y -= RowHeight + Gap;

            Gutter(parent, y, "FORM");
            Panel(parent, new Rect(left, y, w, RowHeight), RowColor());
            Button(parent, "<", new Rect(left + 4f, y - 3f, 26f, RowHeight - 6f),
                   () => CycleShape(-1));
            Button(parent, ">", new Rect(left + w - 30f, y - 3f, 26f, RowHeight - 6f),
                   () => CycleShape(1));
            shapeLabel = Label(parent, "", new Rect(left + 34f, y, w - 68f, RowHeight),
                               Friendly(), 12f, FontStyles.Normal, TextAlignmentOptions.Center);

            return y - (RowHeight + Gap);
        }

        /// <summary>The dim row label in the left gutter of the engagement block.</summary>
        private static void Gutter(RectTransform parent, float y, string text) =>
            Label(parent, text, new Rect(Pad, y, 58f, RowHeight), Dim(), 10f,
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
            summaryLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f - 90f, RowHeight),
                                 Friendly(), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            Button(parent, "SELECT ALL", new Rect(PanelWidth - Pad - 88f, y, 88f, RowHeight),
                   () => WingCommandManager.Instance?.SelectAllMembers());
            return y - RowHeight - Gap;
        }

        private static float AddRosterArea(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FLIGHT");

            // Column headers, so the numbers in each row are readable without guessing.
            float w = PanelWidth - Pad * 2f;
            Label(parent, "CALLSIGN", new Rect(Pad + 26f, y, 100f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "STATE", new Rect(Pad + 128f, y, 58f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "WPN", new Rect(Pad + 188f, y, 30f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "SLOT ERR", new Rect(Pad + 220f, y, 52f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Right);
            Label(parent, "FUEL  AMMO", new Rect(Pad + 276f, y, 70f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Right);
            y -= 16f;

            float h = (RowHeight + 2f) * RosterRowsPerPage;

            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, w, h));

            y -= h + Gap;

            rosterPrevButton = Button(parent, "<", new Rect(Pad, y, 34f, RowHeight),
                                      () => TurnRosterPage(-1));
            rosterPageLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 76f, RowHeight),
                                    Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            rosterNextButton = Button(parent, ">", new Rect(PanelWidth - Pad - 34f, y, 34f, RowHeight),
                                      () => TurnRosterPage(1));
            return y - RowHeight - Gap;
        }

        private static float AddActions(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ORDERS - SELECTED SCOPE");
            float w = (PanelWidth - Pad * 2f - Gap) * 0.5f;

            y = Pair(parent, y, w,
                "Form Up", () => WingCommandManager.Instance?.Execute(WingAction.Rejoin, wholeWing: false),
                "Attack", () => WingCommandManager.Instance?.Execute(WingAction.AttackMyTarget, wholeWing: false));

            y = Pair(parent, y, w,
                "Engage", () => WingCommandManager.Instance?.Execute(WingAction.Engage, wholeWing: false),
                "Disengage", () => WingCommandManager.Instance?.Execute(WingAction.FallBack, wholeWing: false));

            y = Pair(parent, y, w,
                "Hold Here", () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.OrbitHere),
                "Return To Base", () => WingCommandManager.Instance?.Execute(WingAction.ReturnToBase, wholeWing: false));

            cargoButton = Button(parent, "Deliver Cargo", new Rect(Pad, y, w, RowHeight),
                () => WingCommandManager.Instance?.Execute(WingAction.DeliverCargo, wholeWing: false));
            landButton = Button(parent, "Land Here", new Rect(Pad + w + Gap, y, w, RowHeight),
                () => WingCommandManager.Instance?.ArmPointOrder(WingOrder.LandHere));
            y -= RowHeight + Gap;

            return y;
        }

        private static float AddCommandStatus(RectTransform parent, float y)
        {
            commandStatusLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 18f),
                                       Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 20f;
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

            supplyFundsLabel = Label(parent, "", new Rect(Pad, y, half, 18f),
                                     Friendly(), 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            supplySquadronLabel = Label(parent, "", new Rect(Pad + half, y, half, 18f),
                                        Friendly(), 11f, FontStyles.Normal, TextAlignmentOptions.Right);
            return y - 22f;
        }

        private static float AddAssignment(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ACTIVE AIRCRAFT ASSIGNMENT");
            Label(parent,
                  "Select friendly AI on the map, then press twice to confirm the fee.",
                  new Rect(Pad, y, PanelWidth - Pad * 2f, 18f), Dim(), 10f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 22f;

            Button(parent, "Assign Selected",
                   new Rect(Pad, y, PanelWidth - Pad * 2f, RowHeight),
                   () => WingCommandManager.Instance?.AddSelectedFromMap());
            return y - (RowHeight + Gap);
        }

        private static float AddReserve(RectTransform parent, float y)
        {
            y = Heading(parent, y, "WING RESERVE");

            const float actionWidth = 104f;
            reserveReleaseButton = Button(
                parent, "RELEASE",
                new Rect(Pad, y, actionWidth, RowHeight), ReleaseSelectedReserve);
            reserveLabel = Label(
                parent, "",
                new Rect(Pad + actionWidth + Gap, y,
                         PanelWidth - Pad * 2f - (actionWidth + Gap) * 2f, RowHeight),
                Friendly(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            reserveHoldButton = Button(
                parent, "HOLD",
                new Rect(PanelWidth - Pad - actionWidth, y, actionWidth, RowHeight),
                HoldSelectedReserve);
            y -= RowHeight + 2f;

            reserveHintLabel = Label(parent, "",
                  new Rect(Pad, y, PanelWidth - Pad * 2f, 16f), Dim(), 10f,
                  FontStyles.Normal, TextAlignmentOptions.Center);
            return y - 22f;
        }

        private static void HoldSelectedReserve()
        {
            bool held = WingSupplyReserve.Hold(selectedOffer, out string reason);
            WingCommandManager.Instance?.Toast(held
                ? selectedOffer.unitName + " held for the wing (" + WingSupplyReserve.Count +
                  "/" + WingSupplyReserve.Capacity + ")"
                : reason);
        }

        private static void ReleaseSelectedReserve()
        {
            AircraftDefinition definition = selectedOffer;
            bool released = WingSupplyReserve.Release(
                definition, out bool wasOwned, out string reason);
            WingCommandManager.Instance?.Toast(released
                ? definition.unitName + (wasOwned ? " ownership released" : " released") +
                  " to faction stock"
                : reason);
        }

        private static void TurnRosterPage(int direction)
        {
            rosterPage = Mathf.Max(0, rosterPage + direction);
        }

        /// <summary>
        /// Debug tools, hidden unless the config asks for them. They are cheats, and a
        /// panel that shows them to everyone invites their use by accident.
        /// </summary>
        private static float AddDebug(RectTransform parent, float y)
        {
            if (!Plugin.Config2.EnableDebugActions.Value) return y;

            y -= 6f;
            Rule(parent, new Rect(Pad, y, PanelWidth - Pad * 2f, 1f), FrameColor());
            y -= 8f;

            y = Heading(parent, y, "DEBUG");

            float w = PanelWidth - Pad * 2f;

            Button(parent, "Spawn Wing Of My Aircraft", new Rect(Pad, y, w, RowHeight),
                   () => WingDebugActions.SpawnWingLikePlayer(Wing()));

            return y - RowHeight;
        }
        private static float Pair(RectTransform parent, float y, float w,
                                  string leftText, Action leftAction,
                                  string rightText, Action rightAction)
        {
            Button(parent, leftText, new Rect(Pad, y, w, RowHeight), leftAction);
            Button(parent, rightText, new Rect(Pad + w + Gap, y, w, RowHeight), rightAction);
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
                    break;

                case Page.Loadout:
                    RefreshLoadoutPage(wing);
                    break;

                case Page.Wing:
                    RefreshWingPage(wing);
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
                                    "   ·   WING " + wing.Count + "/" + Plugin.Config2.MaxWingSize.Value;

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

            if (commandStatusLabel != null)
            {
                // The map has first claim on this line: an armed point order or a pending
                // assignment fee is a live instruction, and the engagement hints are not.
                commandStatusLabel.text =
                    manager != null && manager.MapStatusIsNotice
                        ? manager.MapStatus
                        : EngagementHint(wing, shared);
            }

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

            y = Heading(parent, y, "AIRFRAME REQUISITION");

            // Page controls. The faction usually has more airframes in stock than fit on a
            // panel sized to its content, and silently showing only the cheapest few hid most
            // of the catalogue. Both arrows go dead on a single page rather than looking
            // available and doing nothing.
            float arrow = 34f;
            shopPrevButton = Button(parent, "<", new Rect(Pad, y, arrow, RowHeight),
                                    () => TurnPage(-1));
            shopPageLabel = Label(parent,
                                  "", new Rect(Pad + arrow + Gap, y,
                                               PanelWidth - Pad * 2f - (arrow + Gap) * 2f, RowHeight),
                                  Dim(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            shopNextButton = Button(parent, ">", new Rect(PanelWidth - Pad - arrow, y, arrow, RowHeight),
                                    () => TurnPage(1));
            y -= RowHeight + Gap;

            var area = new GameObject("ShopArea", typeof(RectTransform));
            shopArea = area.GetComponent<RectTransform>();
            shopArea.SetParent(parent, worldPositionStays: false);

            float height = ShopRows * (RowHeight + 2f);
            Place(shopArea, new Rect(Pad, y, PanelWidth - Pad * 2f, height));

            for (int i = 0; i < ShopRows; i++)
                shopRows.Add(new ShopRow(shopArea, i));

            y -= height + 6f;

            // The detail line gets the full width to itself. It used to share a row with the
            // requisition button and print the pricing formula to fit — "31 x 1.5^0 = 31" —
            // which is a thing to decode rather than a thing to read.
            offerDetailLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 16f),
                                     Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 18f;

            // What is actually being bought. A requisition now carries a loadout, and a
            // price/stock breakdown that named only the airframe would be describing half
            // the purchase.
            offerLoadoutLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 16f),
                                      Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 20f;

            float exceedWidth = PanelWidth - Pad * 2f - Gap - 130f;
            exceedLimitButton = Button(parent, "", new Rect(Pad, y, exceedWidth, RowHeight),
                                       ToggleExceedLimit);
            requisitionButton = Button(parent, "REQUISITION",
                                       new Rect(PanelWidth - Pad - 130f, y, 130f, RowHeight),
                                       RequisitionSelected);
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
            bool overLimit = WingShop.WouldExceedLimit;

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
                    float cost = WingShop.CurrentPriceOf(selectedOffer);
                    int reservedCount = WingSupplyReserve.CountOf(selectedOffer);
                    int ownedCount = WingSupplyReserve.OwnedOf(selectedOffer);
                    int stock = 0;
                    for (int i = 0; i < offers.Count; i++)
                    {
                        if (offers[i].Definition != selectedOffer) continue;
                        stock = offers[i].Stock;
                        break;
                    }

                    offerDetailLabel.text =
                        UiTheme.Truncate(selectedOffer.unitName, 18) +
                        "  ·  " + Mathf.RoundToInt(cost) + " funds" +
                        (overLimit ? " (over limit)" : "") +
                        "  ·  " + stock + " available" +
                        (ownedCount > 0 ? "  ·  " + ownedCount + " owned reserve" :
                         reservedCount > 0 ? "  ·  held in wing reserve" : "");
                    offerDetailLabel.color = WingShop.Allocation >= cost ? Friendly() : Warning();
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

                    if (WingSupplyReserve.NextSource(selectedOffer) != WingSupplyReserve.Source.None &&
                        WingLoadoutBook.PeekReserved(selectedOffer, out WingLoadoutChoice stored))
                    {
                        fit = stored;
                        fromReserve = true;
                    }

                    offerLoadoutLabel.text =
                        "FIT  " + WingLoadoutCatalog.Label(selectedOffer, fit) +
                        (fromReserve ? "  ·  as recovered" : "  ·  change it on LOADOUT");
                    offerLoadoutLabel.color = Dim();
                }
            }

            requisitionButton?.SetEnabled(selectedOffer != null &&
                WingShop.Allocation >= WingShop.CurrentPriceOf(selectedOffer));
        }

        /// <summary>Funds, wing size, and how much of the mission's AI aircraft cap is left.</summary>
        private static void RefreshSupplyStatus()
        {
            if (supplyFundsLabel == null) return;

            int wing = WingCommandManager.Instance?.Wing?.Count ?? 0;
            supplyFundsLabel.text = "FUNDS " + Mathf.RoundToInt(WingShop.Allocation) +
                                    "   ·   WING " + wing + " / " + Plugin.Config2.MaxWingSize.Value;

            WingShop.SquadronState squadron = WingShop.Squadron();
            string text = "SQUADRON " + squadron.Active + " / " + squadron.Limit;

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
            private AircraftDefinition bound;

            public ShopRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * (RowHeight + 2f);

                go = new GameObject("Shop" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                Panel(rt, new Rect(0f, 0f, width, RowHeight), RowColor());

                // The whole row selects, marked by a lit edge, exactly as the flight roster
                // on the Tactical page works. A per-row SELECT button spent a sixth of the
                // width restating what clicking the row would obviously do.
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), RowColor());
                HitButton(rt, new Rect(0f, 0f, width, RowHeight), () =>
                {
                    if (bound != null) selectedOffer = bound;
                });

                name  = Label(rt, "", new Rect(10f, 0f, 170f, RowHeight), Friendly(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                stock = Label(rt, "", new Rect(186f, 0f, 90f, RowHeight), Dim(), 11f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                price = Label(rt, "", new Rect(280f, 0f, width - 292f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Right);

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
            private WingMember bound;

            public RosterRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * (RowHeight + 2f);

                go = new GameObject("Row" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());

                HitButton(rt, new Rect(0f, 0f, width - 52f, RowHeight), () =>
                {
                    if (bound == null) return;
                    bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    WingCommandManager.Instance?.SelectMember(bound, toggle);
                });

                slot  = Label(rt, "", new Rect(6f, 0f, 18f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                name  = Label(rt, "", new Rect(26f, 0f, 100f, RowHeight), WingColor(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(128f, 0f, 58f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                preference = Label(rt, "", new Rect(188f, 0f, 30f, RowHeight), Dim(), 10f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                error = Label(rt, "", new Rect(220f, 0f, 52f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Right);
                reserves = Label(rt, "", new Rect(276f, 0f, 70f, RowHeight), Dim(), 11f,
                              FontStyles.Normal, TextAlignmentOptions.Right);

                Button(rt, "REL", new Rect(width - 48f, -3f, 42f, RowHeight - 6f), () =>
                {
                    if (bound != null) WingCommandManager.Instance?.RemoveMember(bound);
                });
            }

            public void Bind(WingMember m)
            {
                bound = m;
                if (!go.activeSelf) go.SetActive(true);

                bool selected = WingCommandManager.Instance?.Selection.Contains(m) ?? true;

                // Just the slot number. The filled/hollow circles this used to draw are not
                // in the MFD font, so every row rendered the same tofu box and the marker
                // said nothing — while the lit edge and the green callsign beside it were
                // already showing selection perfectly well.
                slot.text = m.Slot.ToString();
                slot.color = selected ? Green() : Dim();
                name.text = UiTheme.Truncate(m.Name, 16);
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();
                order.text = ShortOrder(m);

                // The weapon preference gets its own narrow column rather than being
                // appended to the state text. Sharing that cell would have truncated the
                // order — which is the more important of the two — the moment anything but
                // AUTO was selected.
                preference.text = WingWeaponPreferences.ShortLabel(m.WeaponPreference);
                preference.color = m.WeaponPreference == WingWeaponPreference.Auto
                    ? Dim()
                    : Accent();

                reserves.text = Mathf.RoundToInt(m.Fuel * 100f) + "%  " + m.Ammo;
                reserves.color = m.Fuel <= Plugin.Config2.BingoFuel.Value || m.Ammo <= 0
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
        /// Where a wingman's equipment is decided.
        ///
        /// The page is built around the one honest fact about aircraft loadouts in this
        /// game: they are fitted when the airframe is created and cannot be changed while it
        /// is flying. So the top half configures the <em>next</em> requisition, which is the
        /// only decision the player can actually make, and the bottom half reports what is
        /// already in the air and says plainly that it is fixed. Presenting both as editable
        /// and quietly ignoring one would be the worse lie.
        /// </summary>
        private static float AddLoadoutPage(RectTransform parent, float y)
        {
            y = Heading(parent, y, "NEXT REQUISITION");

            // The airframe here is the Supply tab's selection. Sharing it keeps the two
            // tabs describing one purchase: pick the aircraft on either page, configure it
            // on this one, buy it on that one.
            float w = PanelWidth - Pad * 2f;
            Panel(parent, new Rect(Pad, y, w, RowHeight), RowColor());
            Button(parent, "<", new Rect(Pad + 4f, y - 3f, 26f, RowHeight - 6f),
                   () => CycleOffer(-1));
            Button(parent, ">", new Rect(Pad + w - 30f, y - 3f, 26f, RowHeight - 6f),
                   () => CycleOffer(1));
            loadoutAirframeLabel = Label(parent, "", new Rect(Pad + 34f, y, w - 68f, RowHeight),
                                         Friendly(), 12f, FontStyles.Normal,
                                         TextAlignmentOptions.Center);
            y -= RowHeight + Gap;

            const float gutter = 62f;
            float left = Pad + gutter;
            float inner = PanelWidth - Pad - left;

            Gutter(parent, y, "FIT");
            float presetWidth = (inner - Gap * (AllPresets.Length - 1)) / AllPresets.Length;
            presetButtons.Clear();
            for (int i = 0; i < AllPresets.Length; i++)
            {
                WingLoadoutPreset preset = AllPresets[i];
                presetButtons.Add(WingUi.Button(
                    parent, WingLoadoutCatalog.Label(preset),
                    new Rect(left + (presetWidth + Gap) * i, y, presetWidth, RowHeight), 9f,
                    () => SetPreset(preset)));
            }
            y -= RowHeight + Gap;

            Gutter(parent, y, "CARGO");
            Panel(parent, new Rect(left, y, inner, RowHeight), RowColor());
            cargoPrevButton = Button(parent, "<", new Rect(left + 4f, y - 3f, 26f, RowHeight - 6f),
                                     () => CycleCargo(-1));
            cargoNextButton = Button(parent, ">",
                                     new Rect(left + inner - 30f, y - 3f, 26f, RowHeight - 6f),
                                     () => CycleCargo(1));
            cargoLabel = Label(parent, "", new Rect(left + 34f, y, inner - 68f, RowHeight),
                               Friendly(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            y -= RowHeight + 2f;

            loadoutStatusLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 16f),
                                       Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 20f;

            y = Heading(parent, y, "IN THE AIR");
            Label(parent, "CALLSIGN", new Rect(Pad + 26f, y, 120f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "CARRYING", new Rect(Pad + 160f, y, PanelWidth - Pad * 2f - 168f, 14f),
                  Dim(), 9f, FontStyles.Normal, TextAlignmentOptions.Right);
            y -= 16f;

            loadoutRosterArea = RosterViewport(parent, "LoadoutRoster", y);
            y -= (RowHeight + 2f) * RosterRowsPerPage + Gap;

            loadoutPager = new RosterPager(parent, y);
            y -= RowHeight + Gap;

            loadoutFlightNote = Label(parent, "",
                                      new Rect(Pad, y, PanelWidth - Pad * 2f, 32f), Dim(), 10f,
                                      FontStyles.Normal, TextAlignmentOptions.TopLeft);
            loadoutFlightNote.enableWordWrapping = true;
            return y - 36f;
        }

        /// <summary>A fixed-height area that roster rows are laid out inside.</summary>
        private static RectTransform RosterViewport(RectTransform parent, string name, float y)
        {
            var area = new GameObject(name, typeof(RectTransform));
            RectTransform rt = area.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, new Rect(Pad, y, PanelWidth - Pad * 2f,
                               (RowHeight + 2f) * RosterRowsPerPage));
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
        }

        private static void SetPreset(WingLoadoutPreset preset)
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            WingLoadoutCatalog.PresetsFor(selectedOffer, presetScratch);
            if (!presetScratch.Contains(preset))
            {
                WingCommandManager.Instance?.Toast(
                    selectedOffer.unitName + " has no stock stores for a " +
                    WingLoadoutCatalog.Label(preset) + " fit");
                return;
            }

            WingLoadoutChoice choice = WingLoadoutBook.PlannedFor(selectedOffer).WithPreset(preset);

            // A cargo fit with no cargo chosen takes the first the airframe offers, so the
            // preset is never left in a state that cannot be built.
            if (preset == WingLoadoutPreset.Cargo && choice.CargoKey == null &&
                WingLoadoutCatalog.ResolveCargo(selectedOffer, null,
                                                out WingLoadoutCatalog.CargoOption first))
                choice = choice.WithCargo(first.Key);

            WingLoadoutBook.Plan(selectedOffer, choice);
        }

        private static void CycleCargo(int direction)
        {
            if (selectedOffer == null) return;

            IReadOnlyList<WingLoadoutCatalog.CargoOption> options =
                WingLoadoutCatalog.CargoOptionsFor(selectedOffer);
            if (options.Count == 0) return;

            WingLoadoutChoice choice = WingLoadoutBook.PlannedFor(selectedOffer);

            int index = 0;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Key != choice.CargoKey) continue;
                index = i;
                break;
            }

            index = (index + direction + options.Count) % options.Count;
            WingLoadoutBook.Plan(selectedOffer, choice.WithCargo(options[index].Key));
        }

        private static void RefreshLoadoutPage(WingRegistry wing)
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

            WingLoadoutChoice planned = WingLoadoutBook.PlannedFor(selectedOffer);
            presetScratch.Clear();
            if (selectedOffer != null) WingLoadoutCatalog.PresetsFor(selectedOffer, presetScratch);

            for (int i = 0; i < presetButtons.Count; i++)
            {
                WingLoadoutPreset preset = AllPresets[i];
                presetButtons[i].SetEnabled(presetScratch.Contains(preset));
                presetButtons[i].SetLatched(planned.Preset == preset);
            }

            RefreshCargoSelector(planned);
            RefreshLoadoutStatus(planned);
            RefreshLoadoutRoster(wing);
        }

        private static void RefreshCargoSelector(WingLoadoutChoice planned)
        {
            bool cargoFit = selectedOffer != null && planned.Preset == WingLoadoutPreset.Cargo;
            bool hasOptions = selectedOffer != null && WingLoadoutCatalog.SupportsCargo(selectedOffer);

            cargoPrevButton?.SetEnabled(cargoFit && hasOptions);
            cargoNextButton?.SetEnabled(cargoFit && hasOptions);

            if (cargoLabel == null) return;

            if (!hasOptions)
            {
                cargoLabel.text = "NOT A TRANSPORT";
                cargoLabel.color = Dim();
                return;
            }

            WingLoadoutCatalog.ResolveCargo(selectedOffer, planned.CargoKey,
                                            out WingLoadoutCatalog.CargoOption option);
            cargoLabel.text = UiTheme.Truncate(option.Label, 26);
            cargoLabel.color = cargoFit ? Friendly() : Dim();
        }

        private static void RefreshLoadoutStatus(WingLoadoutChoice planned)
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

            if (!WingLoadoutCatalog.HasPresets(selectedOffer))
            {
                loadoutStatusLabel.text =
                    UiTheme.Truncate(selectedOffer.unitName, 18) +
                    " offers only its own standard fit.";
                loadoutStatusLabel.color = Dim();
                return;
            }

            loadoutStatusLabel.text =
                "Requisitions of " + UiTheme.Truncate(selectedOffer.unitName, 16) + " launch with " +
                WingLoadoutCatalog.Label(selectedOffer, planned) +
                (WingLoadoutBook.ReservedCount(selectedOffer) > 0
                    ? "  ·  a recovered airframe keeps what it came home with"
                    : "");
            loadoutStatusLabel.color = Friendly();
        }

        private static void RefreshLoadoutRoster(WingRegistry wing)
        {
            SyncPickRows(loadoutRows, loadoutRosterArea);
            int first = loadoutPager != null ? loadoutPager.Refresh(wing) : 0;

            for (int i = 0; i < loadoutRows.Count; i++)
            {
                int index = first + i;
                if (index >= wing.Count)
                {
                    loadoutRows[i].Hide();
                    continue;
                }

                WingMember member = wing.Members[index];
                loadoutRows[i].Bind(member, member.LoadoutKnown
                    ? WingLoadoutCatalog.Label(DefinitionOf(member), member.Loadout)
                    : "AS FOUND");
            }

            if (loadoutFlightNote == null) return;

            loadoutFlightNote.text = wing.Count == 0
                ? "No wingmen assigned. Requisition one from SUPPLY, or assign an active " +
                  "mission aircraft from the map."
                : "Equipment is fitted at launch and cannot be changed in flight. An " +
                  "assigned mission aircraft flies as found; send a wingman home with RTB " +
                  "and its airframe keeps this fit into the wing reserve.";
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
            Label(parent, "CALLSIGN", new Rect(Pad + 26f, y, 120f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "STATE", new Rect(Pad + 160f, y, PanelWidth - Pad * 2f - 168f, 14f),
                  Dim(), 9f, FontStyles.Normal, TextAlignmentOptions.Right);
            y -= 16f;

            wingRosterArea = RosterViewport(parent, "WingRoster", y);
            y -= (RowHeight + 2f) * RosterRowsPerPage + Gap;

            wingPager = new RosterPager(parent, y);
            y -= RowHeight + Gap;

            y = Heading(parent, y, "PILOT");
            float w = PanelWidth - Pad * 2f;

            pilotIdentityLabel = Label(parent, "", new Rect(Pad, y, w, 18f), Green(), 13f,
                                       FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 18f;

            pilotRankLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Friendly(), 11f,
                                   FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 16f;

            // Track first, fill second: the fill is resized every refresh, so it must be the
            // later sibling or a full bar would be drawn underneath its own background.
            Rule(parent, new Rect(Pad, y, w, 3f), FrameColor());
            pilotXpBar = Rule(parent, new Rect(Pad, y, w, 3f), Accent());
            pilotXpBarWidth = w;
            y -= 10f;

            pilotBackgroundLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Dim(), 10f,
                                         FontStyles.Italic, TextAlignmentOptions.Left);
            y -= 20f;

            y = Heading(parent, y, "AIRFRAME");

            airframeTypeLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Friendly(), 11f,
                                      FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 18f;
            airframeStateLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Friendly(), 11f,
                                       FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 18f;
            airframeOrderLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Friendly(), 11f,
                                       FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 18f;
            airframeLoadoutLabel = Label(parent, "", new Rect(Pad, y, w, 16f), Dim(), 10f,
                                         FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 20f;
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
                SetWingDetail("NO WINGMEN ASSIGNED", "", 0f, "", "", "", "", "");
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
            else if (crew.Rank >= WingPilotRoster.TopRank)
            {
                rank = WingPilotRoster.RankName(crew.Rank) + "   XP " + crew.Xp +
                       "   " + crew.Kills + " KILL(S)   " + crew.Sorties + " SORTIE(S)";
                progress = 1f;
            }
            else
            {
                int floor = WingPilotRoster.XpForRank(crew.Rank);
                int ceiling = WingPilotRoster.XpForRank(crew.Rank + 1);
                rank = WingPilotRoster.RankName(crew.Rank) + "   XP " + crew.Xp + " / " + ceiling +
                       "   " + crew.Kills + " KILL(S)   " + crew.Sorties + " SORTIE(S)";
                progress = ceiling > floor
                    ? Mathf.Clamp01((crew.Xp - floor) / (float)(ceiling - floor))
                    : 0f;
            }

            Aircraft aircraft = focus.Aircraft;
            AircraftDefinition definition = DefinitionOf(focus);

            string type = definition != null
                ? UiTheme.Truncate(definition.unitName, 22) + "   SLOT " + focus.Slot
                : "AIRFRAME   SLOT " + focus.Slot;

            string state =
                "FUEL " + Mathf.RoundToInt(focus.Fuel * 100f) + "%" +
                "   AMMO " + focus.Ammo +
                "   HULL " + Mathf.RoundToInt(focus.Integrity * 100f) + "%" +
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

            SetWingDetail(identity, rank, progress,
                          crew != null ? crew.Background : "", type, state, order, loadout);

            if (airframeStateLabel != null)
            {
                bool poor = focus.Fuel <= Plugin.Config2.BingoFuel.Value ||
                            focus.Ammo <= 0 || focus.Integrity < 0.75f;
                airframeStateLabel.color = poor ? Warning() : Friendly();
            }

            // Nothing on this page writes to the aircraft, so an unreachable one is worth
            // saying rather than worth disabling controls over.
            if (airframeTypeLabel != null && aircraft != null && !aircraft.LocalSim)
                airframeTypeLabel.text = type + "   (NOT LOCALLY SIMULATED)";
        }

        private static void SetWingDetail(string identity, string rank, float progress,
                                          string background, string type, string state,
                                          string order, string loadout)
        {
            if (pilotIdentityLabel != null) pilotIdentityLabel.text = identity;
            if (pilotRankLabel != null) pilotRankLabel.text = rank;
            if (pilotBackgroundLabel != null) pilotBackgroundLabel.text = background;
            if (airframeTypeLabel != null)
            {
                airframeTypeLabel.text = type;
                airframeTypeLabel.color = Friendly();
            }
            if (airframeStateLabel != null) airframeStateLabel.text = state;
            if (airframeOrderLabel != null) airframeOrderLabel.text = order;
            if (airframeLoadoutLabel != null) airframeLoadoutLabel.text = loadout;

            if (pilotXpBar != null)
                pilotXpBar.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(1f, pilotXpBarWidth * Mathf.Clamp01(progress)), 3f);
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
                float w = PanelWidth - Pad * 2f;

                prev = Button(parent, "<", new Rect(Pad, y, 34f, RowHeight), () => Turn(-1));
                label = Label(parent, "", new Rect(Pad + 38f, y, w - 76f, RowHeight), Dim(), 10f,
                              FontStyles.Normal, TextAlignmentOptions.Center);
                next = Button(parent, ">", new Rect(PanelWidth - Pad - 34f, y, 34f, RowHeight),
                              () => Turn(1));
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
        /// Inspect one wingman on the Loadout and Wing tabs.
        ///
        /// Picking a member on the Loadout page also moves the requisition selection onto
        /// its airframe, which is nearly always what was meant: the reason to click a VT-7
        /// on that page is to configure the next VT-7.
        /// </summary>
        private static void Focus(WingMember member)
        {
            focusMember = member;

            if (page != Page.Loadout) return;

            AircraftDefinition definition = DefinitionOf(member);
            if (definition != null) selectedOffer = definition;
        }

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
            private WingMember bound;

            public PickRow(RectTransform parent, int index)
            {
                float width = parent.rect.width;
                float y = -index * (RowHeight + 2f);

                go = new GameObject("Pick" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, new Rect(0f, y, width, RowHeight));

                Panel(rt, new Rect(0f, 0f, width, RowHeight), MemberFrameColor());
                selectionRule = Rule(rt, new Rect(0f, 0f, 3f, RowHeight), WingColor());

                HitButton(rt, new Rect(0f, 0f, width, RowHeight), () =>
                {
                    if (bound != null) Focus(bound);
                });

                slot = Label(rt, "", new Rect(6f, 0f, 18f, RowHeight), Dim(), 12f,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                name = Label(rt, "", new Rect(26f, 0f, 120f, RowHeight), WingColor(), 12f,
                             FontStyles.Normal, TextAlignmentOptions.Left);
                detail = Label(rt, "", new Rect(150f, 0f, width - 158f, RowHeight), Dim(), 11f,
                               FontStyles.Normal, TextAlignmentOptions.Right);

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

            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
                return UiTheme.Truncate(assigned.definition != null ? assigned.definition.code : assigned.unitName, 8);

            return WingOrderCatalog.ShortLabel(m.Order);
        }
    }
}
