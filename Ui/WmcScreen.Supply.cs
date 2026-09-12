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
    /// <summary>SUPPLY page for funds, purchases, launch choices, and reserve airframes.</summary>
    internal static partial class WmcScreen
    {
        private static TMP_Text assignmentCostLabel;
        /// <summary>Keep funds and capacity limits visible so disabled purchases have an
        /// explanation.</summary>
        private static float AddSupplyStatus(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            const float reserveBlockW = 154f;
            float textW = w - reserveBlockW - Gap;

            supplyFundsLabel = Label(parent, "", new Rect(Pad, y, textW, LineHeight),
                                     Friendly(), FontSmall, FontStyles.Normal,
                                     TextAlignmentOptions.Left);
            y -= LineHeight + 2f;
            supplySquadronLabel = Label(parent, "", new Rect(Pad, y, textW, LineHeight),
                                        Friendly(), FontSmall, FontStyles.Normal,
                                        TextAlignmentOptions.Left);

            // Place reserve controls beside the status readouts.
            float ctrlX = PanelWidth - Pad - reserveBlockW;
            float ctrlY = y + LineHeight + 2f;
            Panel(parent, new Rect(ctrlX, ctrlY, reserveBlockW, LineHeight * 2f + 2f), WingUi.CardFill);
            Outline(parent, new Rect(ctrlX, ctrlY, reserveBlockW, LineHeight * 2f + 2f), FrameColor());

            Label(parent, "HOLD", new Rect(ctrlX + Space2, ctrlY, 36f, LineHeight * 2f + 2f),
                  Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            reserveLabel = Label(parent, "0/3",
                                 new Rect(ctrlX + 38f, ctrlY, 34f, LineHeight * 2f + 2f),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);

            const float btnW = 30f;
            const float btnH = 30f;
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

        /// <summary>Show the shared pilot choice above purchases and assignments; roster selection
        /// excludes lost pilots and advances after assignment.</summary>
        private static float AddPilotSelection(RectTransform parent, float y)
        {
            const float stepperW = 112f;
            Label(parent, "NEXT PILOT", new Rect(Pad, y, PanelWidth - Pad * 2f - stepperW - Gap, RowHeight),
                  Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
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

            return y - RowHeight - Space2;
        }

        /// <summary>Cycle selectable pilots in either direction with wraparound.</summary>
        private static void CycleSupplyPilot(int direction)
        {
            List<WingPilot> selectable = PersonnelFacade.Roster.SelectablePilots();
            if (selectable.Count == 0) return;

            int index = PilotSelectionPolicy.CycleIndex(
                selectable.IndexOf(PersonnelFacade.Roster.Selected), selectable.Count, direction);
            if (index >= 0 && index < selectable.Count)
            {
                PersonnelFacade.Roster.Select(selectable[index]);
            }
            RefreshSupplyPilot();
        }

        /// <summary>Update the SUPPLY pilot chooser.</summary>
        private static void RefreshSupplyPilot()
        {
            if (supplyPilotPortrait == null) return;

            List<WingPilot> selectable = PersonnelFacade.Roster.SelectablePilots();
            WingPilot sel = PersonnelFacade.Roster.Selected;
            if (sel == null && selectable.Count > 0)
            {
                PersonnelFacade.Roster.Select(selectable[0]);
                sel = selectable[0];
            }

            supplyPilotPortrait.sprite = PersonnelFacade.Portraits.For(sel);

            if (sel == null)
            {
                if (supplyPilotNameLabel != null)
                {
                    supplyPilotNameLabel.text = "NO AVAILABLE PILOTS";
                    supplyPilotNameLabel.color = Warning();
                }
                if (supplyPilotRankLabel != null) supplyPilotRankLabel.text = "OPEN WING TO RECRUIT";
                if (supplyPilotStatusLabel != null) { supplyPilotStatusLabel.text = "A pilot is required to launch."; }
                if (supplyPilotCountLabel != null) { supplyPilotCountLabel.text = "0 / 0"; }
                supplyPilotPortrait.color = Color.clear;
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
                supplyPilotRankLabel.text = PersonnelFacade.Roster.RankName(sel.Rank) + "   XP " + sel.Xp;
                supplyPilotRankLabel.color = RankColor(sel.Rank);
            }
            if (supplyPilotStatusLabel != null)
            {
                if (PersonnelFacade.Roster.IsFlying(sel))
                {
                    supplyPilotStatusLabel.text = "IN THE AIR";
                    supplyPilotStatusLabel.color = Friendly();
                }
                else if (PersonnelFacade.Roster.IsReserved(sel))
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
                          new Rect(Pad, y, (PanelWidth - Pad * 2f) * 0.6f, RowHeight),
                          FontSmall, UiButtonStyle.Quiet,
                          () =>
                          {
                              WingCommandManager.Instance?.AddSelectedFromMap();
                              RefreshSupplyPilot();
                          })
                .WithTooltip(OrderHint.AssignSelected);
            float costX = Pad + (PanelWidth - Pad * 2f) * 0.6f + Gap;
            assignmentCostLabel = Label(parent, "— CR",
                new Rect(costX, y, PanelWidth - Pad - costX, RowHeight), Dim(),
                FontSmall, FontStyles.Normal, TextAlignmentOptions.Right);
            return y - (RowHeight + Gap);
        }

        private const int LaunchRowsPerPage = 3;
        private const float LaunchRowHeight = 28f;
        private const float LaunchCheckWidth = 20f;

        /// <summary>Build enabled launch-field and nearest/any routing controls.</summary>
        private static float AddLaunchFrom(RectTransform parent, float y)
        {
            y = Heading(parent, y, "LAUNCH FROM");

            launchPager = HeaderPager(parent, y - Space1, () => TurnLaunchPage(-1), () => TurnLaunchPage(1),
                                      out launchPrevButton, out launchPageLabel, out launchNextButton);

            float pagerX = PanelWidth - Pad - HeaderPagerWidth;
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
                    EconomyFacade.LaunchFields.Mode = HangarLaunchMode.Any;
                    foreach (Airbase field in EconomyFacade.LaunchFields.Listing) EconomyFacade.LaunchFields.SetAllowed(field, true);
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
            EconomyFacade.LaunchFields.Mode = HangarLaunchMode.OnlyNearest;
            Airbase nearest = null;
            foreach (Airbase field in EconomyFacade.LaunchFields.Listing)
                if (nearest == null && (selectedOffer == null || EconomyFacade.LaunchFields.CanProduce(field, selectedOffer)))
                    nearest = field;
            foreach (Airbase field in EconomyFacade.LaunchFields.Listing)
                EconomyFacade.LaunchFields.SetAllowed(field, field == nearest);
            launchPage = nearest == null ? 0 : IndexOfLaunchField(nearest) / LaunchRowsPerPage;
            RefreshLaunchFrom();
            RefreshShop();
        }

        private static int IndexOfLaunchField(Airbase field)
        {
            for (int i = 0; i < EconomyFacade.LaunchFields.Listing.Count; i++)
                if (EconomyFacade.LaunchFields.Listing[i] == field) return i;
            return 0;
        }

        private static void TurnLaunchPage(int direction)
        {
            int count = EconomyFacade.LaunchFields.Listing.Count;
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
            EconomyFacade.LaunchFields.RefreshListing(hq, from);

            IReadOnlyList<Airbase> fields = EconomyFacade.LaunchFields.Listing;
            int pages = Mathf.Max(1, Mathf.CeilToInt(fields.Count / (float)LaunchRowsPerPage));
            if (launchPage >= pages) launchPage = pages - 1;
            if (launchPage < 0) launchPage = 0;

            bool nearest = EconomyFacade.LaunchFields.Mode == HangarLaunchMode.OnlyNearest;
            launchNearestButton?.SetLatched(nearest);
            launchAnyButton?.SetLatched(!nearest);

            RefreshHeaderPager(launchPager, launchPrevButton, launchPageLabel, launchNextButton,
                               launchPage, pages);

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
            bool held = EconomyFacade.SupplyReserve.Hold(selectedOffer, out string reason);
            WingCommandManager.Instance?.Toast(held
                ? selectedOffer.unitName + " held for the wing (" + EconomyFacade.SupplyReserve.Count +
                  "/" + EconomyFacade.SupplyReserve.Capacity + ")"
                : reason);
        }

        /// <summary>Require a second matching press before releasing reserve stock; faction AI may consume
        /// it immediately afterward.</summary>
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
            bool released = EconomyFacade.SupplyReserve.Release(
                definition, out bool wasOwned, out string reason);
            WingCommandManager.Instance?.Toast(released
                ? definition.unitName + (wasOwned ? " ownership released" : " released") +
                  " to faction stock"
                : reason);
        }

        private static readonly Confirmation reserveRelease = new Confirmation();


        /// <summary>Refresh concrete reserve capacity and selected-airframe controls.</summary>
        private static void RefreshReserve()
        {
            if (reserveLabel == null) return;

            if (!EconomyFacade.SupplyReserve.HasFaction)
            {
                reserveLabel.text = "NO FACTION";
                reserveLabel.color = Dim();
                if (reserveHintLabel != null)
                    reserveHintLabel.text = "Join a faction to use the wing reserve.";
                reserveReleaseButton?.SetEnabled(false);
                reserveHoldButton?.SetEnabled(false);
                return;
            }

            reserveLabel.text = "" + EconomyFacade.SupplyReserve.Count + " / " +
                                EconomyFacade.SupplyReserve.Capacity;
            reserveLabel.color = Friendly();

            if (reserveHintLabel != null)
            {
                reserveHintLabel.text = EconomyFacade.SupplyReserve.Count >= EconomyFacade.SupplyReserve.Capacity
                    ? "FULL - RELEASE a selected row before holding another."
                    : EconomyFacade.SupplyReserve.Count > 0
                        ? "Select a row to RELEASE it, or HOLD another from faction stock."
                        : "Select an airframe, then HOLD it to protect it from AI.";
            }

            bool host = EconomyFacade.SupplyReserve.IsHost;
            bool selected = selectedOffer != null;

            // Bind confirmation to the originally selected airframe; changing selection disarms
            // release.
            bool armed = reserveRelease.IsArmedFor(selectedOffer);

            reserveReleaseButton?.SetLatched(armed);
            reserveReleaseButton?.SetText(armed ? "?" : "-");
            reserveReleaseButton?.SetEnabled(
                host && selected && EconomyFacade.SupplyReserve.CountOf(selectedOffer) > 0);
            reserveHoldButton?.SetEnabled(
                host && selected && EconomyFacade.SupplyReserve.Count < EconomyFacade.SupplyReserve.Capacity &&
                EconomyFacade.SupplyReserve.FactionStockOf(selectedOffer) > 0);
        }


        private const int ShopGridRows = 3;
        private const int ShopGridCols = 4;
        private const int ShopGridCapacity = ShopGridRows * ShopGridCols; // Shop grid capacity.
        private const float ShopTileHeight = 36f;
        private const float ShopTileGap = 4f;

        /// <summary>Build airframe tiles followed by fit, fuel, and purchase controls.</summary>
        private static float AddShop(RectTransform parent, float y)
        {
            if (!Plugin.Settings.ShopEnabled.Value) return AddDispatchBrief(parent, y);

            shopTemplatePopup = new AvKit.Popup(parent, PanelWidth);

            y = Heading(parent, y, "AIRFRAME REQUISITION");
            float shopGridTop = y;
            y = AddShopGrid(parent, shopGridTop);

            // Create pager after tiles to preserve draw and click priority at the grid boundary.
            shopPager = HeaderPager(parent, shopGridTop + Space5,
                                    () => TurnPage(-1), () => TurnPage(1),
                                    out shopPrevButton, out shopPageLabel, out shopNextButton);
            shopPager.SetAsLastSibling();
            y -= Gap;

            // Give offer details a full-width row beside no competing purchase control.
            offerDetailLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                     Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + 2f;

            // Select purchase fit here alongside stock and price; LOADOUT only builds reusable
            // templates.
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

            // Open the template list beneath its trigger.
            shopTemplateRowY = y - RowHeight;
            shopTemplateRowX = Pad + fitGutter;
            shopTemplateRowWidth = fitButtonWidth;

            // Keep fuel beside template selection as a launch modifier.
            fullFuelButton = WingUi.Button(
                parent, "", new Rect(Pad + fitGutter + fitButtonWidth + Gap, y, fuelWidth, RowHeight),
                FontSmall, UiButtonStyle.Quiet, CycleSpawnFuel)
                .WithTooltip(OrderHint.FullFuel);
            y -= RowHeight + Space1;

            offerLoadoutLabel = Label(parent, "", new Rect(Pad, y, PanelWidth - Pad * 2f, LineHeight),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= LineHeight + Space1;

            y = AddLaunchFrom(parent, y);
            y = AddDispatchBrief(parent, y);

            // Emphasise requisition; show over-limit permission as a secondary latched modifier.
            const float buyWidth = WingUi.ButtonPrimary;
            float exceedWidth = PanelWidth - Pad * 2f - Gap - buyWidth;
            exceedLimitButton = WingUi.Button(parent, "", new Rect(Pad, y, exceedWidth, RowHeight),
                                              FontBody, UiButtonStyle.Toggle, ToggleExceedLimit)
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

        /// <summary>Compact pre-purchase summary of aircraft, pilot, fit, and launch permission.</summary>
        private static float AddDispatchBrief(RectTransform parent, float y)
        {
            const float height = 108f;
            const float portrait = 56f;
            const float iconSize = 38f;
            float w = PanelWidth - Pad * 2f;
            float left = Pad + Space3;
            float pilotY = y - 24f;
            float dividerX = Pad + w * 0.64f;
            float aircraftX = dividerX + Space3;
            float aircraftW = Pad + w - Space3 - aircraftX;

            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, height), WingUi.RailCyan);
            supplyDispatchRail = rail;
            Label(parent, "NEXT DISPATCH", new Rect(left, y - 4f, w - Space3 * 2f, LineHeight),
                  WingUi.RailCyan, FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            var (_, pilotRail) = WingUi.TacticalCard(parent,
                new Rect(left, pilotY, portrait, portrait), RankColor(WingRank.Rookie));
            supplyPilotRail = pilotRail;
            var portraitMask = new GameObject("SupplyPilotPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            var portraitRect = portraitMask.GetComponent<RectTransform>();
            portraitRect.SetParent(parent, worldPositionStays: false);
            Place(portraitRect, new Rect(left + 3f, pilotY - 3f, portrait - 6f, portrait - 6f));
            supplyPilotPortrait = AddSprite(portraitRect, "SupplyPilotPortrait", PersonnelFacade.Portraits.Sprite,
                new Rect(-5f, 8f, 60f, 90f), Color.white);

            float dossierX = left + portrait + Space2;
            float dossierW = dividerX - Space2 - dossierX;
            supplyPilotNameLabel = Label(parent, "", new Rect(dossierX, pilotY, dossierW, Space5),
                Green(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            supplyPilotRankLabel = Label(parent, "", new Rect(dossierX, pilotY - Space5, dossierW, LineHeight),
                Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            supplyPilotStatusLabel = Label(parent, "", new Rect(dossierX, pilotY - Space5 - LineHeight, dossierW, LineHeight),
                Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            foreach (TMP_Text label in new[] { supplyPilotNameLabel, supplyPilotRankLabel, supplyPilotStatusLabel })
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }

            Rule(parent, new Rect(dividerX, pilotY, 1f, portrait), FrameColor());
            supplyDispatchIcon = AddSprite(parent, "DispatchAirframeIcon", IconFactory.Get("airframe"),
                new Rect(aircraftX + (aircraftW - iconSize) * 0.5f, pilotY, iconSize, iconSize), Dim());
            supplyDispatchAirframeLabel = Label(parent, "", new Rect(aircraftX, pilotY - 40f, aircraftW, LineHeight),
                Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
            supplyDispatchAirframeLabel.enableWordWrapping = false;
            supplyDispatchAirframeLabel.overflowMode = TextOverflowModes.Ellipsis;

            Rule(parent, new Rect(left, y - 84f, w - Space3 * 2f, 1f), FrameColor());
            supplyDispatchStateLabel = Label(parent, "", new Rect(left, y - 88f, w - Space3 * 2f, LineHeight),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            supplyDispatchStateLabel.enableWordWrapping = false;
            supplyDispatchStateLabel.overflowMode = TextOverflowModes.Ellipsis;
            return y - height - Gap;
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
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.Catalogue();
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopGridCapacity));
            shopPage = Mathf.Clamp(shopPage + direction, 0, pages - 1);
            RefreshShop();
        }

        /// <summary>Toggle explicit over-cap permission; surcharge applies only when needed. Keep the
        /// control clickable so rank refusal can explain itself.</summary>
        private static void ToggleExceedLimit()
        {
            if (!EconomyFacade.Shop.MeetsExceedLimitRank)
            {
                WingCommandManager.Instance?.Toast(
                    "Requisitioning past the squadron limit requires rank " + EconomyFacade.Shop.ExceedLimitRank);
                return;
            }

            EconomyFacade.Shop.ExceedLimit = !EconomyFacade.Shop.ExceedLimit;
            WingCommandManager.Instance?.Toast(EconomyFacade.Shop.ExceedLimit
                ? "Over-limit requisition allowed at " +
                  EconomyFacade.Shop.ExceedLimitMultiplier.ToString("0.##") + "x list price"
                : "Over-limit requisition disallowed");
        }

        /// <summary>Cycle launch fuel through 25/50/75/100%. Changes affect later purchases and do not
        /// alter price.</summary>
        private static void CycleSpawnFuel()
        {
            EconomyFacade.Shop.CycleSpawnFuel();
            WingCommandManager.Instance?.Toast(
                "Requisitions launch with " +
                Mathf.RoundToInt(EconomyFacade.Shop.SpawnFuelLevel * 100f) + "% fuel");
        }

        private static void RequisitionSelected()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            bool bought = EconomyFacade.Shop.Buy(selectedOffer, out string why, out float paid);
            WingCommandManager.Instance?.Toast(bought
                ? selectedOffer.unitName + " requisitioned for " + Grouped(paid) +
                  " - departing friendly base"
                : why);
            if (bought)
            {
                RefreshSupplyPilot();
            }
        }

        /// <summary>Refresh catalogue tiles and allocation display.</summary>
        private static void RefreshShop()
        {
            if (!Plugin.Settings.ShopEnabled.Value || shopTiles.Count == 0) return;

            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.Catalogue();

            // Revalidate page bounds on every refresh because changing stock can shrink the catalogue.
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopGridCapacity));
            if (shopPage >= pages) shopPage = pages - 1;
            if (shopPage < 0) shopPage = 0;

            RefreshHeaderPager(shopPager, shopPrevButton, shopPageLabel, shopNextButton, shopPage, pages);

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

        /// <summary>Clear shared aircraft selection when it is no longer offered, wherever the catalogue
        /// is read.</summary>
        private static void ValidateSelectedOffer(IReadOnlyList<WingShop.Offer> offers)
        {
            if (selectedOffer == null) return;

            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Definition == selectedOffer) return;
            }
            selectedOffer = null;
        }

        /// <summary>Refresh selected-airframe details and action controls.</summary>
        private static void RefreshOfferDetail(IReadOnlyList<WingShop.Offer> offers)
        {
            WingShop.PurchaseQuote quote = EconomyFacade.Shop.Quote(selectedOffer);
            bool overLimit = quote.OverLimit;

            if (exceedLimitButton != null)
            {
                exceedLimitButton.SetText(
                    "OVER LIMIT  x" + EconomyFacade.Shop.ExceedLimitMultiplier.ToString("0.##"));
                exceedLimitButton.SetLatched(EconomyFacade.Shop.ExceedLimit);
            }

            if (fullFuelButton != null)
            {
                // Show the chosen fuel percentage without a permanently lit toggle state.
                fullFuelButton.SetText(
                    "FUEL  " + Mathf.RoundToInt(EconomyFacade.Shop.SpawnFuelLevel * 100f) + "%");
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
                    int reservedCount = EconomyFacade.SupplyReserve.CountOf(selectedOffer);
                    int ownedCount = EconomyFacade.SupplyReserve.OwnedOf(selectedOffer);
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
                    // Distinguish saved recovered fit from the future purchase plan in the offer
                    // breakdown.
                    WingLoadoutChoice fit = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
                    bool fromReserve = false;

                    if (EconomyFacade.SupplyReserve.PeekLoadout(selectedOffer,
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
            RefreshDispatchBrief(quote);
        }

        /// <summary>Render the dispatch summary from the live purchase quote, showing pilot, fit, launch,
        /// and eligibility at confirmation.</summary>
        private static void RefreshDispatchBrief(WingShop.PurchaseQuote quote)
        {
            if (supplyDispatchAirframeLabel == null) return;

            if (selectedOffer == null)
            {
                supplyDispatchAirframeLabel.text = "NO AIRFRAME SELECTED";
                supplyDispatchStateLabel.text = "SELECT AN AIRFRAME TO PREPARE A DISPATCH.";
                supplyDispatchAirframeLabel.color = Dim();
                supplyDispatchStateLabel.color = Dim();
                if (supplyDispatchRail != null) supplyDispatchRail.color = Dim();
                if (supplyDispatchIcon != null)
                {
                    supplyDispatchIcon.sprite = IconFactory.Get("airframe");
                    supplyDispatchIcon.color = Dim();
                }
                return;
            }

            string designation = !string.IsNullOrEmpty(selectedOffer.unitName)
                ? selectedOffer.unitName
                : selectedOffer.code;

            WingLoadoutChoice fit = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
            bool fromReserve = EconomyFacade.SupplyReserve.PeekLoadout(selectedOffer,
                                                              out WingLoadoutChoice recoveredFit);
            if (fromReserve) fit = recoveredFit;

            string fuel = fromReserve
                ? "AS RECOVERED"
                : "FUEL " + Mathf.RoundToInt(EconomyFacade.Shop.SpawnFuelLevel * 100f) + "%";
            string fitLabel = fromReserve
                ? "RESERVE FIT"
                : AvTheme.Truncate(EconomyFacade.LoadoutCatalog.Label(fit), 16).ToUpperInvariant();

            supplyDispatchAirframeLabel.text = designation;
            supplyDispatchStateLabel.text = quote.CanBuy
                ? "ALLOWED  ·  " + fitLabel + "  ·  " + fuel
                : "BLOCKED — " +
                  (string.IsNullOrEmpty(quote.Reason)
                      ? "requirements not met."
                      : AvTheme.Truncate(quote.Reason, 38));

            bool ready = quote.CanBuy;
            supplyDispatchAirframeLabel.color = ready ? Friendly() : Warning();
            supplyDispatchStateLabel.color = ready ? Green() : Warning();
            if (supplyDispatchRail != null)
                supplyDispatchRail.color = ready
                    ? fromReserve ? WingUi.RailCyan : WingUi.RailEmerald
                    : Warning();

            if (supplyDispatchIcon != null)
            {
                supplyDispatchIcon.sprite = IconFactory.Aircraft(selectedOffer);
                supplyDispatchIcon.color = ready ? Friendly() : Dim();
            }
        }

        /// <summary>Display the planned fit in the picker; describe any recovered-fit override separately
        /// because this control cannot change it.</summary>
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

            WingLoadoutChoice planned = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);

            // Display Standard when a planned template has been deleted, matching build fallback.
            if (planned.IsTemplate && !EconomyFacade.LoadoutTemplates.Exists(planned.TemplateId))
            {
                EconomyFacade.LoadoutBook.Plan(selectedOffer, planned.WithTemplate(null));
                planned = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
            }

            shopTemplateButton.SetText(
                AvTheme.Truncate(EconomyFacade.LoadoutCatalog.Label(planned), 34)
                       .ToUpperInvariant());
            shopTemplateButton.SetEnabled(true);
            shopTemplateButton.SetLatched(planned.IsTemplate);
        }

        /// <summary>Choose the live player-default Standard fit or a saved template for the next purchase;
        /// Standard is always first.</summary>
        private static void OpenShopTemplatePicker()
        {
            if (selectedOffer == null)
            {
                WingCommandManager.Instance?.Toast("Select an airframe first");
                return;
            }

            WingLoadoutChoice planned = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
            IReadOnlyList<LoadoutTemplateRecord> mine = EconomyFacade.LoadoutTemplates.For(selectedOffer);

            // Use null as the Standard entry's ID.
            var ids = new List<string>(mine.Count + 1) { null };
            popupEntries.Clear();
            popupEntries.Add(new AvKit.PopupEntry(
                "STANDARD FIT", "player default this mission", !planned.IsTemplate));

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

                // Read the current plan again when the popup callback runs; captured state may be
                // stale.
                WingLoadoutChoice current = EconomyFacade.LoadoutBook.PlannedFor(target);
                EconomyFacade.LoadoutBook.Plan(target, current.WithTemplate(ids[index]));
            });
        }

        /// <summary>Refresh funds, wing occupancy, and faction AI capacity.</summary>
        private static void RefreshSupplyStatus()
        {
            if (assignmentCostLabel != null)
            {
                float? cost = WingCommandManager.Instance?.SelectedAssignmentCost();
                assignmentCostLabel.text = cost.HasValue ? Grouped(cost.Value) + " CR" : "— CR";
                assignmentCostLabel.color = !cost.HasValue ? Dim() :
                    cost.Value > EconomyFacade.Shop.Allocation ? Warning() : Friendly();
            }
            if (supplyFundsLabel == null) return;

            int wing = WingCommandManager.Instance?.Wing?.Count ?? 0;
            supplyFundsLabel.text = "YOUR FLIGHT  " + wing + " / " + WingRegistry.WingLimitLabel;

            WingShop.SquadronState squadron = EconomyFacade.Shop.Squadron();
            string text = "AI POOL  " + squadron.Active + " / " + squadron.Limit;
            if (Plugin.Settings.CheatNoWingLimit)
            {
                supplySquadronLabel.text = text + "  ·  CAP WAIVED";
                supplySquadronLabel.color = Warning();
                return;
            }

            if (!squadron.AtCapacity)
            {
                supplySquadronLabel.text = text;
                supplySquadronLabel.color = Friendly();
                return;
            }

            // Explain capacity refusal and available remedies in persistent status.
            if (EconomyFacade.Shop.ExceedLimit && EconomyFacade.Shop.MeetsExceedLimitRank)
            {
                // Keep the surcharge on its button; status only signals over-cap operation.
                supplySquadronLabel.text = text + "  ·  OVER LIMIT";
            }
            else if (!EconomyFacade.Shop.MeetsExceedLimitRank)
            {
                supplySquadronLabel.text = text + "  ·  FULL (RANK " + EconomyFacade.Shop.ExceedLimitRank + "+)";
            }
            else
            {
                supplySquadronLabel.text = text + "  ·  FULL";
            }
            supplySquadronLabel.color = Warning();
        }
        /// <summary>Purchasable-airframe tile with silhouette, code, stock, and cost.</summary>
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
                code.enableAutoSizing = true;
                code.fontSizeMin = 6.5f;
                code.fontSizeMax = FontMicro;
                code.overflowMode = TextOverflowModes.Ellipsis;

                priceStock = Label(rt, "", new Rect(textLeft, -18f, textWidth, 14f), Green(),
                                   FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                priceStock.enableAutoSizing = true;
                priceStock.fontSizeMin = 6.5f;
                priceStock.fontSizeMax = FontMicro;
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

                int owned = EconomyFacade.SupplyReserve.OwnedOf(offer.Definition);
                float cost = EconomyFacade.Shop.CurrentPriceOf(offer.Definition);
                bool affordable = EconomyFacade.Shop.Allocation >= cost;
                bool selected = selectedOffer == offer.Definition;
                bool canSpawn = EconomyFacade.LaunchFields.CanAnyAllowedLaunch(hq, offer.Definition);

                Sprite sprite = IconFactory.Aircraft(offer.Definition);
                icon.sprite = sprite;
                icon.color = selected ? Color.white : (canSpawn ? (affordable ? Color.white : Dim()) : Dim());

                string codeStr = !string.IsNullOrEmpty(offer.Definition.code) ? offer.Definition.code : offer.Name;
                code.text = AvTheme.Truncate(codeStr, 9);
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

                string spawnNotice = " | " + EconomyFacade.HangarStock.AirframeLaunchText(offer.Definition, allowedOnly: true);
                if (!canSpawn)
                    spawnNotice = " | [!] " + EconomyFacade.HangarStock.AirframeLaunchText(offer.Definition, allowedOnly: false);
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

        /// <summary>Friendly launch field with an enable/disable control.</summary>
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

                const float statusWidth = 66f;
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

                bool allowed = EconomyFacade.LaunchFields.IsAllowed(airbase);
                bool hasAirframe = selectedOffer != null;
                bool canProduce = hasAirframe && EconomyFacade.LaunchFields.CanProduce(airbase, selectedOffer);
                bool jammed = allowed && (!hasAirframe || canProduce) &&
                              EconomyFacade.DepartureLane.IsJammed(airbase);

                LaunchBaseStatus state = LaunchBaseStatusPolicy.Evaluate(allowed, canProduce, hasAirframe);
                string badge = LaunchBaseStatusPolicy.BadgeText(state);

                check.SetLatched(allowed);
                check.SetText(allowed ? "ON" : "--");

                name.text = AvTheme.Truncate(EconomyFacade.LaunchFields.DisplayName(airbase), 26);
                status.text = jammed ? "JAMMED" : badge;

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
                    EconomyFacade.LaunchFields.DisplayName(airbase),
                    selectedOffer != null ? selectedOffer.unitName : null,
                    allowed,
                    canProduce);
                string stock = EconomyFacade.HangarStock.FieldStockText(airbase);
                if (!string.IsNullOrEmpty(stock)) tooltip += " — " + stock;
                if (jammed)
                {
                    status.color = Alert();
                    name.color = Warning();
                    tooltip = EconomyFacade.LaunchFields.DisplayName(airbase) +
                        " — Runway queue blocked by an aircraft that is no longer departing. " +
                        "Choose another launch base or Any.";
                    if (!string.IsNullOrEmpty(stock)) tooltip += " — " + stock;
                }
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
                EconomyFacade.LaunchFields.SetAllowed(bound, !EconomyFacade.LaunchFields.IsAllowed(bound));
                RefreshLaunchFrom();
                RefreshShop();
            }
        }

    }
}
