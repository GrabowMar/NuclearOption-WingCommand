using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>The WMC panel's SUPPLY tab: squadron funds, the aircraft shop, and the wing reserve.</summary>
    internal static partial class WmcScreen
    {
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
            float w = PanelWidth - Pad * 2f;
            const float reserveBlockW = 126f;
            float textW = w - reserveBlockW - Gap;

            supplyFundsLabel = Label(parent, "", new Rect(Pad, y, textW, LineHeight),
                                     Friendly(), FontSmall, FontStyles.Normal,
                                     TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            supplySquadronLabel = Label(parent, "", new Rect(Pad, y, textW, LineHeight),
                                        Friendly(), FontSmall, FontStyles.Normal,
                                        TextAlignmentOptions.Left);

            // Compact Wing Reserve control on the right of the top status block
            float ctrlX = PanelWidth - Pad - reserveBlockW;
            float ctrlY = y + LineHeight + 2f;
            Panel(parent, new Rect(ctrlX, ctrlY, reserveBlockW, LineHeight * 2f + 2f), WingUi.CardFill);
            Outline(parent, new Rect(ctrlX, ctrlY, reserveBlockW, LineHeight * 2f + 2f), FrameColor());

            Label(parent, "HOLD", new Rect(ctrlX + Space2, ctrlY, 36f, LineHeight * 2f + 2f),
                  Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            reserveLabel = Label(parent, "0/3",
                                 new Rect(ctrlX + 38f, ctrlY, 34f, LineHeight * 2f + 2f),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);

            const float btnW = 20f;
            const float btnH = 20f;
            float btnY = ctrlY - (LineHeight * 2f + 2f - btnH) * 0.5f;

            reserveReleaseButton = WingUi.Button(parent, "-",
                new Rect(ctrlX + reserveBlockW - (btnW * 2f + Space1 * 2f), btnY, btnW, btnH),
                FontBody, UiButtonStyle.Danger, ReleaseSelectedReserve)
                .WithTooltip("Release selected airframe back to faction stock (-)");

            reserveHoldButton = WingUi.Button(parent, "+",
                new Rect(ctrlX + reserveBlockW - (btnW + Space1), btnY, btnW, btnH),
                FontBody, UiButtonStyle.Primary, HoldSelectedReserve)
                .WithTooltip("Hold selected airframe in wing reserve (+)");

            reserveHintLabel = null;

            return y - LineHeight - Space2;
        }

        /// <summary>
        /// Which pilot the next requisition or assignment is for.
        ///
        /// Sat at the top of the page because it answers the first question a shop asks —
        /// who is this for — rather than hiding it below the list of things to buy. Defaults
        /// to the best available pilot, persists until the player cycles it, and skips a lost
        /// pilot automatically. Choosing a pilot here is the same choice the Wing tab shows
        /// as its listing; the two are one squadron.
        /// </summary>
        private static float AddPilotSelection(RectTransform parent, float y)
        {
            y = Heading(parent, y, "NEXT PILOT");
            Hint(parent, y, "Who flies the next requisitioned or assigned airframe.");
            y -= LineHeight + Space1;

            const float portrait = 56f;
            const float w = PanelWidth - Pad * 2f;
            const float stepperW = 76f;

            var (_, sRail) = WingUi.TacticalCard(parent, new Rect(Pad, y, portrait, portrait), RankColor(WingRank.Rookie));
            supplyPilotRail = sRail;
            supplyPilotPortrait = AddSprite(parent, "SupplyPilotPortrait", PilotPortrait.Sprite,
                                            new Rect(Pad + 3f, y - 3f, portrait - 6f, portrait - 6f),
                                            Color.white);

            float dossierX = Pad + portrait + Space3;
            float dossierW = w - portrait - Space3 - stepperW - Gap;
            supplyPilotNameLabel = Label(parent, "", new Rect(dossierX, y, dossierW, Space5), Green(),
                                         FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            float detailY = y - Space5;
            supplyPilotRankLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight),
                                         Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            detailY -= LineHeight;
            supplyPilotStatusLabel = Label(parent, "", new Rect(dossierX, detailY, dossierW, LineHeight),
                                           Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            float stepperX = PanelWidth - Pad - stepperW;
            Panel(parent, new Rect(stepperX, y, stepperW, RowHeight), RowColor());
            Outline(parent, new Rect(stepperX, y, stepperW, RowHeight), FrameColor());

            const float arrow = 24f;
            supplyPilotPrev = WingUi.Button(parent, "<",
                                            new Rect(stepperX + 1f, y - 1f, arrow, RowHeight - 2f),
                                            FontBody, UiButtonStyle.Quiet, () => CycleSupplyPilot(-1))
                .WithTooltip("Previous available pilot");
            supplyPilotCountLabel = Label(parent, "1 / 8",
                                          new Rect(stepperX + arrow, y, stepperW - arrow * 2f, RowHeight),
                                          Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            supplyPilotNext = WingUi.Button(parent, ">",
                                            new Rect(stepperX + stepperW - arrow - 1f, y - 1f,
                                                     arrow, RowHeight - 2f),
                                            FontBody, UiButtonStyle.Quiet, () => CycleSupplyPilot(1))
                .WithTooltip("Next available pilot");

            return y - portrait - Space2;
        }

        /// <summary>Step the pilot selection through the available pilots, wrapping around.</summary>
        private static void CycleSupplyPilot(int direction)
        {
            List<WingPilot> selectable = WingPilotRoster.SelectablePilots();
            if (selectable.Count == 0) return;

            int index = PilotSelectionPolicy.CycleIndex(
                selectable.IndexOf(WingPilotRoster.Selected), selectable.Count, direction);
            if (index >= 0 && index < selectable.Count)
            {
                WingPilotRoster.Select(selectable[index]);
            }
            RefreshSupplyPilot();
        }

        /// <summary>Repaint the pilot picker on the SUPPLY tab.</summary>
        private static void RefreshSupplyPilot()
        {
            if (supplyPilotPortrait == null) return;

            List<WingPilot> selectable = WingPilotRoster.SelectablePilots();
            WingPilot sel = WingPilotRoster.Selected;
            if (sel == null && selectable.Count > 0)
            {
                WingPilotRoster.Select(selectable[0]);
                sel = selectable[0];
            }

            if (sel == null)
            {
                if (supplyPilotNameLabel != null)
                {
                    supplyPilotNameLabel.text = "NO AVAILABLE PILOTS";
                    supplyPilotNameLabel.color = Warning();
                }
                if (supplyPilotRankLabel != null) supplyPilotRankLabel.text = "EVERY PILOT IS LOST";
                if (supplyPilotStatusLabel != null) { supplyPilotStatusLabel.text = ""; }
                if (supplyPilotCountLabel != null) { supplyPilotCountLabel.text = "0 / 0"; }
                supplyPilotPortrait.color = Color.white;
                if (supplyPilotRail != null) supplyPilotRail.color = Dim();
                supplyPilotPrev?.SetEnabled(false);
                supplyPilotNext?.SetEnabled(false);
                return;
            }

            if (supplyPilotNameLabel != null)
            {
                supplyPilotNameLabel.text = sel.Callsign + "  ·  " + sel.Name;
                supplyPilotNameLabel.color = Green();
            }
            if (supplyPilotRankLabel != null)
            {
                supplyPilotRankLabel.text = WingPilotRoster.RankName(sel.Rank) + "   XP " + sel.Xp;
                supplyPilotRankLabel.color = RankColor(sel.Rank);
            }
            if (supplyPilotStatusLabel != null)
            {
                if (WingPilotRoster.IsFlying(sel))
                {
                    supplyPilotStatusLabel.text = "IN THE AIR";
                    supplyPilotStatusLabel.color = Friendly();
                }
                else if (WingPilotRoster.IsReserved(sel))
                {
                    supplyPilotStatusLabel.text = "AWAITING AIRFRAME";
                    supplyPilotStatusLabel.color = Friendly();
                }
                else
                {
                    supplyPilotStatusLabel.text = "READY FOR COMBAT";
                    supplyPilotStatusLabel.color = Green();
                }
            }
            if (supplyPilotCountLabel != null)
            {
                int index = selectable.IndexOf(sel);
                supplyPilotCountLabel.text = (index >= 0 ? index + 1 : 1) + " / " + selectable.Count;
            }

            supplyPilotPortrait.color = Color.white;
            if (supplyPilotRail != null) supplyPilotRail.color = RankColor(sel.Rank);
            supplyPilotPrev?.SetEnabled(selectable.Count > 1);
            supplyPilotNext?.SetEnabled(selectable.Count > 1);
        }

        private static float AddAssignment(RectTransform parent, float y)
        {
            y = Heading(parent, y, "ACTIVE AIRCRAFT ASSIGNMENT");

            WingUi.Button(parent, "ASSIGN SELECTED",
                          new Rect(Pad, y, PanelWidth - Pad * 2f, RowHeight),
                          FontSmall, UiButtonStyle.Quiet,
                          () =>
                          {
                              WingCommandManager.Instance?.AddSelectedFromMap();
                              RefreshSupplyPilot();
                          })
                .WithTooltip(OrderHint.AssignSelected);
            return y - (RowHeight + Gap);
        }

        private const int LaunchRowsPerPage = 5;
        private const float LaunchRowHeight = 22f;
        private const float LaunchCheckWidth = 20f;

        /// <summary>
        /// Which fields a requisition may launch from, and whether it waits at the nearest
        /// or takes any free pad.
        /// </summary>
        private static float AddLaunchFrom(RectTransform parent, float y)
        {
            y = Heading(parent, y, "LAUNCH FROM");

            const float arrowW = 28f;
            float pagerX = PanelWidth - Pad - arrowW * 2f - 36f;
            launchPrevButton = WingUi.Button(parent, "<", new Rect(pagerX, y, arrowW, RowHeight),
                                             FontMicro, UiButtonStyle.Quiet, () => TurnLaunchPage(-1));
            launchPageLabel = Label(parent, "", new Rect(pagerX + arrowW, y, 36f, RowHeight),
                                    Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            launchNextButton = WingUi.Button(parent, ">", new Rect(pagerX + arrowW + 36f, y, arrowW, RowHeight),
                                             FontMicro, UiButtonStyle.Quiet, () => TurnLaunchPage(1));
            launchPrevButton.gameObject.SetActive(false);
            launchPageLabel.gameObject.SetActive(false);
            launchNextButton.gameObject.SetActive(false);

            float modeW = (pagerX - Pad - Gap * 2f) * 0.5f;
            launchNearestButton = WingUi.Button(
                parent, "ONLY NEAREST", new Rect(Pad, y, modeW, RowHeight),
                FontMicro, UiButtonStyle.Quiet,
                SelectNearestLaunchField)
                .WithTooltip("Select only the nearest available field for this airframe.");
            launchAnyButton = WingUi.Button(
                parent, "ANY", new Rect(Pad + modeW + Gap, y, modeW, RowHeight),
                FontMicro, UiButtonStyle.Quiet,
                () => {
                    WingLaunchFields.Mode = HangarLaunchMode.Any;
                    foreach (Airbase field in WingLaunchFields.Listing) WingLaunchFields.SetAllowed(field, true);
                    RefreshLaunchFrom();
                    RefreshShop();
                })
                .WithTooltip("Launch from the closest checked field with a free hangar. No per-field queue.");
            y -= RowHeight + Space1;

            launchRows.Clear();
            float w = PanelWidth - Pad * 2f;
            for (int i = 0; i < LaunchRowsPerPage; i++)
            {
                launchRows.Add(new LaunchBaseRow(
                    parent, new Rect(Pad, y, w, LaunchRowHeight), i));
                y -= LaunchRowHeight;
            }

            return y - Space1;
        }

        private static void SelectNearestLaunchField()
        {
            RefreshLaunchFrom();
            WingLaunchFields.Mode = HangarLaunchMode.OnlyNearest;
            Airbase nearest = null;
            foreach (Airbase field in WingLaunchFields.Listing)
                if (nearest == null && (selectedOffer == null || WingLaunchFields.CanProduce(field, selectedOffer)))
                    nearest = field;
            foreach (Airbase field in WingLaunchFields.Listing)
                WingLaunchFields.SetAllowed(field, field == nearest);
            launchPage = nearest == null ? 0 : IndexOfLaunchField(nearest) / LaunchRowsPerPage;
            RefreshLaunchFrom();
            RefreshShop();
        }

        private static int IndexOfLaunchField(Airbase field)
        {
            for (int i = 0; i < WingLaunchFields.Listing.Count; i++)
                if (WingLaunchFields.Listing[i] == field) return i;
            return 0;
        }

        private static void TurnLaunchPage(int direction)
        {
            int count = WingLaunchFields.Listing.Count;
            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)LaunchRowsPerPage));
            launchPage = Mathf.Clamp(launchPage + direction, 0, pages - 1);
            RefreshLaunchFrom();
        }

        private static void RefreshLaunchFrom()
        {
            if (launchRows.Count == 0) return;

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            FactionHQ hq = leader != null ? leader.NetworkHQ : null;
            Vector3 from = leader != null ? leader.transform.position : Vector3.zero;
            WingLaunchFields.RefreshListing(hq, from);

            IReadOnlyList<Airbase> fields = WingLaunchFields.Listing;
            int pages = Mathf.Max(1, Mathf.CeilToInt(fields.Count / (float)LaunchRowsPerPage));
            if (launchPage >= pages) launchPage = pages - 1;
            if (launchPage < 0) launchPage = 0;

            bool nearest = WingLaunchFields.Mode == HangarLaunchMode.OnlyNearest;
            launchNearestButton?.SetLatched(nearest);
            launchAnyButton?.SetLatched(!nearest);

            if (launchPageLabel != null)
            {
                bool multiPage = pages > 1;
                launchPrevButton?.gameObject.SetActive(multiPage);
                launchNextButton?.gameObject.SetActive(multiPage);
                launchPageLabel.gameObject.SetActive(multiPage);
                if (multiPage)
                {
                    launchPageLabel.text = (launchPage + 1) + "/" + pages;
                    launchPrevButton?.SetEnabled(launchPage > 0);
                    launchNextButton?.SetEnabled(launchPage < pages - 1);
                }
            }

            int first = launchPage * LaunchRowsPerPage;
            for (int i = 0; i < launchRows.Count; i++)
            {
                int index = first + i;
                if (index < fields.Count) launchRows[i].Bind(fields[index]);
                else launchRows[i].Hide();
            }
        }

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

        private static readonly Confirmation reserveRelease = new Confirmation();


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
            reserveReleaseButton?.SetText(armed ? "?" : "-");
            reserveReleaseButton?.SetEnabled(
                host && selected && WingSupplyReserve.CountOf(selectedOffer) > 0);
            reserveHoldButton?.SetEnabled(
                host && selected && WingSupplyReserve.Count < WingSupplyReserve.Capacity &&
                WingSupplyReserve.FactionStockOf(selectedOffer) > 0);
        }


        private const int ShopGridRows = 3;
        private const int ShopGridCols = 4;
        private const int ShopGridCapacity = ShopGridRows * ShopGridCols; // 12
        private const float ShopTileHeight = 36f;
        private const float ShopTileGap = 4f;

        /// <summary>
        /// The shop: an airframe icon grid matching the LOADOUT screen, then controls for
        /// templates, fuel, and requisition.
        /// </summary>
        private static float AddShop(RectTransform parent, float y)
        {
            if (!Plugin.Settings.ShopEnabled.Value) return y;

            shopTemplatePopup = new AvKit.Popup(parent, PanelWidth);

            y = Heading(parent, y, "AIRFRAME REQUISITION");

            const float arrowW = 28f;
            float pagerX = PanelWidth - Pad - arrowW * 2f - 36f;
            shopPrevButton = WingUi.Button(parent, "<", new Rect(pagerX, y, arrowW, RowHeight),
                                           FontMicro, UiButtonStyle.Quiet, () => TurnPage(-1));
            shopPageLabel = Label(parent, "", new Rect(pagerX + arrowW, y, 36f, RowHeight),
                                  Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            shopNextButton = WingUi.Button(parent, ">", new Rect(pagerX + arrowW + 36f, y, arrowW, RowHeight),
                                           FontMicro, UiButtonStyle.Quiet, () => TurnPage(1));
            shopPrevButton.gameObject.SetActive(false);
            shopPageLabel.gameObject.SetActive(false);
            shopNextButton.gameObject.SetActive(false);

            y = AddShopGrid(parent, y);
            y -= Gap;

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
            const float fuelWidth = 96f;
            float fitButtonWidth = PanelWidth - Pad * 2f - fitGutter - Gap - fuelWidth;
            shopTemplateButton = WingUi.Button(
                parent, "",
                new Rect(Pad + fitGutter, y, fitButtonWidth, RowHeight),
                FontSmall, UiButtonStyle.Default, OpenShopTemplatePicker)
                .WithTooltip(OrderHint.Fit);
            Label(parent, "FIT", new Rect(Pad, y, fitGutter - Gap, RowHeight), Dim(), FontMicro,
                  FontStyles.Normal, TextAlignmentOptions.Left);

            // Where the list drops from: directly under the button that opens it.
            shopTemplateRowY = y - RowHeight;
            shopTemplateRowX = Pad + fitGutter;
            shopTemplateRowWidth = fitButtonWidth;

            // The fuel switch is a small share of the row rather than one of its own — it is
            // a modifier on the fit, not a full step of the purchase, so it rides with the
            // template picker it changes the launch of.
            fullFuelButton = WingUi.Button(
                parent, "", new Rect(Pad + fitGutter + fitButtonWidth + Gap, y, fuelWidth, RowHeight),
                FontSmall, UiButtonStyle.Quiet, ToggleFullFuel)
                .WithTooltip(OrderHint.FullFuel);
            y -= RowHeight + Space1;

            offerLoadoutLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + Space1;

            y = AddLaunchFrom(parent, y);

            // REQUISITION is the reason this page exists and is drawn as such; the
            // over-limit permission beside it is a modifier on that purchase and reads a
            // rank quieter until it is switched on, at which point it latches lit.
            const float buyWidth = WingUi.ButtonPrimary;
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

        private static float AddShopGrid(RectTransform parent, float y)
        {
            shopTiles.Clear();
            float w = PanelWidth - Pad * 2f;
            float colWidth = (w - (ShopGridCols - 1) * ShopTileGap) / ShopGridCols;

            for (int r = 0; r < ShopGridRows; r++)
            {
                float rowY = y - r * (ShopTileHeight + ShopTileGap);
                for (int c = 0; c < ShopGridCols; c++)
                {
                    float tileX = Pad + c * (colWidth + ShopTileGap);
                    int index = r * ShopGridCols + c;
                    shopTiles.Add(new ShopAirframeTile(parent, new Rect(tileX, rowY, colWidth, ShopTileHeight), index));
                }
            }

            return y - (ShopGridRows * ShopTileHeight + (ShopGridRows - 1) * ShopTileGap);
        }

        private static void TurnPage(int direction)
        {
            IReadOnlyList<WingShop.Offer> offers = WingShop.Catalogue();
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopGridCapacity));
            shopPage = Mathf.Clamp(shopPage + direction, 0, pages - 1);
            int first = shopPage * ShopGridCapacity;
            if (first < offers.Count)
            {
                selectedOffer = offers[first].Definition;
            }
            RefreshShop();
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

        /// <summary>
        /// Choose whether the next requisition launches with full tanks or half.
        ///
        /// It applies to the launch, not to the price, so it can be flipped between
        /// purchases and affects only the ones made after it.
        /// </summary>
        private static void ToggleFullFuel()
        {
            WingShop.FullFuel = !WingShop.FullFuel;
            WingCommandManager.Instance?.Toast(WingShop.FullFuel
                ? "Requisitions launch with full fuel"
                : "Requisitions launch with " +
                  Mathf.RoundToInt(WingTuning.PartialFuelLevel * 100f) + "% fuel");
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
                ? selectedOffer.unitName + " requisitioned for " + Grouped(paid) +
                  " - departing friendly base"
                : why);
            if (bought)
            {
                RefreshSupplyPilot();
            }
        }

        /// <summary>Rebind the shop rows and the allocation header.</summary>
        private static void RefreshShop()
        {
            if (!Plugin.Settings.ShopEnabled.Value || shopTiles.Count == 0) return;

            IReadOnlyList<WingShop.Offer> offers = WingShop.Catalogue();

            // Clamp here rather than in TurnPage: stock runs out and the catalogue shrinks
            // under the player, so the page has to be re-validated against what is actually
            // on offer each time rather than only when a button is pressed.
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopGridCapacity));
            if (shopPage >= pages) shopPage = pages - 1;
            if (shopPage < 0) shopPage = 0;

            if (shopPageLabel != null)
            {
                bool multiPage = pages > 1;
                shopPrevButton?.gameObject.SetActive(multiPage);
                shopNextButton?.gameObject.SetActive(multiPage);
                shopPageLabel.gameObject.SetActive(multiPage);

                if (multiPage)
                {
                    shopPageLabel.text = $"{shopPage + 1}/{pages}";
                    shopPrevButton?.SetEnabled(shopPage > 0);
                    shopNextButton?.SetEnabled(shopPage < pages - 1);
                }
            }

            int first = shopPage * ShopGridCapacity;

            for (int i = 0; i < shopTiles.Count; i++)
            {
                int index = first + i;
                if (index < offers.Count) shopTiles[i].Bind(offers[index]);
                else shopTiles[i].Hide();
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

            if (fullFuelButton != null)
            {
                // The label is the whole state: full, or the partial percentage. It never
                // latches — the value is in the words, and a permanently-lit toggle on the
                // default choice would read louder than the setting deserves.
                fullFuelButton.SetText(WingShop.FullFuel
                    ? "FUEL  FULL"
                    : "FUEL  " + Mathf.RoundToInt(WingTuning.PartialFuelLevel * 100f) + "%");
                fullFuelButton.SetLatched(false);
                fullFuelButton.SetEnabled(true);
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

                    string costPart = ownedCount > 0 ? "FREE (OWNED)" : (Grouped(cost) + " funds" + (overLimit ? " (over limit)" : ""));
                    offerDetailLabel.text = quote.CanBuy
                        ? AvTheme.Truncate(selectedOffer.unitName, 18) +
                          "  ·  " + costPart +
                          "  ·  " + stock + " available" +
                          (ownedCount > 0 ? "  ·  READY TO RE-LAUNCH (" + ownedCount + " in wing reserve)" :
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
            requisitionButton?.WithTooltip(quote.CanBuy
                ? OrderHint.Requisition
                : "Cannot requisition — " + quote.Reason);
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
                AvTheme.Truncate(WingLoadoutCatalog.Label(planned), 34)
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
            popupEntries.Add(new AvKit.PopupEntry(
                "STANDARD FIT", "as issued", !planned.IsTemplate));

            for (int i = 0; i < mine.Count; i++)
            {
                ids.Add(mine[i].Id);
                popupEntries.Add(new AvKit.PopupEntry(
                    AvTheme.Truncate(mine[i].Name, 24),
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
            supplyFundsLabel.text = "FUNDS " + Grouped(WingShop.Allocation) +
                                    "   ·   WING " + wing + " / " + WingRegistry.WingLimitLabel +
                                    "   (YOUR FLIGHT)";

            WingShop.SquadronState squadron = WingShop.Squadron();
            string text = "SQUADRON " + squadron.Active + " / " + squadron.Limit +
                          "   (AI POOL)";
            if (Plugin.Settings.CheatNoWingLimit)
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
                // The multiplier is on the OVER LIMIT button itself; the status line only
                // needs to say the cap is being flown past.
                supplySquadronLabel.text = text + "  ·  OVER LIMIT";
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
        /// <summary>One purchasable airframe tile in the shop grid: silhouette, code, stock, cost.</summary>
        private sealed class ShopAirframeTile
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly Image[] outline;
            private readonly Image rail;
            private readonly Image icon;
            private readonly TMP_Text code;
            private readonly TMP_Text priceStock;
            private readonly WingButton hit;
            private AircraftDefinition bound;

            public ShopAirframeTile(RectTransform parent, Rect rect, int index)
            {
                go = new GameObject("ShopTile_" + index, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, rect);

                fill = go.GetComponent<Image>();
                fill.color = WingUi.CardFill;
                fill.raycastTarget = false;

                outline = Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor());
                rail = Rule(rt, new Rect(0f, 0f, 3f, rect.height), Color.clear);

                icon = AddSprite(rt, "ShopAirframeIcon", IconFactory.Get("airframe"),
                                 new Rect(4f, -4f, 28f, 28f), Color.white);

                float textLeft = 34f;
                float textWidth = rect.width - textLeft - 2f;
                code = Label(rt, "", new Rect(textLeft, -2f, textWidth, 16f), Friendly(),
                             FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                code.overflowMode = TextOverflowModes.Ellipsis;

                priceStock = Label(rt, "", new Rect(textLeft, -18f, textWidth, 14f), Green(),
                                   FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                priceStock.overflowMode = TextOverflowModes.Ellipsis;

                hit = HitButton(rt, new Rect(0f, 0f, rect.width, rect.height), () =>
                {
                    if (bound != null)
                    {
                        selectedOffer = bound;
                        RefreshShop();
                        RefreshLaunchFrom();
                    }
                });

                go.SetActive(false);
            }

            public void Bind(WingShop.Offer offer)
            {
                bound = offer.Definition;
                if (!go.activeSelf) go.SetActive(true);

                Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
                FactionHQ hq = leader != null ? leader.NetworkHQ : null;

                int owned = WingSupplyReserve.OwnedOf(offer.Definition);
                float cost = WingShop.CurrentPriceOf(offer.Definition);
                bool affordable = WingShop.Allocation >= cost;
                bool selected = selectedOffer == offer.Definition;
                bool canSpawn = WingLaunchFields.CanAnyAllowedLaunch(hq, offer.Definition);

                Sprite sprite = offer.Definition.mapIcon != null ? offer.Definition.mapIcon
                              : offer.Definition.friendlyIcon != null ? offer.Definition.friendlyIcon
                              : IconFactory.Get("airframe");
                icon.sprite = sprite;
                icon.color = selected ? Color.white : (canSpawn ? (affordable ? Color.white : Dim()) : Dim());

                string codeStr = !string.IsNullOrEmpty(offer.Definition.code) ? offer.Definition.code : offer.Name;
                code.text = AvTheme.Truncate(codeStr, 7);
                code.color = selected ? Green() : (canSpawn ? (affordable ? Friendly() : Dim()) : Dim());

                if (owned > 0)
                {
                    priceStock.text = "FREE · " + offer.Stock + "x (" + owned + " OWNED)";
                    priceStock.color = !canSpawn ? Warning() : Green();
                }
                else
                {
                    priceStock.text = Grouped(cost) + " · " + offer.Stock + "x";
                    priceStock.color = !canSpawn ? Warning() : (affordable ? Green() : Warning());
                }

                fill.color = selected ? WingUi.CardFillSelected : WingUi.CardFill;
                Color frameColor = selected ? Green() : FrameColor();
                if (outline != null)
                {
                    for (int i = 0; i < outline.Length; i++)
                    {
                        if (outline[i] != null) outline[i].color = frameColor;
                    }
                }
                rail.color = selected ? Green() : Color.clear;

                string spawnNotice = !canSpawn ? " | [!] No selected launch base can spawn this aircraft" : "";
                string costNotice = owned > 0 ? "FREE (" + owned + " owned in reserve)" : "Cost: " + Grouped(cost);
                hit.WithTooltip(offer.Name + " — " + costNotice + " | Stock: " + offer.Stock + spawnNotice);
                hit.SetRowHighlight(fill, selected ? WingUi.CardFillSelected : WingUi.CardFill, WingUi.CardFillHover);
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

        /// <summary>One friendly field the player can allow or refuse as a launch origin.</summary>
        private sealed class LaunchBaseRow
        {
            private readonly GameObject go;
            private readonly WingButton check;
            private readonly TMP_Text name;
            private readonly TMP_Text status;
            private readonly WingButton hit;
            private Airbase bound;

            public LaunchBaseRow(RectTransform parent, Rect rect, int index)
            {
                go = new GameObject("LaunchBase_" + index, typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, worldPositionStays: false);
                Place(rt, rect);

                check = WingUi.Button(rt, "",
                    new Rect(0f, -(rect.height - LaunchCheckWidth) * 0.5f,
                             LaunchCheckWidth, LaunchCheckWidth),
                    FontMicro, UiButtonStyle.Quiet, Toggle)
                    .WithTooltip("Allow launches from this field");

                const float statusWidth = 60f;
                float nameWidth = rect.width - LaunchCheckWidth - Space1 - statusWidth - Space1;

                name = Label(rt, "",
                    new Rect(LaunchCheckWidth + Space1, 0f, nameWidth, rect.height),
                    Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                name.overflowMode = TextOverflowModes.Ellipsis;

                status = Label(rt, "",
                    new Rect(rect.width - statusWidth, 0f, statusWidth, rect.height),
                    Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

                hit = HitButton(rt, new Rect(LaunchCheckWidth + Space1, 0f,
                                             rect.width - LaunchCheckWidth - Space1, rect.height),
                                Toggle);

                go.SetActive(false);
            }

            public void Bind(Airbase airbase)
            {
                bound = airbase;
                if (!go.activeSelf) go.SetActive(true);

                bool allowed = WingLaunchFields.IsAllowed(airbase);
                bool hasAirframe = selectedOffer != null;
                bool canProduce = hasAirframe && WingLaunchFields.CanProduce(airbase, selectedOffer);

                LaunchBaseStatus state = LaunchBaseStatusPolicy.Evaluate(allowed, canProduce, hasAirframe);
                string badge = LaunchBaseStatusPolicy.BadgeText(state);

                check.SetLatched(allowed);
                check.SetText(allowed ? "X" : "");

                name.text = AvTheme.Truncate(WingLaunchFields.DisplayName(airbase), 26);
                status.text = badge;

                switch (state)
                {
                    case LaunchBaseStatus.Ready:
                        status.color = Green();
                        name.color = Friendly();
                        break;
                    case LaunchBaseStatus.NoPad:
                        status.color = Warning();
                        name.color = allowed ? Warning() : Dim();
                        break;
                    case LaunchBaseStatus.Blocked:
                        status.color = Dim();
                        name.color = Dim();
                        break;
                    default:
                        status.color = Dim();
                        name.color = allowed ? Friendly() : Dim();
                        break;
                }

                string tooltip = LaunchBaseStatusPolicy.Tooltip(
                    WingLaunchFields.DisplayName(airbase),
                    selectedOffer != null ? selectedOffer.unitName : null,
                    allowed,
                    canProduce);
                hit.WithTooltip(tooltip);
                check.WithTooltip(tooltip);
            }

            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }

            private void Toggle()
            {
                if (bound == null) return;
                WingLaunchFields.SetAllowed(bound, !WingLaunchFields.IsAllowed(bound));
                RefreshLaunchFrom();
                RefreshShop();
            }
        }

    }
}
