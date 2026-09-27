using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    // LOADOUT's airframe tiles and template bar: pick an airframe, pick a template, NEW · COPY · gap · DELETE (two-press).
    internal sealed partial class WmcLoadout
    {
        private WmcTiles tiles;
        private WmcPager tilePager;
        private int tilePage;
        private AvButton picker, create, copy, delete;
        private AvKit.Popup popup;
        private readonly List<AvKit.PopupEntry> entries = new List<AvKit.PopupEntry>();
        private readonly List<string> entryIds = new List<string>();
        private ConfirmGate deleteGate = new ConfirmGate();

        private void BuildTiles(RectTransform r, float y)
        {
            AvStyled.Label(r, new Rect(0f, y, width - 104f, BezelLayout.SectionHead), "AIRFRAME", "section-title");
            tilePager = WmcPager.Build(r, new Rect(width - 100f, y, 100f, BezelLayout.SectionHead), "lo.tiles.", ids, TurnTiles);
            tiles = WmcTiles.Build(r, new Rect(0f, y - BezelLayout.SectionHead - BezelLayout.HeadGap, width, 0f), BezelLayout.TileCols,
                BezelLayout.TileRows, BezelLayout.LoadoutTileH, "lo.tile", ids, PickTile);
        }

        private void RefreshTiles()
        {
            int per = tiles.PerPage;
            tilePage = Pages.Clamp(tilePage, airframes.Count, per);
            tilePager.Set(tilePage, Pages.Count(airframes.Count, per));
            tiles.ShowEmpty(airframes.Count > 0 ? null : "NO AIRFRAME · none with readable hardpoints is in this game");
            int first = Pages.First(tilePage, per);
            for (int s = 0; s < per; s++)
            {
                int k = first + s;
                if (k >= airframes.Count)
                {
                    tiles.Hide(s);
                    continue;
                }
                AircraftDefinition d = airframes[k];
                bool sel = ReferenceEquals(d, airframe);
                int count = WingLoadoutTemplates.CountFor(d);
                int key;
                unchecked
                {
                    key = d.GetHashCode() * 31 + (sel ? 1 : 0) + count * 7;
                }
                if (!tiles.NeedsBind(s, key)) continue;
                string n = count.ToString(CultureInfo.InvariantCulture);
                tiles.Bind(s, IconFactory.Aircraft(d), SupplyWords.Code(d.code, d.unitName), SupplyWords.Name(d.unitName, d.code), null, "",
                    sel ? "live" : count > 0 ? "info" : "inert", sel, true,
                    d.unitName + " · " + (count == 1 ? "1 template" : n + " templates"));
            }
        }

        private void PickTile(int slot)
        {
            int k = Pages.First(tilePage, tiles.PerPage) + slot;
            if (k < 0 || k >= airframes.Count) return;
            airframe = airframes[k];
            hpPage = 0;
            deleteGate = new ConfirmGate();
            WmcPanel.Instance?.Refresh();
        }

        private void TurnTiles(int dir)
        {
            tilePage = Pages.Clamp(tilePage + dir, airframes.Count, tiles.PerPage);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the template bar

        private void BuildTemplateBar(RectTransform r, float y)
        {
            float h = BezelLayout.TemplateBar, x = 0f;
            picker = AvStyled.Button(r, new Rect(x, y, BezelLayout.TemplatePick, h), LoadoutWords.Picker(null), "btn", OpenPicker);
            ids["lo.template"] = picker;
            x += BezelLayout.TemplatePick + 4f;
            create = AvStyled.Button(r, new Rect(x, y, BezelLayout.TemplateBtn, h), "NEW", "btn", New);
            ids["lo.new"] = create;
            x += BezelLayout.TemplateBtn + 4f;
            copy = AvStyled.Button(r, new Rect(x, y, BezelLayout.TemplateBtn, h), "COPY", "btn", Copy);
            ids["lo.copy"] = copy;
            x += BezelLayout.TemplateBtn + BezelLayout.DeleteGap;
            delete = AvStyled.Button(r, new Rect(x, y, BezelLayout.TemplateBtn, h), "DELETE", "btn", Delete, AvButtonStyle.Danger);
            ids["lo.delete"] = delete;
        }

        private void RefreshTemplateBar(bool asking)
        {
            int count = airframe != null ? WingLoadoutTemplates.CountFor(airframe) : 0;
            picker.SetText(LoadoutWords.Picker(current?.Name));
            picker.SetEnabled(count > 0);
            picker.WithTooltip(count > 0 ? "Pick the template to edit." : airframe == null ? "Pick an airframe first." : "NEW starts a template.");
            string why = airframe == null ? "Pick an airframe first." : LoadoutWords.NewWhy(layout != null, count);
            create.SetEnabled(why == null);
            create.WithTooltip(why ?? "Start a template from this airframe's standard stores (its gun, radar and hook included).");
            why = LoadoutWords.CopyWhy(current != null, count);
            copy.SetEnabled(why == null);
            copy.WithTooltip(why ?? "Copy this template under the next free name.");
            why = LoadoutWords.DeleteWhy(current != null);
            delete.SetEnabled(why == null);
            delete.WithTooltip(why ?? "Delete this template (press twice); aircraft in the air keep their fit.");
            delete.SetText(LoadoutWords.DeleteLabel(asking));
            delete.SetLatched(asking);
        }

        private void OpenPicker()
        {
            if (airframe == null) return;
            entries.Clear();
            entryIds.Clear();
            string fit = WingRequisition.FitOf(airframe);
            foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(airframe))
            {
                entries.Add(new AvKit.PopupEntry(t.Name, t.Id == fit ? "SUPPLY FIT" : "", current != null && t.Id == current.Id));
                entryIds.Add(t.Id);
            }
            if (entries.Count == 0) return;
            popup.Show(WmcKit.PopupArea(page, body, (RectTransform)picker.transform, entries.Count, scroll), entries, PickTemplate);
            WmcKit.FitAll(page);
        }

        private void PickTemplate(int i)
        {
            if (airframe == null || i < 0 || i >= entryIds.Count) return;
            editing[airframe.jsonKey] = entryIds[i];
            deleteGate = new ConfirmGate();
            hpPage = 0;
            WmcPanel.Instance?.Refresh();
        }

        private void New()
        {
            if (airframe == null || layout == null) return;
            // Review R5: a new template starts from the airframe's standard stores (an empty one dropped internal guns and radomes).
            var seed = new List<string>(layout.Sets);
            WingLoadoutCatalog.StandardKeys(airframe, seed);
            layout.Normalize(seed);
            LoadoutTemplateRecord t = WingLoadoutTemplates.Create(airframe, null, seed);
            if (t == null)
            {
                WingToast.Show(LoadoutWords.NewWhy(true, WingLoadoutTemplates.CountFor(airframe)) ?? "No template was made");
                return;
            }
            editing[airframe.jsonKey] = t.Id;
            WingToast.Show(t.Name + " started for " + airframe.unitName);
            WmcPanel.Instance?.Refresh();
        }

        private void Copy()
        {
            if (current == null) return;
            LoadoutTemplateRecord t = WingLoadoutTemplates.Duplicate(current);
            if (t == null)
            {
                WingToast.Show(LoadoutWords.CopyWhy(true, WingLoadoutTemplates.CountFor(airframe)) ?? "No copy was made");
                return;
            }
            editing[airframe.jsonKey] = t.Id;
            WingToast.Show(t.Name + " copied");
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>Two presses; the second deletes and puts SUPPLY's fit back to AUTO wherever it named this template.</summary>
        private void Delete()
        {
            if (current == null) return;
            LoadoutTemplateRecord t = current;
            if (!deleteGate.Press(t.Id, Time.unscaledTime))
            {
                WingToast.Show(LoadoutWords.DeleteAsk(t.Name));
                WmcPanel.Instance?.Refresh();
                return;
            }
            WingLoadoutTemplates.Delete(t);
            bool wasFit = WingRequisition.DropFit(t.Id);
            editing.Remove(t.AirframeKey);
            WingToast.Show(LoadoutWords.Deleted(t.Name, wasFit));
            WmcPanel.Instance?.Refresh();
        }
    }
}
