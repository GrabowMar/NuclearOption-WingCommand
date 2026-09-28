using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    // SUPPLY for Dev/Automation.Wmc (F14: picks without an id of their own get arguments) and its report.
    internal sealed partial class WmcSupply
    {
        /// <summary>Selects a listed airframe by jsonKey, unit name or code and shows its page; false when none is listed.</summary>
        public bool Pick(string airframe)
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            for (int i = 0; i < cat.Count; i++)
            {
                AircraftDefinition d = cat[i];
                if (d == null || !(Same(d.jsonKey, airframe) || Same(d.unitName, airframe) || Same(d.code, airframe))) continue;
                WingRequisition.Selected = d;
                tilePage = Pages.Of(i, tiles.PerPage);
                returnGate = new ConfirmGate();
                return true;
            }
            return false;
        }

        /// <summary>The selected airframe's fit: "auto", "yours", or a LOADOUT template's name.</summary>
        public bool Fit(string name)
        {
            AircraftDefinition d = WingRequisition.Selected;
            if (d == null) return false;
            if (Same(name, "auto")) WingRequisition.SetFit(d, null);
            else if (Same(name, "yours")) WingRequisition.SetFit(d, CallSpec.YourLoadout);
            else
            {
                string id = null;
                foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(d))
                    if (Same(t.Name, name)) id = t.Id;
                if (id == null) return false;
                WingRequisition.SetFit(d, id);
            }
            return true;
        }

        /// <summary>Leaves only the named field ON (its unique, display or object name).</summary>
        public bool OnlyBase(string name)
        {
            Airbase hit = null;
            foreach (Airbase f in WingRequisition.Fields)
                if (f != null && (Same(WingRequisition.KeyOf(f), name) || Same(WingRequisition.NameOf(f), name) || Same(f.name, name))) hit = f;
            if (hit == null) return false;
            foreach (Airbase f in WingRequisition.Fields)
                if (f != null) WingRequisition.SetOn(f, ReferenceEquals(f, hit));
            return true;
        }

        private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>What the page shows, as numbers and words a scenario can check.</summary>
        public void Report(Dictionary<string, object> into)
        {
            into["sup_tiles"] = WingRequisition.Catalogue.Count;
            into["sup_pages"] = Pages.Count(WingRequisition.Catalogue.Count, tiles.PerPage);
            into["sup_selected"] = selected != null ? 1 : 0;
            into["sup_blocked"] = selected != null && !quote.Allowed ? 1 : 0;
            into["sup_ready"] = !client && selected != null && quote.Allowed ? 1 : 0;
            into["sup_overlimit"] = selected != null && quote.OverLimit ? 1 : 0;
            into["sup_price"] = selected != null ? quote.Price : 0f;
            into["sup_inbound"] = inboundCount;
            into["sup_adopt"] = adoptVisible ? recruits.Count : 0;
            into["sup_hangar"] = WingSupplyReserve.Count;
            into["sup_return_armed"] = selected != null && returnGate.IsArmed(selected.jsonKey, Time.unscaledTime) ? 1 : 0;
            into["sup_free"] = free.Count;
            into["sup_fuel"] = WingRequisition.FuelPercent;
            into["sup_bases_on"] = baseChipKey >= 0 ? baseChipKey / 1000 : 0;
            into["sup_field"] = field != null ? WingRequisition.NameOf(field) : "";
            into["sup_state"] = stateText.text;
            into["sup_title"] = titleText.text;
            into["sup_line"] = lineText.text;
            into["sup_blocker"] = blockerText.text;
        }
    }
}
