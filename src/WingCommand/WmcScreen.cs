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
        private static WingButton tacticalTab;
        private static WingButton supplyTab;
        private static RectTransform panelRect;
        private static float tacticalHeight;
        private static float supplyHeight;
        private static RectTransform rosterArea;
        private static WingButton rosterPrevButton;
        private static WingButton rosterNextButton;
        private static TMP_Text shapeLabel;
        private static TMP_Text summaryLabel;
        private static TMP_Text rosterPageLabel;
        private static TMP_Text commandStatusLabel;
        private static TMP_Text postureLabel;
        private static WingButton holdButton;
        private static WingButton escortButton;
        private static WingButton freeButton;
        private static WingButton cargoButton;
        private static WingButton landButton;

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
            tacticalRoot = null;
            supplyRoot = null;
            page = Page.Tactical;
            tacticalTab = null;
            supplyTab = null;
            panelRect = null;
            tacticalHeight = 0f;
            supplyHeight = 0f;
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
            exceedLimitButton = null;
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

            // Supply reads top to bottom in the order the questions are actually asked: what
            // can I afford and is there room, then buying one, then conscripting one that is
            // already flying, then the holdback knob that only matters once you care about
            // what the AI is doing with the rest of the stock.
            float supplyY = y;
            supplyY = AddSupplyStatus(supplyRoot, supplyY);
            supplyY = AddShop(supplyRoot, supplyY);
            supplyY = AddAssignment(supplyRoot, supplyY);
            supplyY = AddReserve(supplyRoot, supplyY);
            supplyY = AddDebug(supplyRoot, supplyY);

            // Each page is sized to its own content rather than both to the taller of the
            // two. Supply is much shorter than Tactical, so sharing one height left a third
            // of the panel as empty framed space below the last control.
            tacticalHeight = Mathf.Abs(tacticalY) + Pad;
            supplyHeight = Mathf.Abs(supplyY) + Pad;

            panelRect = rt;
            rt.sizeDelta = new Vector2(PanelWidth, tacticalHeight);

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

            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(
                    PanelWidth, next == Page.Tactical ? tacticalHeight : supplyHeight);
            }
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
        private static float Heading(RectTransform parent, float y, string text) =>
            WingUi.Heading(parent, y, text, PanelWidth);

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

            if (commandStatusLabel != null)
                commandStatusLabel.text = manager?.MapStatus ?? "Select wingmen, then issue an order.";

            RefreshSupplyStatus();
            RefreshShop();
            // Refresh after the catalogue, because selecting or exhausting a shop row can
            // change which reserve action is valid for the current airframe.
            RefreshReserve();

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

            RefreshOfferDetail(offers);
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
