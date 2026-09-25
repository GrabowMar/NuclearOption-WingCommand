using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SUPPLY step 2 AIRFRAME (a page of tiles and the HANGAR store) and step 3 FIT & FUEL.
    internal sealed partial class WmcSupply
    {
        private const float HangarMeter = 120f;
        private WmcTiles tiles;
        private WmcPager tilePager;
        private int tilePage, airChipShown = -1, hangarKey = int.MinValue;
        private TMP_Text airState, hangarLabel;
        private Image airRail, hangarFill;
        private AvButton store, giveBack;
        private ConfirmGate returnGate = new ConfirmGate();

        private void BuildAirframe(RectTransform r, float y)
        {
            airState = WmcKit.StepHeader(r, new Rect(0f, y, width, BezelLayout.StepHead), 2, SupplyWords.AirframeTitle, out airRail);
            float ty = y - BezelLayout.StepHead - BezelLayout.HeadGap;
            tiles = WmcTiles.Build(r, new Rect(0f, ty, width, 0f), BezelLayout.TileCols, BezelLayout.TileRows, BezelLayout.TileH, "sup.tile",
                ids, PickTile);
            // The footer: the pager (always shown, 0.9 critique 13), the HANGAR count and meter, STORE and a two-press RETURN.
            float fy = ty - tiles.Height - BezelLayout.HeadGap;
            tilePager = WmcPager.Build(r, new Rect(0f, fy, 100f, BezelLayout.TileFooter), "sup.tiles.", ids, TurnTiles);
            hangarLabel = WmcKit.Text(r, new Rect(108f, fy - 1f, HangarMeter, 18f), "row-name");
            hangarFill = WmcUi.Bar(r, new Rect(108f, fy - 21f, HangarMeter, 3f));
            store = AvStyled.Button(r, new Rect(282f, fy, 80f, BezelLayout.TileFooter), "STORE", "btn", Store);
            ids["sup.store"] = store;
            giveBack = AvStyled.Button(r, new Rect(366f, fy, width - 366f, BezelLayout.TileFooter), "RETURN", "btn", Return);
            ids["sup.return"] = giveBack;
        }

        /// <summary>This page of listed airframes: each tile quoted with its own reason only (a wing-wide block is said once, on
        /// DISPATCH), rebound only when its quote, stock or selection changes. A selection no longer listed stays selected and
        /// DISPATCH says why (0.9 critique 15).</summary>
        private void RefreshAirframe()
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            int per = tiles.PerPage;
            tilePage = Pages.Clamp(tilePage, cat.Count, per);
            tilePager.Set(tilePage, Pages.Count(cat.Count, per));
            if (cat.Count != airChipShown)
            {
                airChipShown = cat.Count;
                WmcKit.SetStep(airState, airRail, SupplyWords.AirframeChip(cat.Count), cat.Count > 0 ? "live" : "warn");
            }
            tiles.ShowEmpty(cat.Count > 0 ? null : caller == null ? SupplyWords.NotFlyingTiles : SupplyWords.NoneOffered);
            int first = Pages.First(tilePage, per);
            for (int s = 0; s < per; s++)
            {
                int k = first + s;
                if (k >= cat.Count)
                {
                    tiles.Hide(s);
                    continue;
                }
                AircraftDefinition d = cat[k];
                ShopAirframe a = WingRequisition.For(d, hq);
                ShopQuote q = ShopRules.Quote(wing, a);
                bool sel = ReferenceEquals(d, selected);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (int)q.Tile * 7 + (int)(q.Price * 10f) * 131 + (a.FactionStock + a.Held) * 1009 + a.Held * 17
                        + (sel ? 3 : 0) + (wing.Sandbox ? 5 : 0);
                }
                if (!tiles.NeedsBind(s, key)) continue;
                bool blocked = q.Tile != TileBlock.None;
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code),
                    ShopRules.TileFoot(q, a, wing.Sandbox), blocked ? "warn" : "",
                    sel ? "live" : blocked ? "warn" : a.Held > 0 && !wing.Sandbox ? "info" : "inert", sel, true, TileTip(d, q, a));
            }
        }

        private string TileTip(AircraftDefinition d, in ShopQuote q, in ShopAirframe a) =>
            d.unitName + " · " + ShopRules.TileFoot(q, a, wing.Sandbox)
            + (a.Held > 0 && !wing.Sandbox ? " · " + a.Held.ToString(CultureInfo.InvariantCulture) + " IN THE HANGAR" : "");

        private void PickTile(int slot)
        {
            int k = Pages.First(tilePage, tiles.PerPage) + slot;
            if (k < 0 || k >= WingRequisition.Catalogue.Count) return;
            WingRequisition.Selected = WingRequisition.Catalogue[k];
            // A RETURN asked about another airframe asks again (review focus 3).
            returnGate = new ConfirmGate();
            WmcPanel.Instance?.Refresh();
        }

        private void TurnTiles(int dir)
        {
            tilePage = Pages.Clamp(tilePage + dir, WingRequisition.Catalogue.Count, tiles.PerPage);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the HANGAR store

        /// <summary>The HANGAR's count and meter; STORE and RETURN act on the selected airframe, each disabled with its reason.</summary>
        private void RefreshHangar()
        {
            int count = WingSupplyReserve.Count, cap = WingSupplyReserve.Capacity;
            bool host = WingSupplyReserve.IsHost && !client, faction = WingSupplyReserve.HasFaction, offline = client || !faction;
            int stock = selected != null && faction ? WingSupplyReserve.FactionStockOf(selected) : 0;
            int stored = selected != null ? WingSupplyReserve.CountOf(selected) : 0;
            bool asking = selected != null && returnGate.IsArmed(selected.jsonKey, Time.unscaledTime);
            int key;
            unchecked
            {
                key = count * 7 + cap * 31 + (offline ? 3 : 0) + stock * 101 + stored * 1009 + (asking ? 5 : 0) + (host ? 11 : 0)
                    + (selected != null ? selected.GetHashCode() : 0);
            }
            if (key == hangarKey) return;
            hangarKey = key;
            WmcKit.Set(hangarLabel, HangarWords.Label(count, cap, offline));
            WmcUi.SetBar(hangarFill, HangarMeter, HangarWords.Level(count, cap), HangarWords.Full(count, cap) ? AvTheme.Warning : AvTheme.Friendly);
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : ShopRules.StoreBlock(host, faction, count, cap, stock);
            store.SetEnabled(why == null);
            store.WithTooltip(why ?? "Store one " + selected.unitName + " in the HANGAR: the faction's AI cannot take it, and your next " +
                "requisition of it launches from there.");
            why = client ? ClientWhy : selected == null ? "Pick an airframe first." : ShopRules.ReturnBlock(host, faction, stored);
            giveBack.SetEnabled(why == null);
            giveBack.WithTooltip(why ?? "Return one " + selected.unitName + " to the faction's stock (press twice).");
            giveBack.SetText(HangarWords.ReturnLabel(asking));
            giveBack.SetLatched(asking);
        }

        private void Store()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            WingToast.Show(WingSupplyReserve.Hold(d, out string why)
                ? HangarWords.Stored(d.unitName, WingSupplyReserve.Count, WingSupplyReserve.Capacity) : why);
            WmcPanel.Instance?.Refresh();
        }

        private void Return()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            if (!returnGate.Press(d.jsonKey, Time.unscaledTime))
                WingToast.Show(HangarWords.Ask(d.unitName));
            else
                WingToast.Show(WingSupplyReserve.Release(d, out _, out string why)
                    ? HangarWords.Returned(d.unitName, WingSupplyReserve.Count, WingSupplyReserve.Capacity) : why);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- step 3 FIT & FUEL

        private AvButton fitButton, fuelButton;
        private TMP_Text fitState, fitDetail;
        private Image fitRail;
        private AvKit.Popup fitPopup;
        private readonly List<AvKit.PopupEntry> fitEntries = new List<AvKit.PopupEntry>();
        private readonly List<string> fitIds = new List<string>();
        private AircraftDefinition fitFor;
        private string fitShown;
        private int fuelShown = -1;
        private bool fitClient, fitSet;

        private void BuildFit(RectTransform r, float y)
        {
            fitState = WmcKit.StepHeader(r, new Rect(0f, y, width, BezelLayout.StepHead), 3, SupplyWords.FitTitle, out fitRail);
            float by = y - BezelLayout.StepHead - BezelLayout.HeadGap;
            fitButton = AvStyled.Button(r, new Rect(0f, by, 300f, BezelLayout.FitRow), SupplyWords.FitButton("AUTO"), "btn", OpenFit);
            ids["sup.fit"] = fitButton;
            fuelButton = AvStyled.Button(r, new Rect(306f, by, width - 306f, BezelLayout.FitRow), SupplyWords.Fuel(100), "btn", CycleFuel);
            ids["sup.fuel"] = fuelButton;
            fitDetail = AvStyled.Label(r, new Rect(0f, by - BezelLayout.FitRow - BezelLayout.HeadGap, width, BezelLayout.FitDetail), "", "row-sub");
        }

        /// <summary>The selected airframe's fit (AUTO, the default: the game's own pick) and the fuel at launch, rebuilt only when
        /// either changes.</summary>
        private void RefreshFit()
        {
            string fit = WingRequisition.FitOf(selected);
            int fuel = WingRequisition.FuelPercent;
            if (fitSet && ReferenceEquals(selected, fitFor) && ReferenceEquals(fit, fitShown) && fuel == fuelShown && client == fitClient) return;
            fitSet = true;
            fitFor = selected;
            fitShown = fit;
            fuelShown = fuel;
            fitClient = client;
            fitButton.SetText(SupplyWords.FitButton(SupplyWords.Fit(fit,
                fit != null && fit != CallSpec.YourLoadout ? WingLoadoutTemplates.NameOf(fit) : null)));
            fuelButton.SetText(SupplyWords.Fuel(fuel));
            WmcKit.Set(fitDetail, SupplyWords.FitDetail(fit, selected != null && WingLoadoutTemplates.CountFor(selected) > 0));
            WmcKit.SetStep(fitState, fitRail, SupplyWords.Fuel(fuel), fuel < 50 ? "warn" : "live");
            string why = client ? ClientWhy : selected == null ? "Pick an airframe first." : null;
            fitButton.SetEnabled(why == null);
            fitButton.WithTooltip(why ?? "What it carries: AUTO (the game's pick for the mission), YOUR LOADOUT, or a template saved on LOADOUT.");
            fuelButton.SetEnabled(!client);
            fuelButton.WithTooltip(client ? ClientWhy
                : "Fuel at launch: 25, 50, 75 or 100%. Less is lighter but reaches bingo sooner, and a refit refuels to the same level.");
        }

        private void OpenFit()
        {
            if (client || selected == null) return;
            AircraftDefinition d = selected;
            string current = WingRequisition.FitOf(d);
            fitEntries.Clear();
            fitIds.Clear();
            AddFit("AUTO", "the game's pick for the mission", null, current);
            AddFit("YOUR LOADOUT", "as you would fly it", CallSpec.YourLoadout, current);
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(d)) AddFit(SupplyWords.Fit(t.Id, t.Name), "saved on LOADOUT", t.Id, current);
            fitPopup.Show(WmcKit.RectIn(page, (RectTransform)fitButton.transform), fitEntries, PickFit);
            // The popup builds its rows on the first show: no "…" there either.
            WmcKit.FitAll(page);
        }

        private void AddFit(string text, string detail, string id, string current)
        {
            fitEntries.Add(new AvKit.PopupEntry(text, detail, id == current));
            fitIds.Add(id);
        }

        private void PickFit(int i)
        {
            if (client || selected == null || i < 0 || i >= fitIds.Count) return;
            WingRequisition.SetFit(selected, fitIds[i]);
            WmcPanel.Instance?.Refresh();
        }

        private void CycleFuel()
        {
            if (client) return;
            WingRequisition.FuelPercent = ShopRules.NextFuel(WingRequisition.FuelPercent);
            WmcPanel.Instance?.Refresh();
        }
    }
}
