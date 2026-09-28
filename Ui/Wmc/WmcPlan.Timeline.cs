using System.Globalization;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>PLAN › TIMELINE (spec bezel v2 §5, P3): each lane's steps as bars, PLAN (frozen at EXECUTE from distances, speeds and
    /// TIME ends; an open end runs to the edge with a "?") over REAL (when each step went out and was done), on one axis from EXECUTE
    /// to a little past now; then STEP · PLANNED · ACTUAL · DIFF. Before EXECUTE it shows the plan as drawn. Once a second while it shows.</summary>
    internal sealed partial class WmcPlan
    {
        private const float AxisH = 16f, BarRow = 13f, LaneLabel = 86f, TableRow = 18f;
        private const int MaxBars = WingPlan.Lanes * WingPlan.MaxSteps * 2;

        private RectTransform barArea;
        private readonly TMP_Text[] axisLabels = new TMP_Text[5];
        private readonly TMP_Text[] laneLabels = new TMP_Text[WingPlan.Lanes * 2];
        private readonly Image[] bars = new Image[MaxBars];
        private readonly TMP_Text[] barTexts = new TMP_Text[MaxBars];
        private readonly TMP_Text[] tableRows = new TMP_Text[WingPlan.Lanes * WingPlan.MaxSteps];
        private readonly float[,] drawnStart = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[,] drawnEnd = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[] fromX = new float[WingPlan.Lanes], fromZ = new float[WingPlan.Lanes], fromSpeed = new float[WingPlan.Lanes];
        private WmcScroll tableScroll;
        private TMP_Text tableHead, nowLabel, timelineEmpty;
        private float barWidth, timelineNext;

        private void BuildTimeline(RectTransform root, float top, float height)
        {
            barWidth = width - LaneLabel;
            for (int i = 0; i < axisLabels.Length; i++)
                axisLabels[i] = WmcKit.Text(root, new Rect(x + LaneLabel + i * barWidth / 4f - 20f, top, 56f, AxisH - 2f), "hint");
            nowLabel = WmcKit.Text(root, new Rect(x + width - 110f, top, 110f, AxisH - 2f), "row-sub", TextAlignmentOptions.MidlineRight);
            float y = top - AxisH;
            barArea = Container(root, "TimelineBars", new Rect(x, y, width, WingPlan.Lanes * 2f * BarRow + 8f));
            for (int i = 0; i < laneLabels.Length; i++)
                laneLabels[i] = WmcKit.Text(barArea, new Rect(0f, -(i * BarRow + (i / 2) * 4f), LaneLabel - 4f, BarRow - 1f), i % 2 == 0 ? "row-sub" : "hint");
            for (int i = 0; i < MaxBars; i++)
            {
                bars[i] = AvKit.Panel(barArea, new Rect(LaneLabel, 0f, 10f, BarRow - 3f), Color.white);
                bars[i].raycastTarget = false;
                barTexts[i] = WmcKit.Text(barArea, new Rect(LaneLabel, 0f, 60f, BarRow - 2f), "hint");
                bars[i].gameObject.SetActive(false);
                barTexts[i].gameObject.SetActive(false);
            }
            y -= WingPlan.Lanes * 2f * BarRow + 12f;
            tableHead = WmcKit.Text(root, new Rect(x, y, width, TableRow - 2f), "metric-key");
            tableHead.text = "STEP              PLANNED     ACTUAL      DIFF";
            y -= TableRow;
            tableScroll = WmcScroll.Build(root, new Rect(x, y, width + 8f, Mathf.Max(40f, height - (top - y))), "PlanTimelineTable");
            for (int i = 0; i < tableRows.Length; i++)
            {
                tableRows[i] = WmcKit.Text(tableScroll.Content, new Rect(0f, -i * TableRow, tableScroll.Width, TableRow - 2f), "row-sub");
                tableRows[i].gameObject.SetActive(false);
            }
            timelineEmpty = WmcKit.Text(root, new Rect(x, top - AxisH, width, 20f), "hint");
            timelineEmpty.text = "No steps yet: draw the plan on ELEMENTS.";
        }

        private void RefreshTimeline(WmcContext c)
        {
            if (Time.unscaledTime < timelineNext) return;
            timelineNext = Time.unscaledTime + 1f;
            WingPlans plans = WingPlans.Instance;
            WingPlan plan = plans?.Plan;
            int steps = plan == null ? 0 : CountSteps(plan);
            bool none = steps == 0;
            if (timelineEmpty.gameObject.activeSelf != none) timelineEmpty.gameObject.SetActive(none);
            if (barArea.gameObject.activeSelf == none) barArea.gameObject.SetActive(!none);
            if (none)
            {
                foreach (TMP_Text t in tableRows) if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }
            PlanRunner r = plans.Runner;
            bool live = r != null && (r.Running || plans.Completed);
            // PLAN: frozen at EXECUTE; before it, the plan as drawn from where the elements are now.
            if (live) System.Array.Copy(plans.PlannedStart, drawnStart, drawnStart.Length);
            if (live) System.Array.Copy(plans.PlannedEnd, drawnEnd, drawnEnd.Length);
            else
            {
                WingService w = c.Client ? null : c.Wing;
                for (int l = 0; l < WingPlan.Lanes; l++)
                    if (!From(w, l, out fromX[l], out fromZ[l], out fromSpeed[l])) fromSpeed[l] = 150f;
                PlanTimeline.Planned(plan, fromX, fromZ, fromSpeed, drawnStart, drawnEnd);
            }
            float now = live && WingService.Instance != null ? WingService.Instance.MissionTime - r.ExecutedAt : 0f;
            float span = Mathf.Max(60f, now);
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    if (!float.IsNaN(drawnEnd[l, s])) span = Mathf.Max(span, drawnEnd[l, s]);
                    else if (!float.IsNaN(drawnStart[l, s])) span = Mathf.Max(span, drawnStart[l, s] + 60f);
                }
            span *= 1.1f;
            for (int i = 0; i < axisLabels.Length; i++) WmcKit.Set(axisLabels[i], "T+" + WmcText.Clock(span * i / 4f));
            WmcKit.Set(nowLabel, live ? (r.Running ? "NOW T+" : "DONE T+") + WmcText.Clock(now) : "NOT RUN");

            int bar = 0, row = 0, laneRow = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                bool on = plan.Steps[l].Count > 0;
                SetActive(laneLabels[2 * l], on);
                SetActive(laneLabels[2 * l + 1], on);
                if (!on) continue;
                float y = -(laneRow * 2f * BarRow + laneRow * 4f);
                laneLabels[2 * l].rectTransform.anchoredPosition = new Vector2(0f, y);
                laneLabels[2 * l + 1].rectTransform.anchoredPosition = new Vector2(0f, y - BarRow);
                WmcKit.Set(laneLabels[2 * l], ElementRoster.Letter(l) + "  PLAN");
                WmcKit.Set(laneLabels[2 * l + 1], "   REAL");
                laneLabels[2 * l].color = WmcMapOverlay.ElementColor(l);
                Color planColor = WmcMapOverlay.ElementColor(l), realColor = planColor;
                planColor.a = 0.35f;
                realColor.a = 0.85f;
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    string kind = PlanWords.Kind(plan.Steps[l][s].Kind);
                    float ps = drawnStart[l, s], pe = drawnEnd[l, s];
                    if (!float.IsNaN(ps)) bar = Bar(bar, y, ps, float.IsNaN(pe) ? span : pe, span, planColor, float.IsNaN(pe) ? kind + " ?" : kind);
                    float rs = live ? r.StartedAt(l, s) : float.NaN, re = live ? r.EndedAt(l, s) : float.NaN;
                    if (!float.IsNaN(rs)) bar = Bar(bar, y - BarRow, rs, float.IsNaN(re) ? now : re, span, realColor, kind);
                    if (row < tableRows.Length)
                    {
                        SetActive(tableRows[row], true);
                        WmcKit.Set(tableRows[row], TableLine(l, s, kind, ps, rs));
                        row++;
                    }
                }
                laneRow++;
            }
            for (int i = bar; i < MaxBars; i++)
            {
                SetActive(bars[i], false);
                SetActive(barTexts[i], false);
            }
            for (int i = row; i < tableRows.Length; i++) SetActive(tableRows[i], false);
            tableScroll.SetContentHeight(Mathf.Max(1, row) * TableRow);
        }

        /// <summary>"B2 ATTACK        T+1:50      T+2:40      LATE 0:50".</summary>
        private static string TableLine(int l, int s, string kind, float planned, float actual)
        {
            string name = (PlanRules.Name(l, s) + " " + kind).PadRight(16);
            string p = float.IsNaN(planned) ? "?" : "T+" + WmcText.Clock(planned);
            string a = float.IsNaN(actual) ? "-" : "T+" + WmcText.Clock(actual);
            string diff = "";
            if (!float.IsNaN(planned) && !float.IsNaN(actual))
            {
                float d = actual - planned;
                diff = Mathf.Abs(d) < 5f ? "ON TIME" : (d > 0f ? "LATE " : "EARLY ") + WmcText.Clock(Mathf.Abs(d));
            }
            return name + "  " + p.PadRight(10) + "  " + a.PadRight(10) + "  " + diff;
        }

        private int Bar(int i, float y, float from, float to, float span, Color color, string text)
        {
            if (i >= MaxBars) return i;
            float x0 = LaneLabel + Mathf.Clamp01(from / span) * barWidth, x1 = LaneLabel + Mathf.Clamp01(to / span) * barWidth;
            float w = Mathf.Max(3f, x1 - x0);
            Image b = bars[i];
            b.rectTransform.anchoredPosition = new Vector2(x0, y - 1f);
            b.rectTransform.sizeDelta = new Vector2(w, BarRow - 3f);
            if (b.color != color) b.color = color;
            SetActive(b, true);
            TMP_Text t = barTexts[i];
            bool fits = w > 34f;
            SetActive(t, fits);
            if (fits)
            {
                t.rectTransform.anchoredPosition = new Vector2(x0 + 2f, y);
                t.rectTransform.sizeDelta = new Vector2(w - 3f, BarRow - 2f);
                WmcKit.Set(t, text);
            }
            return i + 1;
        }

        private static int CountSteps(WingPlan plan)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            return n;
        }

        private static void SetActive(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
