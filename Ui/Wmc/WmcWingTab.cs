using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>WING (spec M7b §3): one row per member — number, callsign, airframe, state word, fuel and ammo bars,
    /// flags — a row selects; the selected member can be centred on the map or released (second press confirms).</summary>
    internal sealed class WmcWingTab : IWmcTab
    {
        private const float RowHeight = 52f, RowGap = 6f, BarHeight = 3f;

        private sealed class RowView
        {
            public GameObject Root;
            public TMP_Text Name, Sub, State, Flags;
            public Image Fuel, Ammo, Select, Rail;
            public AvButton Hit;
            public bool Selected, Styled;
            public float BarWidth;
        }

        private readonly RowView[] rows = new RowView[FormationCatalog.MaxSlots];
        private readonly Dictionary<string, AvButton> ids;
        private TMP_Text empty;
        private AvButton center, release;
        private WmcContext last;
        private uint armedId;
        private float armedAt;

        public WmcWingTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => FormationCatalog.MaxSlots * (RowHeight + RowGap) + RowGap + WmcUi.Row;

        public string Hint => last != null && last.Count == 0 ? "No wingmen. Call aircraft from the radial menu."
            : "Select a wingman to centre the map on it or release it.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            float half = (body.width - WmcUi.Gap) / 2f;
            float by = body.y;
            float y = body.y - WmcUi.Row - RowGap;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = BuildRow(page, new Rect(body.x, y, body.width, RowHeight), i);
                y -= RowHeight + RowGap;
            }
            empty = AvStyled.Label(page, new Rect(body.x, body.y - WmcUi.Row - RowGap, body.width, 40f), "No wingmen yet.", "hint");
            center = AvStyled.Button(page, new Rect(body.x, by, half, WmcUi.Row), "CENTER", "btn", Center);
            center.WithTooltip("Centre the map on the selected wingman.");
            release = AvStyled.Button(page, new Rect(body.x + half + WmcUi.Gap, by, half, WmcUi.Row), "RELEASE", "btn", Release,
                AvButtonStyle.Danger);
            release.WithTooltip("Release the selected wingman to the game's AI (press twice).");
            ids["wing.center"] = center;
            ids["wing.release"] = release;
        }

        private RowView BuildRow(RectTransform page, Rect r, int index)
        {
            // One stretched container per row (same coordinates as the page) so a row hides as a whole.
            var go = new GameObject("WingRow" + index, typeof(RectTransform));
            var row = (RectTransform)go.transform;
            row.SetParent(page, false);
            AvKit.Stretch(row);
            var v = new RowView { Root = go };
            v.Hit = WmcUi.Card(row, r, () => Pick(index), out v.Select, out v.Rail);
            ids["wing.row" + index] = v.Hit;
            float x = r.x + 12f, w = r.width - 22f;
            v.Name = AvStyled.Label(row, new Rect(x, r.y - 5f, w * 0.62f, 18f), "", "row-name");
            v.State = AvStyled.Label(row, new Rect(x + w * 0.62f, r.y - 5f, w * 0.38f, 18f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            v.Sub = AvStyled.Label(row, new Rect(x, r.y - 24f, w * 0.4f, 14f), "", "row-sub");
            v.Flags = AvStyled.Label(row, new Rect(x + w * 0.4f, r.y - 24f, w * 0.6f, 14f), "", "row-sub",
                align: TextAlignmentOptions.MidlineRight);
            v.Sub.enableWordWrapping = v.Flags.enableWordWrapping = false;
            v.Sub.overflowMode = v.Flags.overflowMode = TextOverflowModes.Ellipsis;
            v.BarWidth = (w - 8f) / 2f;
            v.Fuel = WmcUi.Bar(row, new Rect(x, r.y - 43f, v.BarWidth, BarHeight));
            v.Ammo = WmcUi.Bar(row, new Rect(x + v.BarWidth + 8f, r.y - 43f, v.BarWidth, BarHeight));
            return v;
        }

        private void Pick(int index)
        {
            if (last == null || index >= last.Count) return;
            last.SelectedId = last.Rows[index].Id;
            armedId = 0u;
        }

        private void Center()
        {
            if (last == null) return;
            WmcMap.Center(WmcContext.UnitOf(last.SelectedId));
        }

        private void Release()
        {
            WmcUi.Order(last, () =>
            {
                WingMember m = last.MemberOf(last.SelectedId);
                if (m == null) return;
                if (armedId != last.SelectedId || Time.unscaledTime - armedAt > 3f)
                {
                    armedId = last.SelectedId;
                    armedAt = Time.unscaledTime;
                    WingToast.Show($"Release {WingRows.Number(m.Seat)}? Press RELEASE again");
                    return;
                }
                armedId = 0u;
                last.Wing.Release(m, "released from WMC");
                last.SelectedId = 0u;
                WingToast.Show("Wingman released");
            });
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            // Review focus 3 / M7b-1 I2: a selected aircraft that died or left drops the selection; renumbering keeps it.
            if (WingRows.IndexOf(c.Rows, c.Count, c.SelectedId) < 0) c.SelectedId = 0u;
            for (int i = 0; i < rows.Length; i++)
            {
                RowView v = rows[i];
                bool on = i < c.Count;
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                if (!on) continue;
                SnapshotMember m = c.Rows[i];
                Unit u = WmcContext.UnitOf(m.Id);
                string callsign = u is Aircraft a && !c.Client ? WingPilotRoster.Of(a)?.Callsign : null;
                bool selected = m.Id == c.SelectedId;
                if (!v.Styled || v.Selected != selected)
                {
                    v.Styled = true;
                    v.Selected = selected;
                    if (v.Select != null)
                        v.Hit.SetRowHighlight(v.Select, WmcUi.RowColor(WmcStyle.RowRest(selected)),
                            WmcUi.RowColor(WmcStyle.RowHover(selected)));
                }
                v.Name.text = WmcStyle.SelectedMark(selected) + WingRows.Number(m.Slot) + "  " + (callsign ?? "");
                v.Sub.text = u != null && u.definition != null ? u.definition.unitName : WmcText.Unknown;
                string state = WingRows.State(m);
                v.State.text = state;
                WmcUi.SetRail(v.Rail, WmcStyle.Rail(state));
                string flags = WingRows.Flags(m.Flags);
                float fuel = WingRows.Fraction(m.Fuel), ammo = WingRows.Fraction(m.Ammo);
                string fuelWord = WmcStyle.LevelWord(fuel), ammoWord = WmcStyle.LevelWord(ammo);
                v.Flags.text = "FUEL " + WmcText.Percent(fuel) + (fuelWord.Length > 0 ? " " + fuelWord : "") +
                               "   AMMO " + WmcText.Percent(ammo) + (ammoWord.Length > 0 ? " " + ammoWord : "") +
                               (flags.Length > 0 ? "   " + flags : "");
                WmcUi.SetBar(v.Fuel, v.BarWidth, fuel, WmcUi.LevelColor(WmcStyle.Level(fuel)));
                WmcUi.SetBar(v.Ammo, v.BarWidth, ammo, WmcUi.LevelColor(WmcStyle.Level(ammo)));
            }
            empty.gameObject.SetActive(c.Count == 0);
            empty.text = c.Client && c.Stale ? "Waiting for the host's wing." : "No wingmen yet.";
            center.SetEnabled(c.SelectedId != 0u);
            release.SetEnabled(c.SelectedId != 0u && c.CanOrder);
        }
    }
}
