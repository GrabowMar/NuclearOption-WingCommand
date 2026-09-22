using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics.Ui;

namespace WingCommand
{
    /// <summary>SUPPLY page: a numbered pilot, airframe, fit, launch-base and dispatch workflow.
    /// Steps 1-4 scroll in one bounded viewport; the dispatch card is pinned above the strip.</summary>
    internal static partial class WmcScreen
    {
        // ---------------------------------------------------------------- layout state

        /// <summary>Height of the pinned DISPATCH block at the body foot.</summary>
        private const float DispatchPinHeight = 84f;

        /// <summary>Body height at which tiles and launch rows take their roomier sizes.</summary>
        private const float SupplyTallBodyHeight = 560f;

        /// <summary>Window the map layer keeps a pending assignment confirmed for.</summary>
        private const float AssignConfirmSeconds = 5f;

        /// <summary>Scroll content for steps 1-4, and its viewport.</summary>
        private static RectTransform supplyBody;
        private static ScrollRect supplyScroll;
        private static bool supplyTall;
        private static int launchRowsPerPage = 3;
        private static float supplyAssignArmedUntil;

        /// <summary>Why the previous airframe selection was dropped, until a new one is chosen.</summary>
        private static string supplySelectionNotice;

        private static TMP_Text supplyStep1State;
        private static TMP_Text supplyStep2State;
        private static TMP_Text supplyStep3State;
        private static TMP_Text supplyStep4State;
        private static readonly Image[] supplyStepRails = new Image[4];
        private static WingButton assignButton;
        private static WingButton supplyOpenWingButton;
        private static Image supplyShopEmptyCard;
        private static Image supplyLaunchEmptyCard;
        private static Image supplyDispatchChip;
        private static TMP_Text supplyDispatchDetailLabel;
        private static TMP_Text supplyDispatchNoteLabel;

        private static TMP_Text assignmentCostLabel;
        private static TMP_Text shopEmptyLabel;
        private static TMP_Text launchEmptyLabel;
        private static Image supplyAssignMeter;
        private static Image supplyReserveMeter;
        private static Image supplyFuelMeter;
        private static readonly Confirmation reserveRelease = new Confirmation();

        private const int ShopGridRows = 2;
        private const int ShopGridCols = 3;
        private const int ShopGridCapacity = ShopGridRows * ShopGridCols;
        private const float ShopTileGap = 8f;
        private const float LaunchCheckWidth = 38f;

        /// <summary>Fixed width of the right-side state chip on a numbered step head.</summary>
        private const float StepChipWidth = 96f;

        // ------------------------------------------------------------------ geometry

