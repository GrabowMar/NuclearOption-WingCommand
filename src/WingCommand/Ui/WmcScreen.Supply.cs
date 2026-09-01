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
            if (!Plugin.Settings.ShopEnabled.Value) return y;

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

            // How full the tanks are when it launches. A checkbox rather than a latch
            // styled like OVER LIMIT: this one starts switched on, and a lit toggle that is
            // lit by default reads as a warning rather than as a setting.
            fullFuelButton = WingUi.Button(parent, "",
                                           new Rect(Pad, y, PanelWidth - Pad * 2f, RowHeight),
                                           FontSmall, UiButtonStyle.Quiet, ToggleFullFuel)
                              .WithTooltip(OrderHint.FullFuel);
            y -= RowHeight + Space1;

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
                ? selectedOffer.unitName + " requisitioned for " + Mathf.RoundToInt(paid) +
                  " - departing friendly base"
                : why);
        }

        /// <summary>Rebind the shop rows and the allocation header.</summary>
        private static void RefreshShop()
        {
            if (!Plugin.Settings.ShopEnabled.Value || shopRows.Count == 0) return;

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

            if (fullFuelButton != null)
            {
                // The box states the choice; the trailing clause states what the other
                // state would cost, so the consequence is readable without toggling it.
                fullFuelButton.SetText(WingShop.FullFuel
                    ? "[X]  SPAWN WITH FULL FUEL"
                    : "[ ]  SPAWN WITH FULL FUEL  -  launching at " +
                      Mathf.RoundToInt(WingTuning.PartialFuelLevel * 100f) + "%");
                fullFuelButton.SetLatched(WingShop.FullFuel);
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
                UiTheme.Truncate(WingLoadoutCatalog.Label(planned), 34)
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

    }
}
