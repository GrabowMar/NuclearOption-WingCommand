using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>LOG (spec M7b §3): wing events and radio lines, newest first; a row with a member centres the map on it.</summary>
    internal sealed class WmcLogTab : IWmcTab
    {
        private const float RowHeight = 20f;

        private readonly Dictionary<string, AvButton> ids;
        private readonly List<LogRow> rows = new List<LogRow>(LogRows.MaxRows);
        private TMP_Text[] labels;
        private int[] members;
        private TMP_Text empty;
        private WmcContext last;

        public WmcLogTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => 0f;

        public string Hint => "Click a wingman's line to centre the map on it.";

        public void Build(RectTransform page, Rect body)
        {
            int n = Mathf.Min(LogRows.MaxRows, Mathf.FloorToInt(body.height / RowHeight));
            labels = new TMP_Text[n];
            members = new int[n];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                var r = new Rect(body.x, body.y - i * RowHeight, body.width, RowHeight);
                AvButton hit = AvKit.HitButton(page, r, () => Click(k));
                ids["log.row" + i] = hit;
                labels[i] = AvStyled.Label(page, new Rect(r.x + 4f, r.y, r.width - 8f, r.height), "", "row-sub");
                labels[i].enableWordWrapping = false;
                labels[i].overflowMode = TextOverflowModes.Ellipsis;
            }
            empty = AvStyled.Label(page, new Rect(body.x, body.y, body.width, 40f), "Nothing logged yet.", "hint");
        }

        private void Click(int i)
        {
            // Review focus 4: the slot may have renumbered or gone; UnitAt returns null and Center does nothing.
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
                labels[i].gameObject.SetActive(on);
                members[i] = on ? rows[i].Member : -1;
                if (!on) continue;
                LogRow r = rows[i];
                labels[i].text = WmcText.Clock(r.Time) + "  " + (r.Radio ? r.Text : LogRows.Who(r.Member) + " " + r.Text);
                labels[i].color = r.Radio ? AvTheme.Dim : AvTheme.TextPrimary;
            }
            empty.gameObject.SetActive(n == 0);
        }
    }
}
