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
        private const float RowHeight = 24f;

        private readonly Dictionary<string, AvButton> ids;
        private readonly List<LogRow> rows = new List<LogRow>(LogRows.MaxRows);
        private GameObject[] roots;
        private TMP_Text[] labels;
        private Image[] rails;
        private int[] members;
        private TMP_Text empty;
        private WmcContext last;

        public WmcLogTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => LogRows.MaxRows * RowHeight;

        public string Hint => "Click a wingman's line to centre the map on it.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            int n = LogRows.MaxRows;
            roots = new GameObject[n];
            labels = new TMP_Text[n];
            rails = new Image[n];
            members = new int[n];
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
                ids["log.row" + i] = WmcUi.Card(row, r, () => Click(k), out _, out rails[i]);
                labels[i] = AvStyled.Label(row, new Rect(r.x + 10f, r.y - 3f, r.width - 16f, r.height - 4f), "", "row-sub");
                labels[i].enableWordWrapping = false;
                labels[i].overflowMode = TextOverflowModes.Ellipsis;
                go.SetActive(false);
            }
            empty = AvStyled.Label(page, new Rect(body.x, body.y, body.width, 40f), "Nothing logged yet.", "hint");
        }

        private void Click(int i)
        {
            // Review focus 4: the slot may have renumbered or gone; UnitAtSlot returns null and Center does nothing.
            if (last == null || members == null || i >= members.Length || members[i] < 0) return;
            WmcMap.Center(last.UnitAtSlot(members[i]));
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            int n = LogRows.Fill(c.Wing?.Events, RadioDirector.Instance?.Log, rows, labels.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i < n;
                if (roots[i].activeSelf != on) roots[i].SetActive(on);
                members[i] = on ? rows[i].Member : -1;
                if (!on) continue;
                LogRow r = rows[i];
                WmcUi.SetRail(rails[i], r.Radio ? "info" : r.Member < 0 ? "armed" : "ready");
                labels[i].text = WmcText.Clock(r.Time) + "  " + (r.Radio ? r.Text : LogRows.Who(r.Member) + " " + r.Text);
                labels[i].color = r.Radio ? AvTheme.Dim : AvTheme.TextPrimary;
            }
            empty.gameObject.SetActive(n == 0);
        }
    }
}
