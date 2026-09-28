using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>PLAN › LOG (spec bezel v2 §5; the room's LOG drawer): the wing's events and radio lines, newest first, filtered by
    /// element or the selected aircraft. A line with an aircraft opens it on INSPECT and centres the map on it.</summary>
    internal sealed partial class WmcPlan
    {
        private const float LogRow = 20f, LogChips = 24f;
        private static readonly string[] LogChipLabels = { "ALL", "A", "B", "C", "D", "SELECTED" };

        private readonly List<LogRow> logRows = new List<LogRow>(LogRows.MaxRows);
        private readonly AvButton[] logChipButtons = new AvButton[LogChipLabels.Length];
        private readonly GameObject[] logLineRoots = new GameObject[LogRows.MaxRows];
        private readonly TMP_Text[] logLabels = new TMP_Text[LogRows.MaxRows];
        private readonly Image[] logRails = new Image[LogRows.MaxRows];
        private readonly AvButton[] logHits = new AvButton[LogRows.MaxRows];
        private readonly uint[] logIds = new uint[LogRows.MaxRows];
        private readonly long[] logKeys = new long[LogRows.MaxRows];
        private WmcScroll logScroll;
        private TMP_Text logEmpty, logCount;
        private int logElement = -1, logShown = -1;
        private bool logSelected;
        private long logStamp = long.MinValue;
        private bool logFilled, debrief;
        private AvButton debriefButton;

        /// <summary>LOG lines showing now (automation).</summary>
        public int LogRowsShown { get; private set; }

        private void BuildLog(RectTransform root, float top, float height)
        {
            float cw = (width - 140f - WmcUi.Gap * (LogChipLabels.Length - 1)) / LogChipLabels.Length;
            for (int i = 0; i < LogChipLabels.Length; i++)
            {
                int k = i;
                logChipButtons[i] = AvStyled.Button(root, new Rect(x + i * (cw + WmcUi.Gap), top, cw, LogChips - 2f), LogChipLabels[i], "btn",
                    () => PickLogFilter(k), AvButtonStyle.Toggle);
                ids["plan.log.filter" + i] = logChipButtons[i];
            }
            logCount = WmcKit.Text(root, new Rect(x + width - 136f, top, 66f, LogChips - 2f), "row-sub", TextAlignmentOptions.MidlineRight);
            debriefButton = AvStyled.Button(root, new Rect(x + width - 66f, top, 66f, LogChips - 2f), "DEBRIEF", "btn", ToggleDebrief, AvButtonStyle.Toggle);
            debriefButton.WithTooltip("This sortie in a few lines (kills, losses, tasks), then the last ones.");
            ids["plan.log.debrief"] = debriefButton;
            float listTop = top - LogChips - 4f;
            logScroll = WmcScroll.Build(root, new Rect(x, listTop, width + 8f, height - LogChips - 4f), "PlanLogScroll");
            RectTransform s = logScroll.Content;
            float w = logScroll.Width;
            for (int i = 0; i < logLabels.Length; i++)
            {
                int k = i;
                RectTransform line = Container(s, "LogLine" + i, new Rect(0f, -i * LogRow, w, LogRow - 2f));
                logHits[i] = WmcUi.Card(line, new Rect(0f, 0f, w, LogRow - 2f), () => ClickLog(k), out _, out logRails[i]);
                logLabels[i] = WmcKit.Text(line, new Rect(10f, -1f, w - 16f, LogRow - 4f), "row-sub");
                ids["plan.log.row" + i] = logHits[i];
                logLineRoots[i] = line.gameObject;
                logKeys[i] = long.MinValue;
                line.gameObject.SetActive(false);
            }
            logEmpty = WmcKit.Text(s, new Rect(0f, 0f, w, 20f), "hint");
            logEmpty.text = "Nothing logged yet.";
        }

        private void PickLogFilter(int chip)
        {
            logSelected = chip == 5;
            logElement = chip >= 1 && chip <= 4 ? chip - 1 : -1;
            logFilled = false;
            for (int i = 0; i < logKeys.Length; i++) logKeys[i] = long.MinValue;
            if (last != null) RefreshLog(last);
        }

        private void ClickLog(int i)
        {
            // By aircraft, never by seat; a gone aircraft centres nothing (review P3 I5).
            uint id = logIds[i];
            if (last == null || id == 0u || WmcContext.UnitOf(id) == null) return;
            WmcMap.Center(WmcContext.UnitOf(id));
            if (WingRows.IndexOf(last.Rows, last.Count, id) >= 0) WmcPanel.Instance?.Inspect(id);
        }

        private void ToggleDebrief()
        {
            debrief = !debrief;
            debriefButton.SetLatched(debrief);
            logFilled = false;
            for (int i = 0; i < logKeys.Length; i++) logKeys[i] = long.MinValue;
            if (last != null) RefreshLog(last);
        }

        /// <summary>DEBRIEF: this sortie's lines, then the first line of each kept one (spec WMC rebuild §PLAN DEBRIEF).</summary>
        private void RefreshDebrief()
        {
            DebriefService d = DebriefService.Instance;
            SortieLog s = d?.Sortie;
            long stamp = s == null ? 0 : ((long)(s.End - s.Start) / 10L) * 131L + s.Launched + s.Airborne * 3 + s.Landed * 7 + s.Lost * 11
                                         + s.Kills * 13 + s.TasksDone * 17 + s.TasksFailed * 19 + s.TargetsDown * 23 + s.Relocated * 29 + s.Gcas * 31;
            if (stamp == logStamp && logFilled) return;
            logStamp = stamp;
            logFilled = true;
            int n = 0;
            if (s != null)
                foreach (string line in s.Lines())
                    if (n < logLabels.Length) Line(n++, n == 1 ? "THIS " + line : "   " + line, true);
            DebriefStore store = d?.Store;
            for (int i = 0; store != null && i < store.Count && n < logLabels.Length; i++)
                Line(n++, "EARLIER · " + store.Lines(i)[0], false);
            LogRowsShown = n;
            for (int i = n; i < logLabels.Length; i++)
                if (logLineRoots[i].activeSelf) logLineRoots[i].SetActive(false);
            logShown = -1;
            logScroll.SetContentHeight(Mathf.Max(1, n) * LogRow);
            WmcKit.Set(logCount, "");
            if (logEmpty.gameObject.activeSelf != (n == 0)) logEmpty.gameObject.SetActive(n == 0);
        }

        private void Line(int i, string text, bool current)
        {
            if (!logLineRoots[i].activeSelf) logLineRoots[i].SetActive(true);
            logIds[i] = 0u;
            logKeys[i] = long.MinValue;
            logHits[i].SetEnabled(false);
            WmcUi.SetRail(logRails[i], current ? "ready" : "info");
            logLabels[i].text = text;
            logLabels[i].color = current ? AvTheme.TextPrimary : AvTheme.Dim;
        }

        private void RefreshLog(WmcContext c)
        {
            if (debrief)
            {
                RefreshDebrief();
                return;
            }
            WingEventRing events = c.Client ? null : c.Wing?.Events;
            RadioLog radio = RadioDirector.Instance?.Log;
            // Fill allocates while it describes events: only when something was logged, or the filter or the selection changed.
            long stamp = LogRows.Stamp(events, radio) * 31L + (logElement + 2) * 7L + c.Selection.Single * 3L + (logSelected ? 1L : 0L);
            if (stamp == logStamp && logFilled) return;
            logStamp = stamp;
            logFilled = true;
            var filter = new LogFilter { Element = logElement, ById = logSelected, Id = c.Selection.Single, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(events, radio, logRows, logLabels.Length, filter);
            LogRowsShown = n;
            for (int k = 0; k < logChipButtons.Length; k++)
                logChipButtons[k].SetLatched(k == 5 ? logSelected : k == 0 ? !logSelected && logElement < 0 : !logSelected && logElement == k - 1);
            logChipButtons[5].SetEnabled(c.Selection.Single != 0u);
            if (n != logShown)
            {
                logShown = n;
                logScroll.SetContentHeight(Mathf.Max(1, n) * LogRow);
                WmcKit.Set(logCount, n + " LINES");
            }
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
