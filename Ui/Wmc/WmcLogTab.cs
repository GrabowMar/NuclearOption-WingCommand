using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>LOG (spec M7b §3): wing events and radio lines, newest first, each a small row card whose rail says what it
    /// is (radio info, a member's event ready, a wing event armed); a member's line centres the map on it.</summary>
    internal sealed class WmcLogTab : IWmcTab
    {
        private const float RowHeight = 24f, ChipRow = WmcUi.Row + WmcUi.Gap;
        private static readonly string[] ChipLabels = { "ALL", "A", "B", "C", "D", "SELECTED" };

        private readonly Dictionary<string, AvButton> ids;
        private readonly List<LogRow> rows = new List<LogRow>(LogRows.MaxRows);
        private GameObject[] roots;
        private TMP_Text[] labels;
        private Image[] rails;
        private AvButton[] hits;
        private uint[] members;
        private TMP_Text empty;
        private readonly AvButton[] chips = new AvButton[6];
        private int filterElement = -1;
        private bool filterSelected;
        private RectTransform content;
        private bool scrolls;

        public WmcLogTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => ChipRow + LogRows.MaxRows * RowHeight;

        public string Hint => "Click a wingman's line to centre the map on it. Filter by element or the selected wingman.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            content = page;
            scrolls = page.name == "ScrollContent";
            float cw = (body.width - WmcUi.Gap * 5f) / 6f;
            for (int c = 0; c < chips.Length; c++)
            {
                int k = c;
                chips[c] = AvStyled.Button(page, new Rect(body.x + c * (cw + WmcUi.Gap), body.y, cw, WmcUi.Row), ChipLabels[c], "btn",
                    () => Pick(k), AvButtonStyle.Toggle);
                ids["log.filter" + c] = chips[c];
            }
            body = new Rect(body.x, body.y - ChipRow, body.width, body.height - ChipRow);
            int n = LogRows.MaxRows;
            roots = new GameObject[n];
            labels = new TMP_Text[n];
            rails = new Image[n];
            hits = new AvButton[n];
            members = new uint[n];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                // One stretched container per line so the card, rail, hit target and label hide together.
                var go = new GameObject("LogRow" + i, typeof(RectTransform));
                var row = (RectTransform)go.transform;
                row.SetParent(page, false);
                AvKit.Stretch(row);
                roots[i] = go;
                var r = new Rect(body.x, body.y - i * RowHeight, body.width, RowHeight - 2f);
                hits[i] = WmcUi.Card(row, r, () => Click(k), out _, out rails[i]);
                ids["log.row" + i] = hits[i];
                labels[i] = AvStyled.Label(row, new Rect(r.x + 10f, r.y - 3f, r.width - 16f, r.height - 4f), "", "row-sub");
                labels[i].enableWordWrapping = false;
                labels[i].overflowMode = TextOverflowModes.Ellipsis;
                go.SetActive(false);
            }
            empty = AvStyled.Label(page, new Rect(body.x, body.y, body.width, 40f), "Nothing logged yet.", "hint");
        }

        private void Pick(int chip)
        {
            filterSelected = chip == 5;
            filterElement = chip >= 1 && chip <= 4 ? chip - 1 : -1;
        }

        private void Click(int i)
        {
            // Review P3 I5: by aircraft, never by seat (seats renumber); a gone aircraft centres nothing.
            if (members == null || i >= members.Length || members[i] == 0u) return;
            WmcMap.Center(WmcContext.UnitOf(members[i]));
        }

        public void Refresh(WmcContext c)
        {
            var filter = new LogFilter { Element = filterElement, ById = filterSelected, Id = c.Selection.Single, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(c.Wing?.Events, RadioDirector.Instance?.Log, rows, labels.Length, filter);
            for (int k = 0; k < chips.Length; k++)
                chips[k].SetLatched(k == 5 ? filterSelected : k == 0 ? !filterSelected && filterElement < 0 : !filterSelected && filterElement == k - 1);
            chips[5].SetEnabled(c.Selection.Single != 0u);
            // The scroll range follows the lines shown (review P1 m2): no rebuild, one size.
            if (scrolls) content.sizeDelta = new Vector2(content.sizeDelta.x, ChipRow + Mathf.Max(1, n) * RowHeight);
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i < n;
                if (roots[i].activeSelf != on) roots[i].SetActive(on);
                members[i] = on ? rows[i].Id : 0u;
                if (!on) continue;
                // A radio line or a wing-level event has no one to centre on: no false click target (review P1 m3).
                hits[i].SetEnabled(members[i] != 0u);
                LogRow r = rows[i];
                WmcUi.SetRail(rails[i], r.Radio ? "info" : r.Member < 0 ? "armed" : "ready");
                labels[i].text = WmcText.Clock(r.Time) + "  " + (r.Radio ? r.Text : LogRows.Who(r.Member) + " " + r.Text);
                labels[i].color = r.Radio ? AvTheme.Dim : AvTheme.TextPrimary;
            }
            empty.gameObject.SetActive(n == 0);
        }
    }
}
