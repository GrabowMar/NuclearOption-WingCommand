using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>PLAN's LOG drawer (spec WMC rebuild §PLAN; the bezel's LOG tab moved into the room): wing events and radio lines,
    /// newest first, filtered by element or the selected aircraft; a member's line centres the map on it and shows it in the
    /// aircraft card. It covers the lower part of the map and refreshes only while open.</summary>
    internal sealed partial class RoomTactical
    {
        private const float LogRowHeight = 24f, LogChipHeight = 24f;
        private static readonly string[] LogChips = { "ALL", "A", "B", "C", "D", "SELECTED" };

        private readonly List<LogRow> logRows = new List<LogRow>(LogRows.MaxRows);
        private readonly AvButton[] logChipButtons = new AvButton[LogChips.Length];
        private GameObject logRoot;
        private WmcScroll logScroll;
        private GameObject[] logLineRoots;
        private TMP_Text[] logLabels;
        private Image[] logRails;
        private AvButton[] logHits;
        private uint[] logIds;
        private long[] logKeys;
        private TMP_Text logEmpty;
        private AvButton logToggle;
        private int logElement = -1;
        private bool logSelected;

        public bool LogOpen => logRoot != null && logRoot.activeSelf;
        public int LogRowsShown { get; private set; }

        private void BuildLog(RectTransform mapRect, float mapW, float mapH)
        {
            float h = Mathf.Round(mapH * 0.42f);
            var go = new GameObject("LogDrawer", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(mapRect, false);
            AvKit.Place(rt, new Rect(0f, -mapH + h, mapW, h));
            logRoot = go;
            AvStyled.Box(rt, new Rect(0f, 0f, mapW, h), "panel");
            AvKit.Rule(rt, new Rect(0f, 0f, mapW, 1f), AvTheme.Frame);
            float cw = Mathf.Min(110f, (mapW - 24f - WmcUi.Gap * 5f) / 6f);
            for (int c = 0; c < LogChips.Length; c++)
            {
                int k = c;
                logChipButtons[c] = AvStyled.Button(rt, new Rect(12f + c * (cw + WmcUi.Gap), -8f, cw, LogChipHeight), LogChips[c], "btn",
                    () => PickLogFilter(k), AvButtonStyle.Toggle);
                ids["room.log.filter" + c] = logChipButtons[c];
            }
            float listTop = -8f - LogChipHeight - 6f;
            logScroll = WmcScroll.Build(rt, new Rect(12f, listTop, mapW - 16f, h + listTop - 8f), "LogScroll");
            RectTransform s = logScroll.Content;
            float w = logScroll.Width;
            int n = LogRows.MaxRows;
            logLineRoots = new GameObject[n];
            logLabels = new TMP_Text[n];
            logRails = new Image[n];
            logHits = new AvButton[n];
            logIds = new uint[n];
            logKeys = new long[n];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                var line = new GameObject("LogLine" + i, typeof(RectTransform));
                var lr = (RectTransform)line.transform;
                lr.SetParent(s, false);
                AvKit.Place(lr, new Rect(0f, -i * LogRowHeight, w, LogRowHeight - 2f));
                logHits[i] = WmcUi.Card(lr, new Rect(0f, 0f, w, LogRowHeight - 2f), () => ClickLog(k), out _, out logRails[i]);
                logLabels[i] = AvStyled.Label(lr, new Rect(10f, -3f, w - 16f, LogRowHeight - 6f), "", "row-sub");
                logLabels[i].enableWordWrapping = false;
                ids["room.log.row" + i] = logHits[i];
                logLineRoots[i] = line;
                logKeys[i] = long.MinValue;
                line.SetActive(false);
            }
            logEmpty = AvStyled.Label(s, new Rect(0f, 0f, w, 20f), "Nothing logged yet.", "hint");
            WmcKit.FitAll(rt);
            go.SetActive(false);
        }

        /// <summary>Opens or closes the drawer (the LOG button, automation).</summary>
        public void SetLog(bool open)
        {
            if (logRoot == null) return;
            logRoot.SetActive(open);
            logToggle?.SetLatched(open);
            if (open && last != null) RefreshLog(last);
        }

        private void PickLogFilter(int chip)
        {
            logSelected = chip == 5;
            logElement = chip >= 1 && chip <= 4 ? chip - 1 : -1;
            for (int i = 0; i < logKeys.Length; i++) logKeys[i] = long.MinValue;
            if (last != null) RefreshLog(last);
        }

        private void ClickLog(int i)
        {
            // Review P3 I5: by aircraft, never by seat; a gone aircraft centres nothing.
            if (last == null || logIds == null || logIds[i] == 0u || WmcContext.UnitOf(logIds[i]) == null) return;
            last.Inspected = logIds[i];
            RefreshBoards(last);
            CenterMember();
        }

        private void RefreshLog(WmcContext c)
        {
            if (!LogOpen) return;
            var filter = new LogFilter { Element = logElement, ById = logSelected, Id = c.Selection.Single, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(c.Wing?.Events, RadioDirector.Instance?.Log, logRows, logLabels.Length, filter);
            LogRowsShown = n;
            for (int k = 0; k < logChipButtons.Length; k++)
                logChipButtons[k].SetLatched(k == 5 ? logSelected : k == 0 ? !logSelected && logElement < 0 : !logSelected && logElement == k - 1);
            logChipButtons[5].SetEnabled(c.Selection.Single != 0u);
            logScroll.SetContentHeight(Mathf.Max(1, n) * LogRowHeight);
            for (int i = 0; i < logLabels.Length; i++)
            {
                bool on = i < n;
                if (logLineRoots[i].activeSelf != on) logLineRoots[i].SetActive(on);
                logIds[i] = on ? logRows[i].Id : 0u;
                if (!on)
                {
                    logKeys[i] = long.MinValue;
                    continue;
                }
                LogRow r = logRows[i];
                long key = (long)(r.Time * 10f) * 1009L + r.Member * 31L + (r.Radio ? 7L : 0L) + (r.Text?.Length ?? 0) + r.Id;
                if (key == logKeys[i]) continue;
                logKeys[i] = key;
                // A radio line or a wing-level event has no one to centre on: no false click target (review P1 m3).
                logHits[i].SetEnabled(r.Id != 0u);
                WmcUi.SetRail(logRails[i], r.Radio ? "info" : r.Member < 0 ? "armed" : "ready");
                logLabels[i].text = WmcText.Clock(r.Time) + "  " + (r.Radio ? r.Text : LogRows.Who(r.Member) + " " + r.Text);
                logLabels[i].color = r.Radio ? AvTheme.Dim : AvTheme.TextPrimary;
            }
            if (logEmpty.gameObject.activeSelf != (n == 0)) logEmpty.gameObject.SetActive(n == 0);
        }
    }
}
