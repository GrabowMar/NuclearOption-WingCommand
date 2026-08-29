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
        private const float PanelHeight = 620f;
        private const float Pad = 12f;
        private const float RowHeight = 30f;
        private const float Gap = 4f;
        private const int RosterRowsPerPage = 4;

        private enum Page
        {
            Tactical,
            Supply,
        }

        private static MFDScreen screen;
        private static RectTransform tacticalRoot;
        private static RectTransform supplyRoot;
        private static Page page;
        private static WmcButton tacticalTab;
        private static WmcButton supplyTab;
        private static RectTransform rosterArea;
        private static TMP_Text shapeLabel;
        private static TMP_Text summaryLabel;
        private static TMP_Text rosterPageLabel;
        private static TMP_Text commandStatusLabel;
        private static TMP_Text postureLabel;
        private static WmcButton holdButton;
        private static WmcButton escortButton;
        private static WmcButton freeButton;
        private static WmcButton cargoButton;
        private static WmcButton landButton;
        private static TMP_FontAsset font;

        private static readonly List<RosterRow> rosterRows = new List<RosterRow>();
        private static readonly List<ShopRow> shopRows = new List<ShopRow>();
        private static RectTransform shopArea;
        private static TMP_Text allocationLabel;
        private static TMP_Text shopPageLabel;
        private static TMP_Text reserveLabel;
        private static TMP_Text offerDetailLabel;
        private static WmcButton requisitionButton;
        private static AircraftDefinition selectedOffer;
        private static int shopPage;
        private static int rosterPage;

        /// <summary>Rows built for the shop. Bounded: the panel is sized to its content.</summary>
        private const int ShopRows = 6;

        private static float nextAttempt;
        private static float nextRefresh;
        private static bool gaveUp;
        private static Sprite panelSprite;

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
            tacticalRoot = null;
            supplyRoot = null;
            page = Page.Tactical;
            tacticalTab = null;
            supplyTab = null;
            rosterArea = null;
            shapeLabel = null;
            summaryLabel = null;
            rosterPageLabel = null;
            commandStatusLabel = null;
            rosterRows.Clear();
            shopRows.Clear();
            shopArea = null;
            allocationLabel = null;
            shopPageLabel = null;
            reserveLabel = null;
            offerDetailLabel = null;
            requisitionButton = null;
            selectedOffer = null;
            shopPage = 0;
            rosterPage = 0;
            postureLabel = null;
            holdButton = null;
            escortButton = null;
            freeButton = null;
            cargoButton = null;
            landButton = null;
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
            font = FindFont(template);

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
            bg.sprite = VanillaPanelSprite();
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

            tacticalRoot = PageRoot(contentRt, "TacticalPage");
            supplyRoot = PageRoot(contentRt, "SupplyPage");

            float tacticalY = y;
            tacticalY = AddSummary(tacticalRoot, tacticalY);
            tacticalY = AddRosterArea(tacticalRoot, tacticalY);
            tacticalY = AddPostureSelector(tacticalRoot, tacticalY);
            tacticalY = AddShapeSelector(tacticalRoot, tacticalY);
            tacticalY = AddActions(tacticalRoot, tacticalY);
            tacticalY = AddCommandStatus(tacticalRoot, tacticalY);
            tacticalY = AddDebug(tacticalRoot, tacticalY);

            float supplyY = y;
            supplyY = AddAssignment(supplyRoot, supplyY);
            supplyY = AddReserve(supplyRoot, supplyY);
            supplyY = AddShop(supplyRoot, supplyY);
            supplyY = AddDebug(supplyRoot, supplyY);

            y = Mathf.Min(tacticalY, supplyY);

            // Size the panel to its content instead of leaving a large dead area below,
            // which is how the stock screens read.
            rt.sizeDelta = new Vector2(PanelWidth, Mathf.Abs(y) + Pad);

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

        private static float AddTabs(RectTransform parent, float y)
        {
            float w = (PanelWidth - Pad * 2f - Gap) * 0.5f;
            tacticalTab = Button(parent, "TACTICAL", new Rect(Pad, y, w, RowHeight),
                                 () => SetPage(Page.Tactical));
            supplyTab = Button(parent, "SUPPLY", new Rect(Pad + w + Gap, y, w, RowHeight),
                               () => SetPage(Page.Supply));
            return y - RowHeight - 8f;
        }

        private static void SetPage(Page next)
        {
            page = next;
            if (tacticalRoot != null) tacticalRoot.gameObject.SetActive(next == Page.Tactical);
            if (supplyRoot != null) supplyRoot.gameObject.SetActive(next == Page.Supply);
            tacticalTab?.SetLatched(next == Page.Tactical);
            supplyTab?.SetLatched(next == Page.Supply);

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
        private static float Heading(RectTransform parent, float y, string text)
        {
            float labelWidth = 8f * text.Length + 10f;

            Label(parent, text, new Rect(Pad, y, labelWidth, 16f),
                  Green(), 11f, FontStyles.Normal, TextAlignmentOptions.Left);

            float ruleX = Pad + labelWidth + 6f;
            Rule(parent, new Rect(ruleX, y - 8f, PanelWidth - Pad - ruleX, 1f), FrameColor());

            return y - 20f;
        }

        private static float AddShapeSelector(RectTransform parent, float y)
        {
            y = Heading(parent, y, "FORMATION");
            float w = PanelWidth - Pad * 2f;

            Panel(parent, new Rect(Pad, y, w, RowHeight), RowColor());

            Button(parent, "<", new Rect(Pad + 4f, y - 3f, 30f, RowHeight - 6f),
                   () => CycleShape(-1));
            Button(parent, ">", new Rect(Pad + w - 34f, y - 3f, 30f, RowHeight - 6f),
                   () => CycleShape(1));

            shapeLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 76f, RowHeight),
                               Friendly(), 13f, FontStyles.Normal, TextAlignmentOptions.Center);

            return y - (RowHeight + Gap);
        }

        private static float AddPostureSelector(RectTransform parent, float y)
        {
            y = Heading(parent, y, "RULES OF ENGAGEMENT");

            // Three rungs, so three buttons. They are an escalation rather than a toggle:
            // each answers "the leader is being shot at" differently, which is the whole
            // reason there are three of them.
            float w = (PanelWidth - Pad * 2f - Gap * 2f) / 3f;

            holdButton = Button(parent, "DEFEND", new Rect(Pad, y, w, RowHeight),
                                () => SetRoe(WingRoe.Hold));
            escortButton = Button(parent, "ESCORT", new Rect(Pad + w + Gap, y, w, RowHeight),
                                  () => SetRoe(WingRoe.Escort));
            freeButton = Button(parent, "FREE", new Rect(Pad + (w + Gap) * 2f, y, w, RowHeight),
                                () => SetRoe(WingRoe.Free));

            y -= RowHeight + 2f;

            postureLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 16f),
                                 Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            return y - 22f;
        }

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
            Label(parent, "CALLSIGN", new Rect(Pad + 26f, y, 108f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "STATE", new Rect(Pad + 136f, y, 62f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            Label(parent, "SLOT ERR", new Rect(Pad + 198f, y, 62f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Right);
            Label(parent, "FUEL  AMMO", new Rect(Pad + 264f, y, 70f, 14f), Dim(), 9f,
                  FontStyles.Normal, TextAlignmentOptions.Right);
            y -= 16f;

            float h = (RowHeight + 2f) * RosterRowsPerPage;

            var area = new GameObject("Roster", typeof(RectTransform));
            rosterArea = area.GetComponent<RectTransform>();
            rosterArea.SetParent(parent, worldPositionStays: false);
            Place(rosterArea, new Rect(Pad, y, w, h));

            y -= h + Gap;

            Button(parent, "<", new Rect(Pad, y, 34f, RowHeight), () => TurnRosterPage(-1));
            rosterPageLabel = Label(parent, "", new Rect(Pad + 38f, y, w - 76f, RowHeight),
                                    Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            Button(parent, ">", new Rect(PanelWidth - Pad - 34f, y, 34f, RowHeight),
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

        private static float AddAssignment(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ACTIVE AIRCRAFT ASSIGNMENT");
            Label(parent,
                  "Select friendly AI on the stock map, then confirm its assignment fee.",
                  new Rect(Pad, y, PanelWidth - Pad * 2f, 18f), Dim(), 10f,
                  FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 22f;

            float w = (PanelWidth - Pad * 2f - Gap) * 0.5f;
            return Pair(parent, y, w,
                "Assign Selected", () => WingCommandManager.Instance?.AddSelectedFromMap(),
                "Assign Nearest", () => WingCommandManager.Instance?.Execute(WingAction.RecruitNearest));
        }

        private static float AddReserve(RectTransform parent, float y)
        {
            y = Heading(parent, y, "PLAYER RESERVE");
            reserveLabel = Label(parent, "", new Rect(Pad + 38f, y, PanelWidth - Pad * 2f - 76f, RowHeight),
                                 Friendly(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            Button(parent, "-", new Rect(Pad, y, 34f, RowHeight), () => ChangeReserve(-1));
            Button(parent, "+", new Rect(PanelWidth - Pad - 34f, y, 34f, RowHeight),
                   () => ChangeReserve(1));
            y -= RowHeight + 2f;
            Label(parent, "Held from native AI deployment; stock is not created.",
                  new Rect(Pad, y, PanelWidth - Pad * 2f, 16f), Dim(), 10f,
                  FontStyles.Normal, TextAlignmentOptions.Center);
            return y - 22f;
        }

        private static void ChangeReserve(int delta)
        {
            int value = Mathf.Clamp(Plugin.Config2.AdditionalWingReserve.Value + delta, 0, 2);
            Plugin.Config2.AdditionalWingReserve.Value = value;
            WingCommandManager.Instance?.Toast("Additional player reserve: " + value + " per aircraft type");
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

        private static void Refresh(WingRegistry wing)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            if (shapeLabel != null)
                shapeLabel.text = FormationShapes.Pretty(Plugin.Config2.Shape.Value);

            if (summaryLabel != null)
                summaryLabel.text = "COMMAND: " + (manager?.Selection.Summary(wing) ?? "ALL") +
                                    "   ·   WING " + wing.Count + "/" + Plugin.Config2.MaxWingSize.Value;

            if (postureLabel != null)
                postureLabel.text = RoeRules.Hint(wing.Roe);

            holdButton?.SetLatched(wing.Roe == WingRoe.Hold);
            escortButton?.SetLatched(wing.Roe == WingRoe.Escort);
            freeButton?.SetLatched(wing.Roe == WingRoe.Free);

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

            if (reserveLabel != null)
            {
                reserveLabel.text = "Mission " + WingSupplyReserve.MissionReserve +
                                    "  +  Player " + WingSupplyReserve.Additional +
                                    "  =  " + WingSupplyReserve.EffectiveProtectedPerType + " / TYPE";
            }

            if (commandStatusLabel != null)
                commandStatusLabel.text = manager?.MapStatus ?? "Select wingmen, then issue an order.";

            RefreshShop();

            int pages = Mathf.Max(1, Mathf.CeilToInt(wing.Count / (float)RosterRowsPerPage));
            rosterPage = Mathf.Clamp(rosterPage, 0, pages - 1);
            if (rosterPageLabel != null)
                rosterPageLabel.text = "flight page " + (rosterPage + 1) + " of " + pages;

            SyncRosterRows(RosterRowsPerPage);
            int first = rosterPage * RosterRowsPerPage;

            for (int i = 0; i < rosterRows.Count; i++)
            {
                int index = first + i;
                if (index < wing.Count) rosterRows[i].Bind(wing.Members[index]);
                else rosterRows[i].Hide();
            }
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
        /// The shop: allocation, delivery mode, and a row per airframe the faction has in
        /// stock.
        ///
        /// A fixed set of rows is built once and rebound each refresh, the same way the
        /// roster works — building UI objects on a timer is how a screen like this starts
        /// costing frames. The list is capped because the panel is sized to its content and
        /// an unbounded catalogue would run off the display.
        /// </summary>
        private static float AddShop(RectTransform parent, float y)
        {
            if (!Plugin.Config2.ShopEnabled.Value) return y;

            y = Heading(parent, y, "AIRFRAME REQUISITION");

            allocationLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, 16f),
                                    Dim(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            y -= 20f;

            // Page controls. The faction usually has more airframes in stock than fit on a
            // panel sized to its content, and silently showing only the cheapest six hid
            // most of the catalogue.
            float arrow = 34f;
            Button(parent, "<", new Rect(Pad, y, arrow, RowHeight), () => TurnPage(-1));
            shopPageLabel = Label(parent,
                                  "", new Rect(Pad + arrow + Gap, y,
                                               PanelWidth - Pad * 2f - (arrow + Gap) * 2f, RowHeight),
                                  Dim(), 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            Button(parent, ">", new Rect(PanelWidth - Pad - arrow, y, arrow, RowHeight),
                   () => TurnPage(1));
            y -= RowHeight + Gap;

            var area = new GameObject("ShopArea", typeof(RectTransform));
            shopArea = area.GetComponent<RectTransform>();
            shopArea.SetParent(parent, worldPositionStays: false);

            float height = ShopRows * (RowHeight + 2f);
            Place(shopArea, new Rect(Pad, y, PanelWidth - Pad * 2f, height));

            for (int i = 0; i < ShopRows; i++)
                shopRows.Add(new ShopRow(shopArea, i));

            y -= height + 8f;

            offerDetailLabel = Label(parent, "Select an airframe to inspect its final cost.",
                                     new Rect(Pad, y, PanelWidth - Pad * 2f - 114f, RowHeight),
                                     Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            requisitionButton = Button(parent, "REQUISITION",
                                       new Rect(PanelWidth - Pad - 110f, y, 110f, RowHeight),
                                       RequisitionSelected);
            y -= RowHeight + Gap;
            return y;
        }

        private static void TurnPage(int direction)
        {
            shopPage += direction;
            if (shopPage < 0) shopPage = 0;
        }

        private static void SetDelivery(WingShop.Delivery mode)
        {
            if (WingShop.Mode != mode) WingShop.ToggleDelivery();
        }

        private static void RequisitionSelected()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            bool bought = WingShop.Buy(selectedOffer, WingShop.Delivery.Base, out string why);
            WingCommandManager.Instance?.Toast(bought
                ? selectedOffer.unitName + " requisitioned - departing friendly base"
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

            if (allocationLabel != null)
            {
                allocationLabel.text =
                    "Funds " + Mathf.RoundToInt(WingShop.Allocation) +
                    "   -   delivery BASE" +
                    "   -   wing " + (WingCommandManager.Instance?.Wing?.Count ?? 0);
            }

            if (shopPageLabel != null)
            {
                shopPageLabel.text = offers.Count == 0
                    ? "nothing in stock for your airframe"
                    : "page " + (shopPage + 1) + " of " + pages + "   (" + offers.Count + " types)";
            }

            int first = shopPage * ShopRows;

            for (int i = 0; i < shopRows.Count; i++)
            {
                int index = first + i;
                if (index < offers.Count) shopRows[i].Bind(offers[index]);
                else shopRows[i].Hide();
            }

            bool selectedStillOffered = false;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Definition == selectedOffer)
                {
                    selectedStillOffered = true;
                    break;
                }
            }
            if (!selectedStillOffered) selectedOffer = null;

            if (offerDetailLabel != null)
            {
                if (selectedOffer == null)
                {
                    offerDetailLabel.text = "Select an airframe to inspect its final cost.";
                    offerDetailLabel.color = Dim();
                }
                else
                {
                    float cost = WingShop.PriceOf(selectedOffer, WingShop.Delivery.Base);
                    int protectedCount = WingSupplyReserve.ProtectedFromAi(selectedOffer);
                    int wingSize = WingCommandManager.Instance?.Wing?.Count ?? 0;
                    offerDetailLabel.text = UiTheme.Truncate(selectedOffer.unitName, 18) +
                                            "  " + Mathf.RoundToInt(selectedOffer.value) +
                                            " × " + Plugin.Config2.WingPriceGrowth.Value.ToString("0.##") +
                                            "^" + wingSize + " = " + Mathf.RoundToInt(cost) +
                                            (protectedCount > 0 ? "  ·  " + protectedCount + " held" : "");
                    offerDetailLabel.color = WingShop.Allocation >= cost ? Friendly() : Warning();
                }
            }
            requisitionButton?.SetEnabled(selectedOffer != null &&
                WingShop.Allocation >= WingShop.PriceOf(selectedOffer, WingShop.Delivery.Base));
        }
        /// <summary>One purchasable airframe: name, stock, price, buy.</summary>
        private sealed class ShopRow
        {
            private readonly GameObject go;
            private readonly TMP_Text name, stock, price;
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

                name  = Label(rt, "", new Rect(6f, 0f, 150f, RowHeight), Friendly(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                stock = Label(rt, "", new Rect(158f, 0f, 60f, RowHeight), Dim(), 11f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                price = Label(rt, "", new Rect(218f, 0f, 110f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Right);

                Button(rt, "SELECT", new Rect(width - 66f, -3f, 60f, RowHeight - 6f), () =>
                {
                    if (bound == null) return;
                    selectedOffer = bound;
                });

                go.SetActive(false);
            }

            public void Bind(WingShop.Offer offer)
            {
                bound = offer.Definition;
                if (!go.activeSelf) go.SetActive(true);

                float cost = WingShop.PriceOf(offer.Definition, WingShop.Delivery.Base);
                bool affordable = WingShop.Allocation >= cost;

                name.text = UiTheme.Truncate(offer.Name, 20);
                stock.text = "x" + offer.Stock;
                price.text = Mathf.RoundToInt(cost).ToString();

                // Grey the price when it cannot be met, so the constraint reads at a glance
                // rather than only on a failed press.
                price.color = affordable ? Accent() : new Color(1f, 0.55f, 0.2f);
                name.color = affordable ? Friendly() : Dim();
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
            private readonly TMP_Text slot, name, order, error, reserves;
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
                name  = Label(rt, "", new Rect(26f, 0f, 108f, RowHeight), WingColor(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                order = Label(rt, "", new Rect(136f, 0f, 62f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Left);
                error = Label(rt, "", new Rect(198f, 0f, 62f, RowHeight), Dim(), 12f,
                              FontStyles.Normal, TextAlignmentOptions.Right);
                reserves = Label(rt, "", new Rect(264f, 0f, 70f, RowHeight), Dim(), 11f,
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
                slot.text = selected ? "●" + m.Slot : "○" + m.Slot;
                name.text = UiTheme.Truncate(m.Name, 16);
                name.color = selected ? Green() : WingColor();
                selectionRule.color = selected ? Green() : MemberFrameColor();
                order.text = ShortOrder(m);

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
                if (m.IsPanicking || m.Order != WingOrder.Formation) return "-";
                if (m.SlotError <= 0f) return "...";
                return m.SlotError < 10000f
                    ? m.SlotError.ToString("F0") + " m"
                    : (m.SlotError / 1000f).ToString("F1") + " km";
            }
        }

        // ------------------------------------------------------------------ UI helpers

        private static WingRegistry Wing() => WingCommandManager.Instance?.Wing;


        private static void CycleShape(int direction)
        {
            Plugin.Config2.Shape.Value = FormationShapes.CycleCore(Plugin.Config2.Shape.Value, direction);
        }

        private static TMP_Text Label(RectTransform parent, string text, Rect rect,
                                      Color color, float size, FontStyles style,
                                      TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.color = color;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>
        /// A framed box. Uses the sprite lifted from a HUD OPTIONS row when available, so
        /// the corners and edge weight match the stock panels exactly; otherwise it falls
        /// back to four hairlines, which reads the same at this size.
        /// </summary>
        private static Image Panel(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);
            rt.SetAsFirstSibling();

            // Interior stays near-transparent and the edges carry the colour — the stock
            // panels are outlined boxes, not filled blocks.
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0f, 0f, 0f, 0.25f);

            Outline(parent, rect, color);
            return img;
        }

        /// <summary>Four hairline edges forming a box.</summary>
        private static Image[] Outline(RectTransform parent, Rect rect, Color color)
        {
            const float t = 1f;
            return new[]
            {
                Rule(parent, new Rect(rect.x, rect.y, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y - rect.height + t, rect.width, t), color),
                Rule(parent, new Rect(rect.x, rect.y, t, rect.height), color),
                Rule(parent, new Rect(rect.x + rect.width - t, rect.y, t, rect.height), color),
            };
        }

        private static Image Rule(RectTransform parent, Rect rect) => Rule(parent, rect, Green());

        private static Image Rule(RectTransform parent, Rect rect, Color color)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// An outlined button in the stock idiom: grey frame and text at rest, green when
        /// hovered — the same on/off treatment HUD OPTIONS gives its MAXIMIZE buttons.
        /// </summary>
        private static WmcButton Button(RectTransform parent, string text, Rect rect, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            // A near-transparent fill keeps the whole rect clickable while the frame does
            // the drawing, so the button reads as an outline like the stock controls.
            Image img = go.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0f, 0f, 0f, 0.30f);

            Image[] frame = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), Grey());

            TMP_Text label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                                   Grey(), 12f, FontStyles.Normal,
                                   TextAlignmentOptions.Center);

            WmcButton behaviour = go.AddComponent<WmcButton>();
            behaviour.Initialise(frame, label, onClick);
            return behaviour;
        }

        private static WmcButton HitButton(RectTransform parent, Rect rect, Action onClick)
        {
            var go = new GameObject("HitTarget", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, rect);

            Image hit = go.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            WmcButton behaviour = go.AddComponent<WmcButton>();
            behaviour.Initialise(null, null, onClick);
            return behaviour;
        }

        /// <summary>Anchor a rect to the parent's top-left and place it in pixels.</summary>
        private static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

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

        /// <summary>The stock "on" colour: what HUD OPTIONS uses for an active control.</summary>
        internal static Color Green() => UiTheme.Green;

        /// <summary>The stock "off" colour.</summary>
        internal static Color Grey() => Color.grey;

        private static Color Accent() => Green();

        private static Color Warning() => new Color(1f, 0.55f, 0.2f);

        private static Color Friendly() => UiTheme.Friendly;

        private static Color WingColor() => WingMarkers.MemberColor;

        private static Color Dim() => Grey();

        private static Color RowColor() => Grey();
        private static Color MemberFrameColor() => WingColor().WithAlpha(0.58f);
        private static Color FrameColor() => new Color(Grey().r, Grey().g, Grey().b, 0.75f);
        private static Color PanelBackground() => new Color(0.075f, 0.12f, 0.16f, 0.84f);
        private static Color PanelEdge() => new Color(0.33f, 0.33f, 0.33f, 1f);
        private static Color PanelShadow() => new Color(0.24f, 0.24f, 0.24f, 1f);

        /// <summary>
        /// Reproduce the stock mission/menu card: a translucent slate fill, a soft grey
        /// two-pixel edge, and small rounded corners. A sliced sprite keeps the treatment
        /// consistent when the WMC content grows on another page or resolution.
        /// </summary>
        private static Sprite VanillaPanelSprite()
        {
            if (panelSprite != null) return panelSprite;

            const int size = 32;
            const float radius = 5f;
            const float edge = 3f;
            const float shadow = 1f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_VanillaPanel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = size * 0.5f;
            float half = size * 0.5f;
            Color fill = PanelBackground();
            Color frame = PanelEdge();
            Color shadowColor = PanelShadow();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, half + shadow, radius + shadow);
                    float coverage = Mathf.Clamp01(0.5f - outer);
                    if (coverage <= 0f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float actual = RoundedDistance(x + 0.5f, y + 0.5f,
                                                   centre, half, radius);
                    float innerHalf = half - edge;
                    float inner = RoundedDistance(x + 0.5f, y + 0.5f,
                                                  centre, innerHalf, Mathf.Max(1f, radius - edge));
                    Color pixel = actual > 0.5f
                        ? shadowColor
                        : inner <= -0.5f ? fill : frame;
                    pixel.a *= coverage;
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            panelSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));
            panelSprite.name = "WingCommand_VanillaPanelSprite";
            panelSprite.hideFlags = HideFlags.HideAndDontSave;
            return panelSprite;
        }

        private static float RoundedDistance(float x, float y, float centre,
                                             float half, float radius)
        {
            float qx = Mathf.Abs(x - centre) - (half - radius);
            float qy = Mathf.Abs(y - centre) - (half - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

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
            if (m.IsPanicking) return "DEFENSIVE";

            Unit assigned = m.AssignedTarget;
            if (assigned != null && !assigned.disabled)
                return UiTheme.Truncate(assigned.definition != null ? assigned.definition.code : assigned.unitName, 8);

            return WingOrderCatalog.ShortLabel(m.Order);
        }
    }

    /// <summary>Minimal clickable button with a hover tint, so no stock UI script is reused.</summary>
    internal class WmcButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image[] frame;
        private TMP_Text label;
        private Action onClick;
        private bool latched;
        private bool interactable = true;

        public void Initialise(Image[] frame, TMP_Text label, Action onClick)
        {
            this.frame = frame;
            this.label = label;
            this.onClick = onClick;
            Tint(WmcScreen.Green());
        }

        /// <summary>Hold this button lit, for a selected option in a group.</summary>
        public void SetLatched(bool on)
        {
            if (latched == on) return;
            latched = on;
            Tint(on ? Color.white : WmcScreen.Green());
        }

        public void SetEnabled(bool on)
        {
            if (interactable == on) return;
            interactable = on;
            Tint(on ? (latched ? Color.white : WmcScreen.Green())
                    : new Color(0.3f, 0.3f, 0.3f));
        }

        private void Tint(Color color)
        {
            if (frame != null)
            {
                foreach (Image edge in frame)
                {
                    if (edge != null) edge.color = color;
                }
            }
            if (label != null) label.color = color;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!interactable) return;

            try { onClick?.Invoke(); }
            catch (Exception e) { Plugin.Logger.LogError("WMC button failed: " + e); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (interactable) Tint(Color.white);
        }

        public void OnPointerExit(PointerEventData eventData) =>
            Tint(interactable ? (latched ? Color.white : WmcScreen.Green())
                              : new Color(0.3f, 0.3f, 0.3f));
    }
}
