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
        private const float RowHeight = 44f, BarHeight = 4f;

        private sealed class RowView
        {
            public GameObject Root;
            public TMP_Text Name, Sub, State, Flags;
            public Image Fuel, Ammo, Select;
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

        public float ContentHeight => 0f;

        public string Hint => last != null && last.Count == 0 ? "No wingmen. Call aircraft from the radial menu."
            : "Select a wingman to centre the map on it or release it.";

        public void Build(RectTransform page, Rect body)
        {
            float y = body.y;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = BuildRow(page, new Rect(body.x, y, body.width, RowHeight), i);
                y -= RowHeight + WmcUi.Gap;
            }
            empty = AvStyled.Label(page, new Rect(body.x, body.y, body.width, 40f), "No wingmen yet.", "hint");
            float half = (body.width - WmcUi.Gap) / 2f;
            float by = body.y - body.height + WmcUi.Row;
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
            v.Select = AvKit.Panel(row, r, AvTheme.SurfaceInert);
            v.Select.raycastTarget = false;
            ids["wing.row" + index] = AvKit.HitButton(row, r, () => Pick(index));
            float x = r.x + 8f, w = r.width - 16f;
            v.Name = AvStyled.Label(row, new Rect(x, r.y - 3f, w * 0.6f, 18f), "", "row-name");
            v.State = AvStyled.Label(row, new Rect(x + w * 0.6f, r.y - 3f, w * 0.4f, 18f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            v.Sub = AvStyled.Label(row, new Rect(x, r.y - 21f, w * 0.5f, 14f), "", "row-sub");
            v.Flags = AvStyled.Label(row, new Rect(x + w * 0.5f, r.y - 21f, w * 0.5f, 14f), "", "row-sub",
                align: TextAlignmentOptions.MidlineRight);
            v.BarWidth = (w - 8f) / 2f;
            v.Fuel = WmcUi.Bar(row, new Rect(x, r.y - 37f, v.BarWidth, BarHeight));
            v.Ammo = WmcUi.Bar(row, new Rect(x + v.BarWidth + 8f, r.y - 37f, v.BarWidth, BarHeight));
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
                    WingToast.Show($"Release {WingRows.Number(m.Brain.Slot)}? Press RELEASE again");
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
                v.Name.text = WingRows.Number(m.Slot) + "  " + (callsign ?? "");
                v.Sub.text = u != null && u.definition != null ? u.definition.unitName : WmcText.Unknown;
                v.State.text = WingRows.State(m);
                string flags = WingRows.Flags(m.Flags);
                v.Flags.text = "FUEL " + WmcText.Percent(WingRows.Fraction(m.Fuel)) + "  AMMO " +
                               WmcText.Percent(WingRows.Fraction(m.Ammo)) + (flags.Length > 0 ? "  " + flags : "");
                WmcUi.SetBar(v.Fuel, v.BarWidth, WingRows.Fraction(m.Fuel), WmcUi.Level(WingRows.Fraction(m.Fuel)));
                WmcUi.SetBar(v.Ammo, v.BarWidth, WingRows.Fraction(m.Ammo), WmcUi.Level(WingRows.Fraction(m.Ammo)));
                v.Select.color = m.Id == c.SelectedId ? AvTheme.SurfaceRaised : AvTheme.SurfaceInert;
            }
            empty.gameObject.SetActive(c.Count == 0);
            empty.text = c.Client && c.Stale ? "Waiting for the host's wing." : "No wingmen yet.";
            center.SetEnabled(c.SelectedId != 0u);
            release.SetEnabled(c.SelectedId != 0u && c.CanOrder);
        }
    }
}
