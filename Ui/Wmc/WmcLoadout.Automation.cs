using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WingCommand
{
    // LOADOUT for Dev/Automation.Wmc (F14: picks without an id of their own get arguments) and its report.
    internal sealed partial class WmcLoadout
    {
        private bool popupClear;

        /// <summary>Selects an editable airframe by jsonKey, unit name or code and shows its page.</summary>
        public bool PickAirframe(string name)
        {
            for (int i = 0; i < airframes.Count; i++)
            {
                AircraftDefinition d = airframes[i];
                if (!(Same(d.jsonKey, name) || Same(d.unitName, name) || Same(d.code, name))) continue;
                airframe = d;
                tilePage = Pages.Of(i, tiles.PerPage);
                hpPage = 0;
                Resolve();
                return true;
            }
            return false;
        }

        /// <summary>Edits the airframe's template of that name, or starts one with "new".</summary>
        public bool UseTemplate(string name)
        {
            if (airframe == null) return false;
            if (Same(name, "new"))
            {
                int before = WingLoadoutTemplates.CountFor(airframe);
                New();
                return WingLoadoutTemplates.CountFor(airframe) > before;
            }
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(airframe))
                if (Same(t.Name, name))
                {
                    editing[airframe.jsonKey] = t.Id;
                    Resolve();
                    return true;
                }
            return false;
        }

        public bool RenameTo(string name) => current != null && (WingLoadoutTemplates.Rename(current, name) || Same(current.Name, TemplateNames.Clean(name)));

        /// <summary>Fits station <paramref name="station"/> (0-based) with a store by key or label, "#n" (the n-th pickable one as the popup
        /// lists them), or "empty"; false when the station or store is not there or the station is blocked.</summary>
        public bool Mount(int station, string store)
        {
            if (current == null || layout == null || station < 0 || station >= layout.Stations) return false;
            if (Same(store, "empty"))
            {
                layout.Clear(keys, station);
                WingLoadoutTemplates.SetMounts(current, keys);
                return true;
            }
            int set = layout.First(station), pylons = layout.PylonsOf(station);
            WingLoadoutCatalog.StoresFor(airframe, set, stores);
            int want = store != null && store.StartsWith("#", StringComparison.Ordinal)
                && int.TryParse(store.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;
            string key = null;
            int seen = 0;
            for (int pass = 0; pass < 2 && key == null; pass++)
                foreach (WingLoadoutCatalog.StoreOption o in stores)
                {
                    StoreVerdict v = StoreRules.Check(WingLoadoutCatalog.FactsOf(o, pylons, hq), mission);
                    if (!StoreRules.Pickable(v) || (v == StoreVerdict.Ok ? 0 : 1) != pass) continue;
                    seen++;
                    if (want > 0 ? seen == want : Same(o.Key, store) || Same(o.Label, store))
                    {
                        key = o.Key;
                        break;
                    }
                }
            if (key == null || layout.Pick(keys, station, key) < 0) return false;
            WingLoadoutTemplates.SetMounts(current, keys);
            return true;
        }

        /// <summary>"next", "prev", an index into the faction's list or a label.</summary>
        public bool Livery(string arg)
        {
            if (airframe == null) return false;
            RefreshLivery();
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            int to = Same(arg, "next") ? LiveryChoice.Step(i, liveries.Count, 1) : Same(arg, "prev") ? LiveryChoice.Step(i, liveries.Count, -1) : -1;
            if (to < 0 && int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) to = index;
            for (int k = 0; to < 0 && k < liveries.Count; k++)
                if (Same(liveries[k].Label, arg)) to = k;
            if (to < 0 || to >= liveries.Count) return false;
            WingLoadoutTemplates.SetLivery(airframe, liveryTokens[to]);
            return true;
        }

        public void FocusName() => nameField.Focus();

        public void SubmitName(string text) => nameField.Submit(text);

        /// <summary>Test hygiene: deletes the airframe's templates (and their SUPPLY fits) and its saved livery.</summary>
        public void ResetAirframe()
        {
            if (airframe == null) return;
            var ids = new List<string>();
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(airframe)) ids.Add(t.Id);
            foreach (string id in ids)
            {
                WingLoadoutTemplates.Delete(WingLoadoutTemplates.ById(id));
                WingRequisition.DropFit(id);
            }
            WingLoadoutTemplates.SetLivery(airframe, null);
            editing.Remove(airframe.jsonKey);
            Resolve();
        }

        private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>What the page shows, as numbers and words a scenario can check.</summary>
        public void Report(Dictionary<string, object> into)
        {
            Resolve();
            int liveryIndex = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            into["lo_tiles"] = airframes.Count;
            into["lo_selected"] = airframe != null ? 1 : 0;
            into["lo_airframe"] = airframe != null ? airframe.unitName : "";
            into["lo_templates"] = airframe != null ? WingLoadoutTemplates.CountFor(airframe) : 0;
            into["lo_editing"] = current != null ? 1 : 0;
            into["lo_template"] = current != null ? current.Name : "";
            into["lo_rows"] = layout != null ? layout.Stations : 0;
            into["lo_sets"] = layout != null ? layout.Sets : 0;
            into["lo_pylons"] = layout != null ? layout.Pylons : 0;
            into["lo_fitted"] = current != null ? summary.Fitted : 0;
            into["lo_blocked"] = current != null ? summary.Blocked : 0;
            into["lo_refused"] = current != null ? summary.Refused : 0;
            into["lo_unknown"] = current != null ? summary.Unknown : 0;
            into["lo_mass"] = current != null ? summary.Mass : 0f;
            into["lo_role"] = current != null ? LoadoutWords.Role(summary) : "";
            into["lo_chip"] = chipText.text;
            into["lo_liveries"] = liveries.Count;
            into["lo_livery_index"] = liveryIndex;
            into["lo_livery"] = liveries.Count > 0 ? liveries[liveryIndex].Label : "";
            into["lo_delete_armed"] = current != null && deleteGate.IsArmed(current.Id, Time.unscaledTime) ? 1 : 0;
            into["lo_name_focused"] = nameField.Focused ? 1 : 0;
            into["lo_typing"] = WmcNameField.Typing ? 1 : 0;
            into["lo_keyboard_held"] = Rewired.ReInput.isReady && Rewired.ReInput.controllers?.Keyboard != null
                && !Rewired.ReInput.controllers.Keyboard.enabled ? 1 : 0;
            into["lo_supply_fit"] = current != null && WingRequisition.FitOf(airframe) == current.Id ? 1 : 0;
            into["lo_popup_clear"] = popupClear ? 1 : 0;
        }

        /// <summary>The popup just opened sits clear of its row and inside the body (the scenario's check).</summary>
        private void NotePopup(Rect area, RectTransform row)
        {
            Rect r = WmcKit.RectIn(page, row);
            bool clearOfRow = area.y <= r.y - r.height + 0.5f || area.y - area.height >= r.y - 0.5f;
            bool inBody = area.y <= body.y + 0.5f && area.y - area.height >= body.y - body.height - 0.5f;
            popupClear = clearOfRow && inBody;
        }
    }
}