        /// <summary>Numbered step head: number chip, accent rail, title and a right-side state chip.
        /// Everything stays inside the 22-unit head so the first step row cannot cover it.</summary>
        private static float StepHeader(RectTransform parent, float y, int step, string number, string title,
                                        out TMP_Text state)
        {
            var numberRect = new Rect(Pad, y - 4f, 16f, 16f);
            Panel(parent, numberRect, AvTheme.SurfaceInert);
            Outline(parent, numberRect, FrameColor());
            Label(parent, number, numberRect, Green(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            var chipGo = new GameObject("StepStateChip" + number, typeof(RectTransform));
            var chipRect = chipGo.GetComponent<RectTransform>();
            chipRect.SetParent(parent, worldPositionStays: false);
            Place(chipRect, new Rect(Pad + ContentWidth - StepChipWidth, y - 4f, StepChipWidth, 16f));
            Panel(chipRect, new Rect(0f, 0f, StepChipWidth, 16f), AvTheme.SurfaceInert);
            Outline(chipRect, new Rect(0f, 0f, StepChipWidth, 16f), FrameColor());
            supplyStepRails[step - 1] = Rule(chipRect, new Rect(0f, 0f, 3f, 16f), WingUi.RailInert);
            state = Label(chipRect, "", new Rect(12f, 0f, StepChipWidth - 16f, 16f),
                          Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            return SectionHeader(parent, Pad + 20f, y, ContentWidth - 20f - StepChipWidth - Space1, title);
        }

        /// <summary>Write a step's right-side state chip: label text, text colour and rail.</summary>
        private static void SetStepState(TMP_Text label, Image rail, string text, Color color)
        {
            if (label != null)
            {
                label.text = text;
                label.color = color;
            }
            if (rail != null) rail.color = color;
        }

        // ------------------------------------------------------------------ step 1

        /// <summary>Step 1 PILOT &amp; CREW: opens the scrolling body and draws the pilot chooser.</summary>
        private static float AddSupplyStatus(RectTransform parent, float y)
        {
            // The shell metric block superseded this status card; keep the legacy readouts alive
            // but off-screen so the offline UI check can still bind them.
            supplyFundsLabel = Label(parent, "", new Rect(0f, 0f, 1f, 1f), Dim(), FontMicro,
                                     FontStyles.Bold, TextAlignmentOptions.Left);
            supplySquadronLabel = Label(parent, "", new Rect(0f, 0f, 1f, 1f), Dim(), FontMicro,
                                        FontStyles.Normal, TextAlignmentOptions.Right);
            supplyFundsLabel.gameObject.SetActive(false);
            supplySquadronLabel.gameObject.SetActive(false);

            supplyTall = BodyHeight >= SupplyTallBodyHeight;
            // The dispatch card is pinned under this viewport. Giving the scroll the whole body
            // painted launch bases underneath the card.
            float viewH = Mathf.Max(160f, BodyHeight - DispatchPinHeight - Space2);
            BuildViewport(parent, new Rect(0f, BodyTop, PageWidth, viewH), "SupplyViewport",
                          out supplyBody, out supplyScroll);
            pageScrolls[(int)Page.Supply] = supplyScroll;

            float cursor = 0f;
            cursor = StepHeader(supplyBody, cursor, 1, "1", "PILOT & CREW", out supplyStep1State);
            cursor = AddPilotCard(supplyBody, cursor);
            cursor = AddAssignRow(supplyBody, cursor);
            return cursor - Space2;
        }

        private static float AddPilotCard(RectTransform parent, float y)
        {
            const float cardHeight = 46f;
            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, y, ContentWidth, cardHeight),
                                                RankColor(WingRank.Rookie));
            supplyPilotRail = rail;

            // The whole card inspects the pilot on WING; the hit target precedes the stepper so the
            // arrows stay clickable.
            HitButton(parent, new Rect(Pad, y, ContentWidth, cardHeight), () => SetPage(Page.Wing))
                .WithTooltip("Open the WING tab to inspect, recruit, or customise pilots.");

            var portraitGo = new GameObject("SupplyPilotPortraitMask", typeof(RectTransform), typeof(RectMask2D));
            var portraitMask = portraitGo.GetComponent<RectTransform>();
            portraitMask.SetParent(parent, worldPositionStays: false);
            Place(portraitMask, new Rect(Pad + 6f, y - 3f, 36f, 40f));
            supplyPilotPortrait = AddSprite(portraitMask, "SupplyPilotPortrait", PersonnelFacade.Portraits.Sprite,
                                            new Rect(-3f, 2f, 42f, 52f), Color.white);

            float textX = Pad + 50f;
            float stepperX = Pad + 330f;
            float textWidth = stepperX - textX - Space2;
            supplyPilotNameLabel = Label(parent, "", new Rect(textX, y - 3f, textWidth, 16f),
                                         WingUi.TextPrimary, FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            float split = textWidth * 0.5f;
            supplyPilotRankLabel = Label(parent, "", new Rect(textX, y - 20f, split, 14f),
                                         Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            supplyPilotStatusLabel = Label(parent, "", new Rect(textX + split, y - 20f, textWidth - split, 14f),
                                           Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            foreach (TMP_Text label in new[] { supplyPilotNameLabel, supplyPilotRankLabel, supplyPilotStatusLabel })
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }

            WingButton[] arrows = Stepper(parent, stepperX, y - 8f, 114f, out supplyPilotCountLabel,
                                          () => CycleSupplyPilot(-1), () => CycleSupplyPilot(1),
                                          "Previous or next available pilot");
            supplyPilotPrev = arrows[0];
            supplyPilotNext = arrows[1];

            return y - cardHeight - Space1;
        }

        private static float AddAssignRow(RectTransform parent, float y)
        {
            const float rowHeight = KeyHeight;
            const float buttonWidth = 168f;
            assignButton = WingUi.Button(parent, "ASSIGN SELECTED",
                                         new Rect(Pad, y, buttonWidth, rowHeight),
                                         FontMicro, UiButtonStyle.Default, OnAssignPressed)
                                    .WithTooltip(OrderHint.AssignSelected);
            assignmentCostLabel = Label(parent, "— CR",
                                        new Rect(Pad + buttonWidth + Space2, y,
                                                 ContentWidth - buttonWidth - Space2, rowHeight),
                                        Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            supplyOpenWingButton = WingUi.Button(parent, "OPEN WING",
                                             new Rect(Pad, y, buttonWidth, rowHeight),
                                             FontMicro, UiButtonStyle.Primary, () => SetPage(Page.Wing))
                                        .WithTooltip("Open WING to recruit or customise pilots.");
            StampKey(supplyOpenWingButton, "posture", WingUi.TextPrimary, 24f);
            supplyOpenWingButton.gameObject.SetActive(false);

            // Cost against squadron funds, so the assignment price is a length as well as a number.
            supplyAssignMeter = AvKit.ProgressBar(parent,
                new Rect(Pad, y - rowHeight - Space1, ContentWidth, 3f), 0f, Green());
            return y - rowHeight - Space1 - 3f - Space1;
        }

        /// <summary>Mirror the map layer's five-second two-press assignment window in the UI.</summary>
        private static void OnAssignPressed()
        {
            bool eligible = WingCommandManager.Instance?.SelectedAssignmentCost().HasValue == true;
            bool armed = eligible && Time.unscaledTime <= supplyAssignArmedUntil;
            supplyAssignArmedUntil = eligible && !armed ? Time.unscaledTime + AssignConfirmSeconds : 0f;
            WingCommandManager.Instance?.AddSelectedFromMap();
            RefreshSupplyPilot();
        }

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

        /// <summary>Update the SUPPLY pilot chooser and assignment row.</summary>
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

            bool hasPilots = selectable.Count > 0;
            supplyOpenWingButton?.gameObject.SetActive(!hasPilots);
            assignButton?.gameObject.SetActive(hasPilots);
            SetStepState(supplyStep1State, supplyStepRails[0],
                         hasPilots ? selectable.Count + " AVAILABLE" : "NO PILOTS",
                         hasPilots ? Dim() : Warning());

            bool armed = hasPilots && Time.unscaledTime <= supplyAssignArmedUntil;
            assignButton?.SetText(armed ? "ASSIGN SELECTED?" : "ASSIGN SELECTED");
            assignButton?.SetLatched(armed);

            float? cost = WingCommandManager.Instance?.SelectedAssignmentCost();
            if (assignmentCostLabel != null)
            {
                if (!hasPilots)
                {
                    assignmentCostLabel.text = "Recruit a pilot on WING";
                    assignmentCostLabel.color = Dim();
                }
                else if (armed)
                {
                    assignmentCostLabel.text = cost.HasValue ? "CONFIRM  ·  " + Grouped(cost.Value) + " CR" : "CONFIRM";
                    assignmentCostLabel.color = Warning();
                }
                else
                {
                    assignmentCostLabel.text = cost.HasValue ? Grouped(cost.Value) + " CR" : "Select friendly AI on the map";
                    assignmentCostLabel.color = cost.HasValue && cost.Value > EconomyFacade.Shop.Allocation
                        ? Warning() : Dim();
                }
            }

            if (supplyAssignMeter != null)
            {
                float funds = EconomyFacade.Shop.Allocation;
                bool priced = hasPilots && cost.HasValue && funds > 0f;
                supplyAssignMeter.fillAmount = priced ? Mathf.Clamp01(cost.Value / funds) : 0f;
                supplyAssignMeter.color = priced && cost.Value > funds ? Warning() : Green();
            }

            if (sel == null)
            {
                if (supplyPilotNameLabel != null)
                {
                    supplyPilotNameLabel.text = "NO PILOTS ON THE ROSTER";
                    supplyPilotNameLabel.color = Warning();
                }
                if (supplyPilotRankLabel != null)
                {
                    supplyPilotRankLabel.text = "A combat pilot is required to requisition aircraft";
                    supplyPilotRankLabel.color = Dim();
                }
                if (supplyPilotStatusLabel != null)
                {
                    supplyPilotStatusLabel.text = "OPEN WING";
                    supplyPilotStatusLabel.color = Friendly();
                }
                if (supplyPilotCountLabel != null) supplyPilotCountLabel.text = "0 / 0";
                supplyPilotPortrait.sprite = PersonnelFacade.Portraits.Sprite;
                supplyPilotPortrait.enabled = supplyPilotPortrait.sprite != null;
                supplyPilotPortrait.color = new Color(1f, 1f, 1f, 0.30f);
                if (supplyPilotRail != null) supplyPilotRail.color = Dim();
                supplyPilotPrev?.SetEnabled(false);
                supplyPilotNext?.SetEnabled(false);
                return;
            }

            if (supplyPilotNameLabel != null)
            {
                supplyPilotNameLabel.text = sel.Callsign + "  ·  " + sel.Name;
                supplyPilotNameLabel.color = WingUi.TextPrimary;
            }
            if (supplyPilotRankLabel != null)
            {
                supplyPilotRankLabel.text = PersonnelFacade.Roster.RankName(sel.Rank) + "   XP " + sel.Xp;
                supplyPilotRankLabel.color = Dim();
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

            supplyPilotPortrait.sprite = PersonnelFacade.Portraits.For(sel);
            supplyPilotPortrait.enabled = supplyPilotPortrait.sprite != null;
            supplyPilotPortrait.color = Color.white;
            if (supplyPilotRail != null) supplyPilotRail.color = RankColor(sel.Rank);
            supplyPilotPrev?.SetEnabled(selectable.Count > 1);
            supplyPilotNext?.SetEnabled(selectable.Count > 1);
        }

        // ------------------------------------------------------------------ step 2

        /// <summary>Step 2 AIRFRAME: 2x3 tiles plus the pager and reserve footer.</summary>
        private static float AddPilotSelection(RectTransform parent, float y)
        {
            RectTransform host = supplyBody ?? parent;
            y = StepHeader(host, y, 2, "2", "AIRFRAME", out supplyStep2State);

            // Tiles take the room the taller body leaves after the fixed rows, within legible bounds.
            float tileHeight = supplyTall ? 64f : 56f;
            float gridHeight = tileHeight * ShopGridRows + ShopTileGap * (ShopGridRows - 1);

            var (shopCard, _) = WingUi.TacticalCard(host,
                new Rect(Pad, y, ContentWidth, gridHeight), WingUi.RailInert);
            supplyShopEmptyCard = shopCard;
            supplyShopEmptyCard.gameObject.SetActive(false);
            shopEmptyLabel = Label(host, "NO AIRFRAMES AVAILABLE\nStock and compatible aircraft appear here.",
                                   new Rect(Pad + Space2, y - gridHeight * 0.5f + 14f,
                                            ContentWidth - Space4, 32f),
                                   Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            shopEmptyLabel.enableWordWrapping = true;
            shopEmptyLabel.gameObject.SetActive(false);

            shopTiles.Clear();
            float colWidth = (ContentWidth - ShopTileGap * (ShopGridCols - 1)) / ShopGridCols;
            for (int r = 0; r < ShopGridRows; r++)
            {
                for (int c = 0; c < ShopGridCols; c++)
                {
                    shopTiles.Add(new ShopAirframeTile(host,
                        new Rect(Pad + c * (colWidth + ShopTileGap),
                                 y - r * (tileHeight + ShopTileGap), colWidth, tileHeight),
                        r * ShopGridCols + c));
                }
            }
            y -= gridHeight + Space1;

            var pagerGo = new GameObject("SupplyPager", typeof(RectTransform));
            shopPager = pagerGo.GetComponent<RectTransform>();
            shopPager.SetParent(host, worldPositionStays: false);
            Place(shopPager, new Rect(Pad, y, 176f, KeyHeight));
            var (prev, label, next) = PagerRow(shopPager, 0f, 176f, () => TurnPage(-1), () => TurnPage(1),
                                              null, KeyHeight);
            shopPrevButton = prev;
            shopPageLabel = label;
            shopNextButton = next;

            reserveLabel = Label(host, "RESERVE 0/3", new Rect(Pad + 184f, y, 88f, KeyHeight),
                                 Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            supplyReserveMeter = AvKit.ProgressBar(host,
                new Rect(Pad + 184f, y - KeyHeight + Space1, 88f, 3f), 0f, Green());
            reserveHoldButton = WingUi.Button(host, "HOLD", new Rect(Pad + 276f, y, 72f, KeyHeight),
                                              FontMicro, UiButtonStyle.Default, HoldSelectedReserve)
                                       .WithTooltip("Hold the selected airframe in wing reserve; protects it from faction AI.");
            reserveReleaseButton = WingUi.Button(host, "RELEASE", new Rect(Pad + 352f, y, 92f, KeyHeight),
                                                 FontMicro, UiButtonStyle.Danger, ReleaseSelectedReserve)
                                          .WithTooltip("Release the selected reserve airframe back to faction stock. Press twice to confirm.");

            // Guidance lives in the HOLD/RELEASE tooltips; keep the legacy label alive but hidden.
            reserveHintLabel = Label(host, "", new Rect(0f, 0f, 1f, 1f), Dim(), FontMicro,
                                     FontStyles.Normal, TextAlignmentOptions.Left);
            reserveHintLabel.gameObject.SetActive(false);

            return y - KeyHeight - Space1;
        }

        private static void TurnPage(int direction)
        {
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.Catalogue();
            int pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)ShopGridCapacity));
            shopPage = Mathf.Clamp(shopPage + direction, 0, pages - 1);
            RefreshShop();
        }

        /// <summary>Refresh catalogue tiles, step notes, reserve footer and the pinned card.</summary>
        private static void RefreshShop()
        {
            if (shopTiles.Count == 0) return;

            bool shopEnabled = Plugin.Settings.ShopEnabled.Value;
            IReadOnlyList<WingShop.Offer> offers = EconomyFacade.Shop.Catalogue();

            bool empty = offers.Count == 0;
            if (shopEmptyLabel != null)
            {
                shopEmptyLabel.gameObject.SetActive(empty);
                if (empty)
                {
                    shopEmptyLabel.text = shopEnabled
                        ? "<b>NO AIRFRAMES AVAILABLE</b>\nStock and compatible aircraft appear here."
                        : "<b>SHOP DISABLED IN CONFIG</b>\nAssignment and reserve remain available.";
                }
            }
            supplyShopEmptyCard?.gameObject.SetActive(empty);

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

            SetStepState(supplyStep2State, supplyStepRails[1],
                         !shopEnabled ? "OFFLINE" : empty ? "NO AIRFRAMES" : offers.Count + " AVAILABLE",
                         !shopEnabled || empty ? Warning() : Dim());

            ValidateSelectedOffer(offers);
            RefreshOfferDetail(offers);
            RefreshLiveryControl();
            RefreshSupplyStatus();
        }

        /// <summary>Clear shared aircraft selection when it is no longer offered, keeping the reason so
        /// the strip can explain the missing selection instead of losing it silently.</summary>
        private static void ValidateSelectedOffer(IReadOnlyList<WingShop.Offer> offers)
        {
            if (selectedOffer == null) return;

            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Definition == selectedOffer) return;
            }

            supplySelectionNotice = selectedOffer.unitName +
                                    " is no longer offered here; select another airframe.";
            selectedOffer = null;
        }

        /// <summary>Refresh the FIT/FUEL controls and the two step-3 notes.</summary>
        private static void RefreshOfferDetail(IReadOnlyList<WingShop.Offer> offers)
        {
            bool shopEnabled = Plugin.Settings.ShopEnabled.Value;
            WingShop.PurchaseQuote quote = EconomyFacade.Shop.Quote(selectedOffer);
            bool overLimit = quote.OverLimit;
            float fuelLevel = EconomyFacade.Shop.SpawnFuelLevel;

            if (fullFuelButton != null)
            {
                fullFuelButton.SetText("FUEL  " + Mathf.RoundToInt(fuelLevel * 100f) + "%");
                fullFuelButton.SetLatched(false);
                fullFuelButton.SetEnabled(shopEnabled && selectedOffer != null);
            }

            if (supplyFuelMeter != null)
            {
                supplyFuelMeter.fillAmount = Mathf.Clamp01(fuelLevel);
                supplyFuelMeter.color = !shopEnabled ? Dim() : fuelLevel < 0.5f ? Warning() : Green();
            }

            if (offerDetailLabel != null)
            {
                if (!shopEnabled)
                {
                    offerDetailLabel.text = "SHOP OFFLINE  ·  assignment and reserve remain available";
                    offerDetailLabel.color = Warning();
                }
                else if (selectedOffer == null)
                {
                    offerDetailLabel.text = "SELECT AN AIRFRAME ABOVE TO CONFIGURE FIT & DEPLOYMENT";
                    offerDetailLabel.color = Dim();
                }
                else
                {
                    int ownedCount = EconomyFacade.SupplyReserve.OwnedOf(selectedOffer);
                    int heldCount = EconomyFacade.SupplyReserve.CountOf(selectedOffer);
                    int stock = 0;
                    for (int i = 0; i < offers.Count; i++)
                    {
                        if (offers[i].Definition != selectedOffer) continue;
                        stock = offers[i].Stock;
                        break;
                    }

                    string costPart = ownedCount > 0
                        ? "FREE — OWNED " + ownedCount
                        : Grouped(quote.Price) + " CR" + (overLimit ? "  ·  OVER LIMIT" : "");
                    offerDetailLabel.text = selectedOffer.unitName + "  ·  " + costPart + "  ·  " + stock +
                                            " IN STOCK" + (heldCount > 0 ? "  ·  " + heldCount + " HELD" : "");
                    offerDetailLabel.color = quote.CanBuy ? Friendly() : Warning();
                }
            }

            if (offerLoadoutLabel != null)
            {
                if (!shopEnabled || selectedOffer == null)
                {
                    offerLoadoutLabel.text = "";
                }
                else
                {
                    WingLoadoutChoice fit = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
                    bool fromReserve = EconomyFacade.SupplyReserve.PeekLoadout(selectedOffer,
                                                                               out WingLoadoutChoice stored);
                    if (fromReserve) fit = stored;
                    string label = AvTheme.Truncate(EconomyFacade.LoadoutCatalog.Label(fit), 22).ToUpperInvariant();
                    offerLoadoutLabel.text = fromReserve
                        ? "RECOVERED FIT KEPT — " + label + "  ·  the FIT choice applies to a new airframe"
                        : "FIT " + label + "  ·  build templates on LOADOUT";
                    offerLoadoutLabel.color = Dim();
                }
            }

            RefreshShopTemplateButton();

            if (!shopEnabled)
            {
                SetStepState(supplyStep3State, supplyStepRails[2], "OFFLINE", Warning());
            }
            else if (selectedOffer == null)
            {
                SetStepState(supplyStep3State, supplyStepRails[2], "NO AIRFRAME", Dim());
            }
            else
            {
                bool fromReserve = EconomyFacade.SupplyReserve.PeekLoadout(selectedOffer, out _);
                SetStepState(supplyStep3State, supplyStepRails[2],
                             fromReserve ? "RESERVE FIT" : "FUEL " + Mathf.RoundToInt(fuelLevel * 100f) + "%",
                             fromReserve ? WingUi.RailCyan : Dim());
            }
        }

        /// <summary>Display the planned fit in the picker; describe any recovered-fit override separately
        /// because this control cannot change it.</summary>
        private static void RefreshShopTemplateButton()
        {
            if (shopTemplateButton == null) return;

            if (!Plugin.Settings.ShopEnabled.Value)
            {
                shopTemplateButton.SetText("FIT  ·  OFFLINE");
                shopTemplateButton.SetEnabled(false);
                shopTemplateButton.SetLatched(false);
                return;
            }

            if (selectedOffer == null)
            {
                shopTemplateButton.SetText("FIT  ·  SELECT AIRFRAME");
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
                "FIT  ·  " + AvTheme.Truncate(EconomyFacade.LoadoutCatalog.Label(planned), 30).ToUpperInvariant());
            shopTemplateButton.SetEnabled(true);
            shopTemplateButton.SetLatched(planned.IsTemplate);
        }

        // ------------------------------------------------------------------ step 3

        /// <summary>Build the FIT/FUEL row, notes and step 4, then hand the content cursor to the
        /// pinned dispatch builder.</summary>
        private static float AddShop(RectTransform parent, float y)
        {
            RectTransform host = supplyBody ?? parent;
            y = StepHeader(host, y, 3, "3", "FIT & FUEL", out supplyStep3State);

            const float rowH = KeyHeight;
            const float fuelWidth = 116f;
            float fitWidth = ContentWidth - fuelWidth - 6f;
            Image fitTrack = Panel(host, new Rect(Pad, y, ContentWidth, rowH), WingUi.CardFill);
            fitTrack.sprite = AvSprites.Slot;
            fitTrack.type = Image.Type.Sliced;
            fitTrack.raycastTarget = false;
            shopTemplateButton = WingUi.Button(host, "FIT  ·  SELECT AIRFRAME",
                                               new Rect(Pad + 2f, y - 2f, fitWidth - 4f, rowH - 4f),
                                               FontMicro, UiButtonStyle.Default, OpenShopTemplatePicker)
                                        .WithTooltip(OrderHint.Fit);
            fullFuelButton = WingUi.Button(host, "FUEL  100%",
                                           new Rect(Pad + fitWidth + 4f, y - 2f, fuelWidth - 4f, rowH - 4f),
                                           FontMicro, UiButtonStyle.Toggle, CycleSpawnFuel)
                                    .WithTooltip(OrderHint.FullFuel);

            // The picker opens beneath its trigger; both coordinates live in content space so the
            // popup follows the scroll offset.
            shopTemplatePopup = new AvKit.Popup(host, PageWidth);
            shopTemplateRowX = Pad;
            shopTemplateRowWidth = fitWidth;
            shopTemplateRowY = y;
            y -= rowH + Space1;

            // Fuel level for the next requisition as a bar under the FIT/FUEL controls.
            supplyFuelMeter = AvKit.ProgressBar(host, new Rect(Pad, y, ContentWidth, 3f), 1f, Green());
            y -= 3f + Space1;

            Label(host, "LIVERY", new Rect(Pad, y, 58f, KeyHeight),
                  Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stepper(host, Pad + 62f, y, ContentWidth - 62f, out liveryLabel,
                    () => CycleLivery(-1), () => CycleLivery(1),
                    "Paint the next requisition of this airframe launches with.", KeyHeight);
            y -= KeyHeight + Space1;

            offerDetailLabel = Label(host, "", new Rect(Pad, y, ContentWidth, 14f),
                                     Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 14f + 1f;
            offerLoadoutLabel = Label(host, "", new Rect(Pad, y, ContentWidth, 14f),
                                      Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 14f + Space1;

            return AddLaunchFrom(host, y);
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
            RefreshShop();
        }

        /// <summary>Cycle launch fuel through 25/50/75/100%. Changes affect later purchases and do not
        /// alter price.</summary>
        private static void CycleSpawnFuel()
        {
            EconomyFacade.Shop.CycleSpawnFuel();
            WingCommandManager.Instance?.Toast(
                "Requisitions launch with " +
                Mathf.RoundToInt(EconomyFacade.Shop.SpawnFuelLevel * 100f) + "% fuel");
            RefreshShop();
        }

        // ------------------------------------------------------------------ step 4

        /// <summary>Build the launch-field toggles, pager and visible rows.</summary>
        private static float AddLaunchFrom(RectTransform parent, float y)
        {
            y = StepHeader(parent, y, 4, "4", "LAUNCH BASE", out supplyStep4State);

            const float modeHeight = KeyHeight;
            const float modeWidth = 248f;
            Image modeTrack = Panel(parent, new Rect(Pad, y, modeWidth, modeHeight), WingUi.CardFill);
            modeTrack.sprite = AvSprites.Slot;
            modeTrack.type = Image.Type.Sliced;
            modeTrack.raycastTarget = false;
            float segW = (modeWidth - 6f) * 0.5f;
            launchNearestButton = WingUi.Button(
                parent, "NEAREST", new Rect(Pad + 2f, y - 2f, segW, modeHeight - 4f),
                FontMicro, UiButtonStyle.Toggle, SelectNearestLaunchField)
                .WithTooltip("Select only the nearest available field for this airframe.");
            launchAnyButton = WingUi.Button(
                parent, "ANY FIELD", new Rect(Pad + 4f + segW, y - 2f, segW, modeHeight - 4f),
                FontMicro, UiButtonStyle.Toggle, SelectAnyLaunchField)
                .WithTooltip("Launch from the closest checked field with a free hangar. No per-field queue.");
            launchPager = HeaderPager(parent, y - 2f, () => TurnLaunchPage(-1), () => TurnLaunchPage(1),
                                      out launchPrevButton, out launchPageLabel, out launchNextButton);
            y -= modeHeight + Space1;

            // Launch rows grow with the body: 3 rows in a short bay, 4 with room for taller rows.
            launchRowsPerPage = supplyTall ? 4 : 3;
            float rowHeight = supplyTall ? 32f : 28f;
            float rowsHeight = launchRowsPerPage * rowHeight;

            var (launchCard, _) = WingUi.TacticalCard(parent,
                new Rect(Pad, y, ContentWidth, rowsHeight), WingUi.RailInert);
            supplyLaunchEmptyCard = launchCard;
            launchEmptyLabel = Label(parent, "NO FRIENDLY LAUNCH FIELDS\nJoin a faction to view available bases.",
                                     new Rect(Pad + Space2, y - rowsHeight * 0.5f + 14f,
                                              ContentWidth - Space4, 32f),
                                     Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            launchEmptyLabel.enableWordWrapping = true;

            launchRows.Clear();
            for (int i = 0; i < launchRowsPerPage; i++)
            {
                launchRows.Add(new LaunchBaseRow(parent,
                    new Rect(Pad, y - i * rowHeight, ContentWidth, rowHeight), i));
            }

            return y - rowsHeight - Space1;
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
            launchPage = nearest == null ? 0 : IndexOfLaunchField(nearest) / launchRowsPerPage;
            RefreshLaunchFrom();
            RefreshShop();
        }

        private static void SelectAnyLaunchField()
        {
            EconomyFacade.LaunchFields.Mode = HangarLaunchMode.Any;
            foreach (Airbase field in EconomyFacade.LaunchFields.Listing)
                EconomyFacade.LaunchFields.SetAllowed(field, true);
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
            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)launchRowsPerPage));
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

            bool shopEnabled = Plugin.Settings.ShopEnabled.Value;
            IReadOnlyList<Airbase> fields = EconomyFacade.LaunchFields.Listing;
            bool empty = fields.Count == 0;
            if (launchEmptyLabel != null)
            {
                launchEmptyLabel.gameObject.SetActive(empty);
                if (empty)
                    launchEmptyLabel.text = "<b>NO FRIENDLY LAUNCH FIELDS</b>\nFly with a wing leader in a faction to list bases.";
            }
            supplyLaunchEmptyCard?.gameObject.SetActive(empty);

            int pages = Mathf.Max(1, Mathf.CeilToInt(fields.Count / (float)launchRowsPerPage));
            if (launchPage >= pages) launchPage = pages - 1;
            if (launchPage < 0) launchPage = 0;

            bool nearest = EconomyFacade.LaunchFields.Mode == HangarLaunchMode.OnlyNearest;
            launchNearestButton?.SetLatched(nearest);
            launchAnyButton?.SetLatched(!nearest);
            launchNearestButton?.SetEnabled(shopEnabled && !empty);
            launchAnyButton?.SetEnabled(shopEnabled && !empty);

            RefreshHeaderPager(launchPager, launchPrevButton, launchPageLabel, launchNextButton,
                               launchPage, pages);

            int first = launchPage * launchRowsPerPage;
            for (int i = 0; i < launchRows.Count; i++)
            {
                int index = first + i;
                if (index < fields.Count) launchRows[i].Bind(fields[index], shopEnabled);
                else launchRows[i].Hide();
            }

            SetStepState(supplyStep4State, supplyStepRails[3],
                         !shopEnabled ? "OFFLINE" : empty ? "NO BASES" : fields.Count + " BASES",
                         !shopEnabled || empty ? Warning() : Dim());
        }

        // ------------------------------------------------------------------ reserve

        private static void HoldSelectedReserve()
        {
            bool held = EconomyFacade.SupplyReserve.Hold(selectedOffer, out string reason);
            WingCommandManager.Instance?.Toast(held
                ? selectedOffer.unitName + " held for the wing (" + EconomyFacade.SupplyReserve.Count +
                  "/" + EconomyFacade.SupplyReserve.Capacity + ")"
                : reason);
            if (!held) return;
            RefreshShop();
            RefreshReserve();
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
                RefreshReserve();
                return;
            }

            reserveRelease.Clear();
            bool released = EconomyFacade.SupplyReserve.Release(
                definition, out bool wasOwned, out string reason);
            WingCommandManager.Instance?.Toast(released
                ? definition.unitName + (wasOwned ? " ownership released" : " released") +
                  " to faction stock"
                : reason);
            if (!released) return;
            RefreshShop();
            RefreshReserve();
        }

        /// <summary>Refresh concrete reserve capacity and selected-airframe controls.</summary>
        private static void RefreshReserve()
        {
            if (reserveLabel == null) return;

            string hint;
            bool host = EconomyFacade.SupplyReserve.IsHost;
            bool selected = selectedOffer != null;

            if (!EconomyFacade.SupplyReserve.HasFaction)
            {
                reserveLabel.text = "RESERVE —";
                reserveLabel.color = Dim();
                hint = "Join a faction to use the wing reserve.";
                reserveReleaseButton?.SetEnabled(false);
                reserveHoldButton?.SetEnabled(false);
                reserveReleaseButton?.SetText("RELEASE");
                reserveReleaseButton?.SetLatched(false);
            }
            else
            {
                reserveLabel.text = "RESERVE " + EconomyFacade.SupplyReserve.Count + "/" +
                                    EconomyFacade.SupplyReserve.Capacity;
                reserveLabel.color = Friendly();
                hint = EconomyFacade.SupplyReserve.Count >= EconomyFacade.SupplyReserve.Capacity
                    ? "FULL — RELEASE a selected airframe before holding another."
                    : EconomyFacade.SupplyReserve.Count > 0
                        ? "Select an airframe row to RELEASE it, or HOLD another from faction stock."
                        : "Select an airframe row, then HOLD it to protect it from AI.";
                if (!host) hint = "HOST ONLY — reserve stock is managed by the host.";

                // Bind confirmation to the originally selected airframe; changing selection disarms
                // release.
                bool armed = reserveRelease.IsArmedFor(selectedOffer);
                reserveReleaseButton?.SetLatched(armed);
                reserveReleaseButton?.SetText(armed ? "RELEASE?" : "RELEASE");
                reserveReleaseButton?.SetEnabled(
                    host && selected && EconomyFacade.SupplyReserve.CountOf(selectedOffer) > 0);
                reserveHoldButton?.SetEnabled(
                    host && selected && EconomyFacade.SupplyReserve.Count < EconomyFacade.SupplyReserve.Capacity &&
                    EconomyFacade.SupplyReserve.FactionStockOf(selectedOffer) > 0);
            }

            if (supplyReserveMeter != null)
            {
                int capacity = EconomyFacade.SupplyReserve.Capacity;
                int count = EconomyFacade.SupplyReserve.Count;
                supplyReserveMeter.fillAmount = capacity > 0 ? Mathf.Clamp01(count / (float)capacity) : 0f;
                supplyReserveMeter.color = !EconomyFacade.SupplyReserve.HasFaction ? Dim()
                    : capacity > 0 && count >= capacity ? Warning() : Green();
            }

            if (reserveHintLabel != null)
            {
                reserveHintLabel.text = hint;
                reserveHintLabel.gameObject.SetActive(false);
            }
            reserveHoldButton?.WithTooltip("Hold the selected airframe in wing reserve. " + hint);
            reserveReleaseButton?.WithTooltip(
                "Release the selected reserve airframe back to faction stock. Press twice to confirm. " + hint);

            PublishSupplyStatus();
        }

        /// <summary>Write the strongest Supply status: a blocked purchase names its reason instead of
        /// leaving the strip on generic help. Called from the last Supply refresh. The shared shell
        /// then rewrites the strip with its ambient fallback (WmcScreen.cs Refresh case), so a
        /// Blocked line only stays visible once that shared call is dropped or delegated.</summary>
        private static void PublishSupplyStatus()
        {
            if (statusLabels[(int)Page.Supply] == null) return;

            if (selectedOffer == null)
            {
                WriteStatus(Page.Supply, StripKind.Help,
                            supplySelectionNotice ?? "Select an airframe in step 2 to build a dispatch.");
                return;
            }

            WingShop.PurchaseQuote quote = EconomyFacade.Shop.Quote(selectedOffer);
            if (!quote.CanBuy && !string.IsNullOrEmpty(quote.Reason))
            {
                WriteStatus(Page.Supply, StripKind.Blocked, quote.Reason);
                return;
            }

            string pending = PendingLine();
            RefreshStatusStrip(Page.Supply, pending != null
                ? pending + "  ·  press REQUISITION to launch."
                : "Choose a pilot, airframe and launch base, then requisition.");
        }

        // ------------------------------------------------------------------ step 5

        /// <summary>Pin the DISPATCH summary, over-limit policy chip and requisition above the strip.
        /// The three-line summary card is anchored to the region top and REQUISITION to BodyBottom,
        /// so nothing is placed below the body foot.</summary>
        private static float AddAssignment(RectTransform parent, float y)
        {
            // Grow the scroll tail so the pinned card never covers the last step at bottom scroll.
            Reflow(supplyScroll, y);

            const float cardHeight = 48f;
            float bottom = BodyBottom;
            float top = bottom + DispatchPinHeight;

            var (_, rail) = WingUi.TacticalCard(parent, new Rect(Pad, top, ContentWidth, cardHeight),
                                                WingUi.RailInert);
            supplyDispatchRail = rail;

            supplyDispatchChip = Panel(parent, new Rect(Pad + 6f, top - 4f, 96f, 16f), RowColor());
            supplyDispatchStateLabel = Label(parent, "", new Rect(Pad + 6f, top - 4f, 96f, 16f),
                                             Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            supplyDispatchAirframeLabel = Label(parent, "", new Rect(Pad + 108f, top - 4f,
                                                                     ContentWidth - 136f, 16f),
                                                Friendly(), FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            supplyDispatchAirframeLabel.enableWordWrapping = false;
            supplyDispatchAirframeLabel.overflowMode = TextOverflowModes.Ellipsis;
            supplyDispatchIcon = AddSprite(parent, "DispatchAirframeIcon", IconFactory.Get("airframe"),
                                           new Rect(Pad + ContentWidth - 22f, top - 4f, 16f, 16f), Dim());
            supplyDispatchDetailLabel = Label(parent, "", new Rect(Pad + 6f, top - 20f, ContentWidth - 12f, 13f),
                                              Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            supplyDispatchNoteLabel = Label(parent, "", new Rect(Pad + 6f, top - 34f, ContentWidth - 12f, 12f),
                                            Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            float keyY = top - cardHeight - 4f;
            float half = (ContentWidth - 4f) * 0.5f;
            exceedLimitButton = WingUi.Button(parent, "OVER-LIMIT OFF",
                                              new Rect(Pad, keyY, half, KeyHeight),
                                              FontMicro, UiButtonStyle.Toggle, ToggleExceedLimit)
                                         .WithTooltip(OrderHint.OverLimit);
            requisitionButton = WingUi.Button(parent, "REQUISITION",
                                              new Rect(Pad + half + 4f, keyY, half, KeyHeight),
                                              FontMicro, UiButtonStyle.Primary, RequisitionSelected)
                                         .WithTooltip(OrderHint.Requisition);
            return y;
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
            if (!bought) return;
            RefreshSupplyPilot();
            RefreshShop();
            RefreshReserve();
        }

        /// <summary>Compact code for the first blocking reason, shown on a blocked tile's value line.</summary>
        private static string ShortBlockReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "BLOCKED";
            if (reason.Contains("disabled in config")) return "OFFLINE";
            if (reason.Contains("rank"))
            {
                int space = reason.LastIndexOf(' ');
                if (space >= 0) return "RANK " + reason.Substring(space + 1);
            }
            if (reason.Contains("No launch base")) return "NO BASE";
            if (reason.Contains("pad")) return "NO PAD";
            if (reason.Contains("runway")) return "NO RUNWAY";
            if (reason.Contains("Wing is full")) return "WING FULL";
            if (reason.Contains("stock")) return "NO STOCK";
            if (reason.StartsWith("Need ")) return "FUNDS";
            if (reason.Contains("formate")) return "CLASS";
            if (reason.Contains("restricted")) return "RESTRICTED";
            if (reason.Contains("Host") || reason.Contains("Not flying")) return "NO HOST";
            return AvTheme.Truncate(reason, 12).ToUpperInvariant();
        }

        /// <summary>Compact inbound-delivery summary for the dispatch card.</summary>
        private static string PendingLine()
        {
            int count = EconomyFacade.ShopDelivery.PendingCount;
            if (count <= 0) return null;

            string text = "INBOUND";
            for (int i = 0; i < count && i < 2; i++)
            {
                WingShopDelivery.PendingDelivery order = EconomyFacade.ShopDelivery.GetPending(i);
                if (order == null) continue;
                text += (i == 0 ? " " : "  ·  ") + order.StatusCode + " " + order.AirframeName;
            }
            if (count > 2) text += "  ·  +" + (count - 2);
            return text;
        }

        /// <summary>Refresh the pinned dispatch card from the live purchase quote, plus the over-limit
        /// policy chip.</summary>
        private static void RefreshSupplyStatus()
        {
            if (supplyDispatchAirframeLabel == null) return;

            bool shopEnabled = Plugin.Settings.ShopEnabled.Value;
            WingShop.PurchaseQuote quote = EconomyFacade.Shop.Quote(selectedOffer);
            string pending = PendingLine();

            RefreshExceedLimitButton(shopEnabled);

            if (selectedOffer == null)
            {
                supplyDispatchStateLabel.text = shopEnabled ? "NO AIRFRAME" : "OFFLINE";
                supplyDispatchStateLabel.color = shopEnabled ? Dim() : Warning();
                supplyDispatchAirframeLabel.text = "NO AIRFRAME SELECTED";
                supplyDispatchAirframeLabel.color = Dim();
                supplyDispatchDetailLabel.text = shopEnabled
                    ? "Steps 1-4 above build the dispatch."
                    : "Shop disabled in config  ·  assignment and reserve remain available.";
                supplyDispatchDetailLabel.color = Dim();
                string notice = supplySelectionNotice;
                supplyDispatchNoteLabel.text = notice != null
                    ? pending != null ? notice + "  ·  " + pending : notice
                    : pending ?? "";
                supplyDispatchNoteLabel.color = notice != null ? Warning() : Dim();
                if (supplyDispatchChip != null) supplyDispatchChip.color = RowColor();
                if (supplyDispatchRail != null) supplyDispatchRail.color = Dim();
                SetDispatchIcon(null);
                requisitionButton?.SetEnabled(false);
                requisitionButton?.WithTooltip("Select an airframe in step 2 first.");
                return;
            }

            int selectablePilots = PersonnelFacade.Roster.SelectablePilots().Count;
            bool full = !quote.CanBuy && quote.Reason != null && quote.Reason.Contains("Wing is full");
            int pendingCount = EconomyFacade.ShopDelivery.PendingCount;

            string state;
            Color stateColor;
            if (!shopEnabled) { state = "OFFLINE"; stateColor = Warning(); }
            else if (selectablePilots == 0) { state = "NO PILOT"; stateColor = Warning(); }
            else if (full) { state = "FULL"; stateColor = Warning(); }
            else if (!quote.CanBuy) { state = "BLOCKED"; stateColor = Warning(); }
            else if (pendingCount > 0) { state = "PENDING " + pendingCount; stateColor = WingUi.RailCyan; }
            else { state = "READY"; stateColor = Green(); }

            supplyDispatchStateLabel.text = state;
            supplyDispatchStateLabel.color = stateColor;
            if (supplyDispatchChip != null) supplyDispatchChip.color = stateColor.WithAlpha(0.30f);

            int owned = EconomyFacade.SupplyReserve.OwnedOf(selectedOffer);
            string costPart = owned > 0
                ? "FREE — OWNED " + owned
                : Grouped(quote.Price) + " CR" + (quote.OverLimit ? "  ·  OVER LIMIT" : "");
            supplyDispatchAirframeLabel.text = selectedOffer.unitName + "  ·  " + costPart;
            supplyDispatchAirframeLabel.color = quote.CanBuy ? Friendly() : Warning();

            WingLoadoutChoice fit = EconomyFacade.LoadoutBook.PlannedFor(selectedOffer);
            bool fromReserve = EconomyFacade.SupplyReserve.PeekLoadout(selectedOffer, out WingLoadoutChoice stored);
            if (fromReserve) fit = stored;
            string fitText = fromReserve
                ? "RESERVE FIT"
                : AvTheme.Truncate(EconomyFacade.LoadoutCatalog.Label(fit), 18).ToUpperInvariant();
            string fuel = fromReserve
                ? "AS RECOVERED"
                : "FUEL " + Mathf.RoundToInt(EconomyFacade.Shop.SpawnFuelLevel * 100f) + "%";
            string pilot = PersonnelFacade.Roster.Selected != null
                ? PersonnelFacade.Roster.Selected.Callsign
                : "NO PILOT";
            supplyDispatchDetailLabel.text = "PILOT " + pilot + "  ·  " + fitText + "  ·  " + fuel +
                                             "  ·  " + BaseLine();
            supplyDispatchDetailLabel.color = Dim();

            string blocker = null;
            if (!shopEnabled) blocker = "Shop disabled in config";
            else if (!quote.CanBuy) blocker = quote.Reason;
            if (pending != null && blocker != null)
            {
                supplyDispatchNoteLabel.text = pending + "  ·  " + blocker;
                supplyDispatchNoteLabel.color = Warning();
            }
            else if (blocker != null)
            {
                supplyDispatchNoteLabel.text = blocker;
                supplyDispatchNoteLabel.color = Warning();
            }
            else if (pending != null)
            {
                supplyDispatchNoteLabel.text = pending;
                supplyDispatchNoteLabel.color = WingUi.RailCyan;
            }
            else
            {
                supplyDispatchNoteLabel.text = "";
                supplyDispatchNoteLabel.color = Green();
            }

            if (supplyDispatchRail != null)
                supplyDispatchRail.color = !quote.CanBuy ? Warning()
                    : fromReserve ? WingUi.RailCyan
                    : WingUi.RailEmerald;
            SetDispatchIcon(selectedOffer);

            requisitionButton?.SetEnabled(quote.CanBuy);
            requisitionButton?.WithTooltip(quote.CanBuy
                ? OrderHint.Requisition
                : "Cannot requisition — " + (blocker ?? "requirements not met"));
        }

        /// <summary>Name of the base the next delivery would use, or the routing mode.</summary>
        private static string BaseLine()
        {
            if (EconomyFacade.LaunchFields.Mode == HangarLaunchMode.Any) return "ANY FIELD";
            foreach (Airbase field in EconomyFacade.LaunchFields.Listing)
            {
                if (EconomyFacade.LaunchFields.IsAllowed(field))
                    return EconomyFacade.LaunchFields.DisplayName(field);
            }
            return "NO BASE";
        }

        private static void SetDispatchIcon(AircraftDefinition definition)
        {
            if (supplyDispatchIcon == null) return;
            Sprite sprite = definition != null ? IconFactory.Aircraft(definition) : IconFactory.Get("airframe");
            supplyDispatchIcon.sprite = sprite;
            supplyDispatchIcon.enabled = sprite != null;
            supplyDispatchIcon.color = definition != null && selectedOffer != null ? Friendly() : Dim();
        }

        /// <summary>Over-limit policy chip: explains itself in every state.</summary>
        private static void RefreshExceedLimitButton(bool shopEnabled)
        {
            if (exceedLimitButton == null) return;

            float mult = EconomyFacade.Shop.ExceedLimitMultiplier;
            if (!shopEnabled)
            {
                exceedLimitButton.SetText("OVER-LIMIT OFFLINE");
                exceedLimitButton.SetLatched(false);
                exceedLimitButton.SetEnabled(false);
            }
            else if (!EconomyFacade.Shop.MeetsExceedLimitRank)
            {
                exceedLimitButton.SetText("RANK " + EconomyFacade.Shop.ExceedLimitRank + " REQUIRED");
                exceedLimitButton.SetLatched(false);
                exceedLimitButton.SetEnabled(true);
            }
            else
            {
                bool allow = EconomyFacade.Shop.ExceedLimit;
                exceedLimitButton.SetText("OVER-LIMIT ×" + mult.ToString("0.##") + (allow ? " ON" : " OFF"));
                exceedLimitButton.SetLatched(allow);
                exceedLimitButton.SetEnabled(true);
            }
        }

        // ------------------------------------------------------------------ tiles

        /// <summary>Purchasable-airframe tile: silhouette, code, name, price/stock, corner badge.</summary>
        private sealed class ShopAirframeTile
        {
            private readonly GameObject go;
            private readonly Image fill;
            private readonly Image rail;
            private readonly Image icon;
            private readonly TMP_Text code;
            private readonly TMP_Text name;
            private readonly TMP_Text priceStock;
            private readonly TMP_Text badge;
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

                Outline(rt, new Rect(0f, 0f, rect.width, rect.height), FrameColor());
                rail = Rule(rt, new Rect(0f, 0f, 3f, rect.height), Color.clear);

                float iconSize = 20f;
                icon = AddSprite(rt, "ShopAirframeIcon", IconFactory.Get("airframe"),
                                 new Rect(6f, -(rect.height - iconSize) * 0.5f, iconSize, iconSize), Color.white);

                const float textLeft = 32f;
                float textWidth = rect.width - textLeft - 4f;
                code = Label(rt, "", new Rect(textLeft, -2f, textWidth, 14f), Friendly(),
                             FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                code.overflowMode = TextOverflowModes.Ellipsis;
                code.enableWordWrapping = false;
                name = Label(rt, "", new Rect(textLeft, -16f, textWidth, 12f), Dim(),
                             FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.enableWordWrapping = false;

                // Hairline separating the identity block from the price/stock readout at the foot.
                Rule(rt, new Rect(textLeft, -(rect.height - Space6), textWidth, 1f), WingUi.BorderSubtle);

                badge = Label(rt, "", new Rect(textLeft, -(rect.height - 13f), 52f, 12f), Dim(),
                              FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
                badge.overflowMode = TextOverflowModes.Ellipsis;
                priceStock = Label(rt, "", new Rect(textLeft + 52f, -(rect.height - 13f), textWidth - 52f, 12f),
                                   Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
                priceStock.overflowMode = TextOverflowModes.Ellipsis;

                hit = HitButton(rt, new Rect(0f, 0f, rect.width, rect.height), () =>
                {
                    if (bound == null) return;
                    selectedOffer = bound;
                    supplySelectionNotice = null;
                    RefreshShop();
                    RefreshLaunchFrom();
                });

                go.SetActive(false);
            }

            public void Bind(WingShop.Offer offer)
            {
                bound = offer.Definition;
                if (!go.activeSelf) go.SetActive(true);
                hit.SetEnabled(true);

                bool shopEnabled = Plugin.Settings.ShopEnabled.Value;
                int owned = EconomyFacade.SupplyReserve.OwnedOf(offer.Definition);
                int held = Mathf.Max(0, EconomyFacade.SupplyReserve.CountOf(offer.Definition) - owned);
                bool selected = selectedOffer == offer.Definition;
                WingShop.PurchaseQuote quote = EconomyFacade.Shop.Quote(offer.Definition);
                bool canBuy = quote.CanBuy;

                Sprite sprite = IconFactory.Aircraft(offer.Definition);
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.color = selected ? Color.white : canBuy ? Friendly() : Dim();

                code.text = !string.IsNullOrEmpty(offer.Definition.code) ? offer.Definition.code : offer.Name;
                code.color = selected ? WingUi.TextPrimary : canBuy ? Friendly() : Dim();
                name.text = offer.Name;
                name.color = selected ? Friendly() : Dim();

                badge.text = owned > 0 ? "OWNED " + owned : held > 0 ? "HELD " + held : quote.OverLimit ? "OVER" : "";
                badge.color = owned > 0 ? Green() : held > 0 ? WingUi.RailCyan : Warning();

                if (canBuy)
                {
                    priceStock.text = owned > 0
                        ? "FREE — OWNED " + owned
                        : Grouped(quote.Price) + " CR  ·  " + offer.Stock + "x";
                    priceStock.color = owned > 0 ? Green() : Dim();
                }
                else
                {
                    priceStock.text = ShortBlockReason(quote.Reason);
                    priceStock.color = Warning();
                }

                fill.color = selected ? WingUi.CardFillSelected : WingUi.CardFill;
                rail.color = selected ? Green() : !canBuy ? Warning() : owned + held > 0 ? WingUi.RailCyan : Color.clear;

                string costNotice = owned > 0 ? "FREE (" + owned + " owned in reserve)" : "Cost: " + Grouped(quote.Price);
                string launchNotice = canBuy
                    ? " | " + EconomyFacade.HangarStock.AirframeLaunchText(offer.Definition, allowedOnly: true)
                    : " | [!] " + (quote.Reason ?? "requirements not met");
                hit.WithTooltip(offer.Name + " — " + costNotice + " | Stock: " + offer.Stock + launchNotice +
                                (shopEnabled ? "" : " | Shop disabled in config"));
                hit.SetRowHighlight(fill, selected ? WingUi.CardFillSelected : WingUi.CardFill,
                    selected ? WingUi.CardFillSelectedHover : WingUi.CardFillHover);
            }

            /// <summary>Clear an unfilled bay. Deactivating it lets the step-2 empty card read as one
            /// full inert card instead of empty tiles stacked over it.</summary>
            public void Hide()
            {
                bound = null;
                if (go.activeSelf) go.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ launch rows

        /// <summary>Friendly launch field with an enable/disable control and readiness badge.</summary>
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
                    new Rect(0f, -2f, LaunchCheckWidth, rect.height - 4f),
                    FontMicro, UiButtonStyle.Toggle, Toggle)
                    .WithTooltip("Allow launches from this field");

                const float statusWidth = 80f;
                float nameWidth = rect.width - LaunchCheckWidth - Space1 - statusWidth - Space1;

                name = Label(rt, "",
                    new Rect(LaunchCheckWidth + Space1, 0f, nameWidth, rect.height),
                    Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                name.overflowMode = TextOverflowModes.Ellipsis;

                status = Label(rt, "",
                    new Rect(rect.width - statusWidth, 0f, statusWidth, rect.height),
                    Dim(), FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

                hit = HitButton(rt, new Rect(LaunchCheckWidth + Space1, 0f,
                                             rect.width - LaunchCheckWidth - Space1, rect.height),
                                Toggle);

                go.SetActive(false);
            }

            public void Bind(Airbase airbase, bool interactive)
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
                check.SetText(allowed ? "ON" : "OFF");
                check.SetEnabled(interactive);
                hit.SetEnabled(interactive);

                name.text = EconomyFacade.LaunchFields.DisplayName(airbase);
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
                if (!interactive) tooltip += " | Shop disabled in config";
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
