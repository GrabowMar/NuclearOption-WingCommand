using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>PLAN › ELEMENTS, the plan editor on the map (spec bezel v2 §5 full version, §6): the plan bar (NEW?, EXECUTE, ABORT?),
    /// the map tools, a cue that says what the armed tool wants or what the selected step does, then a lane per element — its head
    /// (SELECT, FIT, FORM UP, and RESUME, RETRY, SKIP while the plan runs), its steps (START · KIND · detail · END · state) with the
    /// step editor under the selected one, and + RTB · + REFIT · + FORM UP. A tool's right-click inserts a step after the selected
    /// step of the selected lane. While a plan runs it is read-only.</summary>
    internal sealed partial class WmcPlan
    {
        private const float Row = 24f, StepRow = 21f, LaneGap = 6f, EditorH = 4f * (Row + 2f) + 4f;
        private static readonly PlanTool[] ToolOrder =
        {
            PlanTool.Move, PlanTool.Route, PlanTool.Orbit, PlanTool.Cap, PlanTool.Sweep,
            PlanTool.Attack, PlanTool.Off, PlanTool.Land, PlanTool.Cargo, PlanTool.Replace,
        };
        private static readonly string[] ToolLabels = { "MOVE", "ROUTE", "ORBIT", "CAP", "SWEEP", "ATTACK", "ESCORT", "LAND", "CARGO", "RE-PLACE" };
        private static readonly string[] ToolIds = { "move", "route", "orbit", "cap", "sweep", "attack", "escort", "land", "cargo", "replace" };
        private static readonly string[] ToolTips =
        {
            "A step to fly to a point.", "A step to fly a route: right-click its points, DONE ends it.", "A step to orbit a point.",
            "A step to guard an area: right-press the centre and drag the radius.", "A step to sweep an area: right-press and drag.",
            "A step to attack enemies: right-click one, SHIFT adds more.", "Escort by any element comes with A3.",
            "A step to land at a point (helicopters).", "A step to deliver cargo at a point (helicopters).",
            "Put the selected step's point, area or targets somewhere else.",
        };
        private static readonly PlanStart[] Starts = { PlanStart.Now, PlanStart.Exec, PlanStart.TPlus, PlanStart.After };
        private static readonly PlanEnd[] Ends = { PlanEnd.Arrive, PlanEnd.Time, PlanEnd.Bingo, PlanEnd.Winchester, PlanEnd.TargetsDown };

        private sealed class StepView
        {
            public AvButton Button;
            public Image Rail;
            public TMP_Text Start, Kind, Detail, End, State;
        }

        private sealed class LaneView
        {
            public RectTransform Head, Footer;
            public Image Rail;
            public TMP_Text Title;
            public AvButton Select, Fit, Form, Resume, Retry, Skip;
            public readonly StepView[] Steps = new StepView[WingPlan.MaxSteps];
        }

        private readonly LaneView[] lanes = new LaneView[WingPlan.Lanes];
        private readonly List<uint> elementIds = new List<uint>();
        private readonly ConfirmGate planGate = new ConfirmGate();
        private readonly List<RouteLeg> cardLegs = new List<RouteLeg>();
        private readonly List<RouteRing> cardRings = new List<RouteRing>();
        private WmcScroll elementsScroll;
        private TMP_Text barText, cueText, cueRadiusText, delayText, timeText, altText, radiusText;
        private AvButton execute, clearPlan, cueMinus, cuePlus, cueDone, cueCancel, addElement;
        private AvButton delayMinus, delayPlus, timeMinus, timePlus, altMinus, altPlus, radiusMinus, radiusPlus, replace, up, down, delete;
        private readonly AvButton[] tools = new AvButton[ToolOrder.Length];
        private RectTransform editor;
        private SegmentRow startRow, endRow;
        private int selLane, selStep = -1, lanesShown, version;
        private long elementsKey = long.MinValue;
        private float toolRadius;
        private bool routeOpen;

        /// <summary>Lanes showing now (automation; was the element cards).</summary>
        public int Cards => lanesShown;
        public int SelectedLane => selLane;
        public int SelectedStep => selStep;

        private void BuildElements(RectTransform root, float top, float height)
        {
            float y = top;
            barText = WmcKit.Text(root, new Rect(x, y, width - 170f, Row - 2f), "row-name");
            clearPlan = AvStyled.Button(root, new Rect(x + width - 166f, y, 60f, Row - 2f), "NEW", "btn", NewPlan);
            clearPlan.WithTooltip("A new, empty plan (asks first).");
            execute = AvStyled.Button(root, new Rect(x + width - 102f, y, 102f, Row - 2f), "EXECUTE", "btn", ExecuteOrAbort);
            execute.WithTooltip("Run the plan: every lane's steps in turn. While it runs: ABORT (asks first).");
            ids["plan.bar.new"] = clearPlan;
            ids["plan.bar.execute"] = execute;
            y -= Row + 2f;
            float tw = (width - 4f * WmcUi.Gap) / 5f;
            for (int i = 0; i < ToolOrder.Length; i++)
            {
                int k = i;
                float ty = y - (i / 5) * (Row + 2f);
                tools[i] = AvStyled.Button(root, new Rect(x + (i % 5) * (tw + WmcUi.Gap), ty, tw, Row - 2f), ToolLabels[i], "btn",
                    () => PressTool(k), AvButtonStyle.Toggle);
                tools[i].WithTooltip(ToolTips[i]);
                ids["plan.tool." + ToolIds[i]] = tools[i];
            }
            tools[6].SetEnabled(false);
            y -= 2f * (Row + 2f);
            cueText = WmcKit.Text(root, new Rect(x, y, width - 196f, Row - 2f), "row-sub");
            cueMinus = AvStyled.Button(root, new Rect(x + width - 192f, y, 22f, Row - 2f), "-", "btn", () => StepToolRadius(-1));
            cueRadiusText = WmcKit.Text(root, new Rect(x + width - 168f, y, 52f, Row - 2f), "row-sub", TextAlignmentOptions.Center);
            cuePlus = AvStyled.Button(root, new Rect(x + width - 114f, y, 22f, Row - 2f), "+", "btn", () => StepToolRadius(1));
            cueDone = AvStyled.Button(root, new Rect(x + width - 88f, y, 58f, Row - 2f), "DONE", "btn", EndRoute);
            cueCancel = AvStyled.Button(root, new Rect(x + width - 26f, y, 26f, Row - 2f), "×", "btn", () => last?.Map.Disarm());
            cueMinus.WithTooltip("A smaller area (a right-drag sets it too).");
            cuePlus.WithTooltip("A larger area.");
            cueDone.WithTooltip("The route is complete.");
            cueCancel.WithTooltip("Put the tool down (Esc).");
            ids["plan.cue.minus"] = cueMinus;
            ids["plan.cue.plus"] = cuePlus;
            ids["plan.cue.done"] = cueDone;
            ids["plan.cue.cancel"] = cueCancel;
            y -= Row + 4f;

            elementsScroll = WmcScroll.Build(root, new Rect(x, y, width + 8f, Mathf.Max(40f, height - (top - y))), "PlanLanes");
            RectTransform s = elementsScroll.Content;
            float w = elementsScroll.Width;
            for (int e = 0; e < lanes.Length; e++) lanes[e] = BuildLane(s, e, w);
            BuildEditor(s, w);
            addElement = AvStyled.Button(s, new Rect(0f, 0f, w, Row - 2f), "+ ELEMENT FROM SELECTION", "btn", AddElement);
            addElement.WithTooltip("The selected aircraft become an element of their own (orbiting where they are), with a lane to plan.");
            ids["plan.add.element"] = addElement;
            last = null;
        }

        private LaneView BuildLane(RectTransform s, int e, float w)
        {
            var v = new LaneView();
            v.Head = Container(s, "PlanLane" + ElementRoster.Letter(e), new Rect(0f, 0f, w, Row));
            AvButton back = WmcUi.Card(v.Head, new Rect(0f, 0f, w, Row), null, out _, out v.Rail);
            back.SetEnabled(false);
            float bw = 50f, bx = w - 3f * (bw + WmcUi.Gap);
            v.Title = WmcKit.Text(v.Head, new Rect(8f, -2f, bx - 2f * (bw + WmcUi.Gap) - 12f, Row - 4f), "row-name");
            int k = e;
            v.Resume = AvStyled.Button(v.Head, new Rect(bx - 2f * (bw + WmcUi.Gap), -1f, bw, Row - 2f), "RESUME", "btn", () => LaneAct(k, 0));
            v.Retry = AvStyled.Button(v.Head, new Rect(bx - 2f * (bw + WmcUi.Gap), -1f, bw, Row - 2f), "RETRY", "btn", () => LaneAct(k, 1));
            v.Skip = AvStyled.Button(v.Head, new Rect(bx - (bw + WmcUi.Gap), -1f, bw, Row - 2f), "SKIP", "btn", () => LaneAct(k, 2));
            v.Select = AvStyled.Button(v.Head, new Rect(bx, -1f, bw, Row - 2f), "SELECT", "btn", () => SelectElement(k));
            v.Fit = AvStyled.Button(v.Head, new Rect(bx + bw + WmcUi.Gap, -1f, bw, Row - 2f), "FIT", "btn", () => FitElement(k));
            v.Form = AvStyled.Button(v.Head, new Rect(bx + 2f * (bw + WmcUi.Gap), -1f, bw, Row - 2f), "FORM UP", "btn", () => FormElement(k));
            v.Resume.WithTooltip("The lane was held by your order: send its step again.");
            v.Retry.WithTooltip("Send the refused step again.");
            v.Skip.WithTooltip("Pass over this lane's step; what waits for it goes on.");
            v.Select.WithTooltip("Orders go to this element, and tools add to its lane.");
            v.Fit.WithTooltip("Frame this element and its plan on the map.");
            v.Form.WithTooltip(e == 0 ? "The wing forms up on you." : "The element rejoins A.");
            ids["plan.el" + e + ".resume"] = v.Resume;
            ids["plan.el" + e + ".retry"] = v.Retry;
            ids["plan.el" + e + ".skip"] = v.Skip;
            ids["plan.el" + e + ".select"] = v.Select;
            ids["plan.el" + e + ".fit"] = v.Fit;
            ids["plan.el" + e + ".form"] = v.Form;
            for (int i = 0; i < v.Steps.Length; i++)
            {
                int n = i;
                var sv = new StepView();
                RectTransform row = Container(s, "PlanStep" + ElementRoster.Letter(e) + (i + 1), new Rect(0f, 0f, w, StepRow));
                sv.Button = WmcUi.Card(row, new Rect(0f, 0f, w, StepRow - 1f), () => SelectStep(k, n), out _, out sv.Rail);
                sv.Start = WmcKit.Text(row, new Rect(8f, -1f, 92f, StepRow - 2f), "row-sub");
                sv.Kind = WmcKit.Text(row, new Rect(102f, -1f, 58f, StepRow - 2f), "row-sub");
                sv.Detail = WmcKit.Text(row, new Rect(162f, -1f, w - 162f - 110f, StepRow - 2f), "hint");
                sv.End = WmcKit.Text(row, new Rect(w - 106f, -1f, 52f, StepRow - 2f), "row-sub");
                sv.State = WmcKit.Text(row, new Rect(w - 54f, -1f, 50f, StepRow - 2f), "row-sub", TextAlignmentOptions.MidlineRight);
                sv.Button.WithTooltip("Select this step: the editor opens under it.");
                ids["plan.step" + e + "." + (i + 1)] = sv.Button;
                row.gameObject.SetActive(false);
                v.Steps[i] = sv;
            }
            v.Footer = Container(s, "PlanLaneFoot" + ElementRoster.Letter(e), new Rect(0f, 0f, w, Row));
            float fw = (w - 16f - 2f * WmcUi.Gap) / 3f;
            string[] adds = { "+ RTB", "+ REFIT", "+ FORM UP" };
            string[] addIds = { "rtb", "refit", "formup" };
            PlanKind[] kinds = { PlanKind.Rtb, PlanKind.Refit, PlanKind.FormUp };
            for (int i = 0; i < 3; i++)
            {
                PlanKind kind = kinds[i];
                AvButton b = AvStyled.Button(v.Footer, new Rect(16f + i * (fw + WmcUi.Gap), -1f, fw, Row - 3f), adds[i], "btn", () => AddStep(k, kind));
                b.WithTooltip(i == 2 ? "A step to form up (A on you; the others rejoin A)." : i == 0 ? "A step to go home and land." : "A step to land, rearm, refuel and come back.");
                ids["plan.el" + e + "." + addIds[i]] = b;
            }
            v.Head.gameObject.SetActive(false);
            v.Footer.gameObject.SetActive(false);
            return v;
        }

        private void BuildEditor(RectTransform s, float w)
        {
            editor = Container(s, "PlanStepEditor", new Rect(0f, 0f, w, EditorH));
            float y = -2f, sw = w - 16f - 106f;
            startRow = SegmentRow.Build(editor, new Rect(16f, y, sw, Row - 2f), 48f, "START", new[] { "NOW", "EXEC", "T+", "AFTER" },
                new[] { "Goes as soon as the plan runs.", "Goes on EXECUTE.", "Goes its time after EXECUTE.", "Goes when another lane's step is done (press again for the next)." },
                "plan.edit.start.", new[] { "now", "exec", "tplus", "after" }, ids, PickStart);
            Stepper(editor, w, y, "plan.edit.delay.", () => StepDelay(-1), () => StepDelay(1), out delayMinus, out delayText, out delayPlus,
                "Wait less.", "Wait longer (T+ from EXECUTE, AFTER from the other step).");
            y -= Row + 2f;
            endRow = SegmentRow.Build(editor, new Rect(16f, y, sw, Row - 2f), 48f, "END", new[] { "ARRIVE", "TIME", "BINGO", "WINCH", "TGT DN" },
                new[] { "Ends when its task is flown.", "Ends after its time.", "Ends when a member is at bingo fuel.", "Ends when every member is out of ammunition.", "Ends when its targets are down." },
                "plan.edit.end.", new[] { "arrive", "time", "bingo", "winch", "tgtdn" }, ids, PickEnd);
            Stepper(editor, w, y, "plan.edit.time.", () => StepTime(-1), () => StepTime(1), out timeMinus, out timeText, out timePlus,
                "Shorter.", "Longer.");
            y -= Row + 2f;
            AvStyled.Label(editor, new Rect(16f, y, 48f, Row - 2f), "ALT", "metric-key");
            altMinus = AvStyled.Button(editor, new Rect(64f, y, 22f, Row - 2f), "-", "btn", () => StepAlt(-1));
            altText = WmcKit.Text(editor, new Rect(88f, y, 70f, Row - 2f), "row-sub", TextAlignmentOptions.Center);
            altPlus = AvStyled.Button(editor, new Rect(160f, y, 22f, Row - 2f), "+", "btn", () => StepAlt(1));
            altMinus.WithTooltip("Lower (below 500 m: the task's own).");
            altPlus.WithTooltip("Higher.");
            ids["plan.edit.alt.minus"] = altMinus;
            ids["plan.edit.alt.plus"] = altPlus;
            AvStyled.Label(editor, new Rect(200f, y, 24f, Row - 2f), "R", "metric-key");
            radiusMinus = AvStyled.Button(editor, new Rect(224f, y, 22f, Row - 2f), "-", "btn", () => StepRadius(-1));
            radiusText = WmcKit.Text(editor, new Rect(248f, y, 56f, Row - 2f), "row-sub", TextAlignmentOptions.Center);
            radiusPlus = AvStyled.Button(editor, new Rect(306f, y, 22f, Row - 2f), "+", "btn", () => StepRadius(1));
            radiusMinus.WithTooltip("A smaller area.");
            radiusPlus.WithTooltip("A larger area.");
            ids["plan.edit.r.minus"] = radiusMinus;
            ids["plan.edit.r.plus"] = radiusPlus;
            y -= Row + 2f;
            float ew = (w - 16f - 3f * WmcUi.Gap) / 4f;
            replace = AvStyled.Button(editor, new Rect(16f, y, ew, Row - 2f), "RE-PLACE", "btn", () => PressTool(9));
            up = AvStyled.Button(editor, new Rect(16f + (ew + WmcUi.Gap), y, ew, Row - 2f), "UP", "btn", () => MoveSelected(-1));
            down = AvStyled.Button(editor, new Rect(16f + 2f * (ew + WmcUi.Gap), y, ew, Row - 2f), "DOWN", "btn", () => MoveSelected(1));
            delete = AvStyled.Button(editor, new Rect(16f + 3f * (ew + WmcUi.Gap), y, ew, Row - 2f), "DELETE", "btn", DeleteSelected);
            replace.WithTooltip("Right-click the step's new point, area or target.");
            up.WithTooltip("Fly this step earlier in its lane.");
            down.WithTooltip("Fly this step later in its lane.");
            delete.WithTooltip("Remove this step (asks first); what waited for it goes on EXECUTE.");
            ids["plan.edit.replace"] = replace;
            ids["plan.edit.up"] = up;
            ids["plan.edit.down"] = down;
            ids["plan.edit.delete"] = delete;
            editor.gameObject.SetActive(false);
        }

        private void Stepper(RectTransform parent, float w, float y, string id, System.Action minus, System.Action plus, out AvButton m,
            out TMP_Text text, out AvButton p, string minusTip, string plusTip)
        {
            m = AvStyled.Button(parent, new Rect(w - 104f, y, 22f, Row - 2f), "-", "btn", minus);
            text = WmcKit.Text(parent, new Rect(w - 80f, y, 54f, Row - 2f), "row-sub", TextAlignmentOptions.Center);
            p = AvStyled.Button(parent, new Rect(w - 24f, y, 22f, Row - 2f), "+", "btn", plus);
            m.WithTooltip(minusTip);
            p.WithTooltip(plusTip);
            ids[id + "minus"] = m;
            ids[id + "plus"] = p;
        }

        private static WingPlans Plans => WingPlans.Instance;

        private void RefreshElements(WmcContext c)
        {
            WingPlans plans = Plans;
            WingService w = c.Client ? null : c.Wing;
            if (c.Map.PlanClick == null) c.Map.PlanClick = OnPlanClick;
            WingPlan plan = plans?.Plan;
            bool running = plans != null && plans.Running, editable = plan != null && !running && w != null;
            PlanRunner r = plans?.Runner;
            if (plan != null && (selLane < 0 || selLane >= WingPlan.Lanes)) selLane = 0;
            if (plan != null && selStep >= plan.Steps[selLane].Count) selStep = -1;
            c.Map.ToolLane = LaneName(w, selLane);

            // The key: plan edits, runner states, selection, tool, where the elements are (to the 100 m) and the dock.
            long key = version * 7919L + (running ? 1 : 0) + (plans != null && plans.Completed ? 2 : 0) + selLane * 13L + selStep * 131L
                       + (long)c.Map.Tool * 1543L + (long)toolRadius + c.Count * 17L;
            for (int e = 0; e < WingPlan.Lanes && plan != null; e++)
            {
                key = key * 31L + (w != null && w.Roster.InUse(e) ? 1 : 0);
                if (r != null)
                    for (int s = 0; s < plan.Steps[e].Count; s++) key = key * 7L + (int)r.State(e, s);
                if (From(w, e, out float fx, out float fz, out _)) key = key * 31L + (long)(fx / 100f) * 3L + (long)(fz / 100f);
            }
            key = key * 7L + (planGate.IsArmed("new", Time.unscaledTime) ? 1 : 0) + (planGate.IsArmed("abort", Time.unscaledTime) ? 2 : 0)
                  + (planGate.IsArmed("del" + selLane + "." + selStep, Time.unscaledTime) ? 4 : 0);
            RefreshTools(c, editable, running);
            if (key == elementsKey) return;
            elementsKey = key;

            if (plan == null)
            {
                WmcKit.Set(barText, c.Client ? "The host plans this mission." : "Wing Command is not ready.");
                return;
            }
            WmcKit.Set(barText, PlanWords.Bar(plan, running, plans.Completed));
            execute.SetText(running ? (planGate.IsArmed("abort", Time.unscaledTime) ? "ABORT?" : "ABORT") : "EXECUTE");
            clearPlan.SetText(planGate.IsArmed("new", Time.unscaledTime) ? "NEW?" : "NEW");
            execute.SetEnabled(w != null && (running || Count(plan) > 0));
            clearPlan.SetEnabled(editable && Count(plan) > 0);
            RefreshCue(c, plan, running);

            float y = 0f;
            int shown = 0;
            for (int e = 0; e < lanes.Length; e++)
            {
                LaneView v = lanes[e];
                bool inUse = w != null && w.Roster.InUse(e);
                bool on = w != null && (e == 0 || inUse || plan.Steps[e].Count > 0);
                if (v.Head.gameObject.activeSelf != on) v.Head.gameObject.SetActive(on);
                if (v.Footer.gameObject.activeSelf != on) v.Footer.gameObject.SetActive(on);
                if (!on)
                {
                    for (int i = 0; i < v.Steps.Length; i++) Hide(v.Steps[i]);
                    continue;
                }
                shown++;
                v.Head.anchoredPosition = new Vector2(0f, y);
                y -= Row + 1f;
                int count = inUse ? w.Roster.Count(e) : 0;
                string state = LaneState(plan, r, e, inUse);
                WmcKit.Set(v.Title, ElementRoster.Letter(e) + " " + (inUse ? w.Roster.Name(e) : "NOT FORMED") + " · " + count + " AC · " + state);
                v.Title.color = WmcMapOverlay.ElementColor(e);
                WmcUi.SetRail(v.Rail, e == selLane ? "armed" : "info");
                int cur = r != null ? r.Current(e) : -1;
                StepState cs = cur >= 0 ? r.State(e, cur) : StepState.Pending;
                bool held = running && cur >= 0 && cs == StepState.Held, blocked = running && cur >= 0 && cs == StepState.Blocked;
                Toggle(v.Resume, held);
                Toggle(v.Retry, blocked);
                Toggle(v.Skip, running && cur >= 0);
                if (blocked) v.Retry.WithTooltip("Refused: " + (r.Why(e) ?? "") + ". Send it again.");
                v.Select.SetEnabled(count > 0);
                v.Fit.SetEnabled(WmcMap.Usable && (count > 0 || plan.Steps[e].Count > 0));
                v.Form.SetEnabled(c.CanOrder && (e == 0 || count > 0));

                From(w, e, out float x0, out float z0, out float speed);
                for (int i = 0; i < v.Steps.Length; i++)
                {
                    StepView sv = v.Steps[i];
                    if (i >= plan.Steps[e].Count)
                    {
                        Hide(sv);
                        continue;
                    }
                    PlanStep p = plan.Steps[e][i];
                    RectTransform row = (RectTransform)sv.Button.transform.parent;
                    if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
                    row.anchoredPosition = new Vector2(0f, y);
                    y -= StepRow;
                    WmcKit.Set(sv.Start, (i + 1) + " " + PlanWords.Start(plan, e, i));
                    WmcKit.Set(sv.Kind, PlanWords.Kind(p.Kind));
                    WmcKit.Set(sv.Detail, PlanWords.Detail(p, x0, z0, speed));
                    WmcKit.Set(sv.End, PlanWords.End(p));
                    string st = PlanWords.State(running || (r != null && plans.Completed) ? r : null, e, i);
                    WmcKit.Set(sv.State, st);
                    WmcUi.SetRail(sv.Rail, e == selLane && i == selStep ? "armed" : st == "RUN" ? "ready" : st == "HELD" || st == "BLOCKED" ? "caution" : "info");
                    if (PlanEdit.EndPoint(p, out float ex, out float ez))
                    {
                        x0 = ex;
                        z0 = ez;
                    }
                    if (e == selLane && i == selStep)
                    {
                        editor.anchoredPosition = new Vector2(0f, y);
                        y -= EditorH;
                    }
                }
                v.Footer.anchoredPosition = new Vector2(0f, y);
                y -= Row + LaneGap;
            }
            bool editing = selStep >= 0 && plan.Steps[selLane].Count > selStep;
            if (editor.gameObject.activeSelf != editing) editor.gameObject.SetActive(editing);
            if (editing) RefreshEditor(plan.Steps[selLane][selStep], editable);
            ((RectTransform)addElement.transform).anchoredPosition = new Vector2(0f, y);
            addElement.SetEnabled(editable && c.Selection.Count > 0);
            y -= Row;
            lanesShown = shown;
            elementsScroll.SetContentHeight(Mathf.Max(20f, -y));
        }

        private void RefreshTools(WmcContext c, bool editable, bool running)
        {
            for (int i = 0; i < tools.Length; i++)
            {
                if (i == 6) continue;
                tools[i].SetLatched(ToolOrder[i] != PlanTool.Off && c.Map.Tool == ToolOrder[i]);
                tools[i].SetEnabled(editable && (ToolOrder[i] != PlanTool.Replace || selStep >= 0));
            }
            string why = running ? "ABORT the plan to change it." : c.Client ? "The host plans this mission." : null;
            if (why != null)
                for (int i = 0; i < tools.Length; i++)
                    if (i != 6) tools[i].WithTooltip(why);
        }

        private void RefreshCue(WmcContext c, WingPlan plan, bool running)
        {
            PlanTool tool = c.Map.Tool;
            bool area = tool == PlanTool.Cap || tool == PlanTool.Sweep;
            if (area && toolRadius <= 0f) toolRadius = tool == PlanTool.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius;
            Toggle(cueMinus, area);
            Toggle(cuePlus, area);
            Toggle(cueDone, tool == PlanTool.Route);
            Toggle(cueCancel, tool != PlanTool.Off);
            if (cueRadiusText.gameObject.activeSelf != area) cueRadiusText.gameObject.SetActive(area);
            if (area) WmcKit.Set(cueRadiusText, Km(toolRadius));
            string cue = tool != PlanTool.Off ? PlanWords.ToolCue(tool, LaneName(c.Client ? null : c.Wing, selLane))
                : selStep >= 0 && selStep < plan.Steps[selLane].Count ? PlanWords.Cue(plan, selLane, selStep)
                : running ? "The plan runs: a lane you order yourself holds until RESUME."
                : "Pick a tool, then right-click the map: steps go into the selected lane.";
            WmcKit.Set(cueText, cue);
        }

        private void RefreshEditor(PlanStep p, bool editable)
        {
            int start = System.Array.IndexOf(Starts, p.Start);
            startRow.Set(selStep > 0 && (p.Start == PlanStart.Now || p.Start == PlanStart.Exec) ? 1 : start);
            endRow.Set(System.Array.IndexOf(Ends, p.End));
            string why = editable ? null : "ABORT the plan to change it.";
            startRow.SetEnabled(editable, why);
            endRow.SetEnabled(editable, why);
            bool delay = p.Start == PlanStart.TPlus || p.Start == PlanStart.After;
            WmcKit.Set(delayText, delay ? WmcText.Clock(p.Delay) : "");
            delayMinus.SetEnabled(editable && delay && p.Delay > 0f);
            delayPlus.SetEnabled(editable && delay);
            bool time = p.End == PlanEnd.Time;
            WmcKit.Set(timeText, time ? WmcText.Clock(p.EndSeconds) : "");
            timeMinus.SetEnabled(editable && time && p.EndSeconds > 30f);
            timePlus.SetEnabled(editable && time);
            bool points = p.Points != null && p.Points.Length > 0 && p.Kind != PlanKind.Attack;
            float alt = points ? p.Points[0].Altitude : float.NaN;
            WmcKit.Set(altText, !points ? "" : float.IsNaN(alt) ? "AUTO" : alt.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " M");
            altMinus.SetEnabled(editable && points && !float.IsNaN(alt));
            altPlus.SetEnabled(editable && points);
            bool area = p.Kind == PlanKind.Cap || p.Kind == PlanKind.Sweep;
            WmcKit.Set(radiusText, area ? Km(p.Radius) : "");
            radiusMinus.SetEnabled(editable && area && p.Radius > AreaGuard.MinRadius);
            radiusPlus.SetEnabled(editable && area && p.Radius < AreaGuard.MaxRadius);
            replace.SetEnabled(editable && (points || p.Kind == PlanKind.Attack));
            up.SetEnabled(editable && selStep > 0);
            down.SetEnabled(editable && selStep < Plans.Plan.Steps[selLane].Count - 1);
            delete.SetEnabled(editable);
            delete.SetText(planGate.IsArmed("del" + selLane + "." + selStep, Time.unscaledTime) ? "DELETE?" : "DELETE");
        }

        private static string Km(float metres) =>
            (metres / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM";

        private static int Count(WingPlan plan)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            return n;
        }

        private static string LaneName(WingService w, int e) =>
            w != null && w.Roster.InUse(e) ? w.Roster.Name(e) : ElementRoster.Letter(e);

        private static string LaneState(WingPlan plan, PlanRunner r, int e, bool inUse)
        {
            int n = plan.Steps[e].Count;
            if (n == 0) return "NO STEPS";
            if (r == null || (!r.Running && !(Plans?.Completed ?? false))) return "WAIT EXEC";
            int cur = r.Current(e);
            return cur < 0 ? "THROUGH" : PlanRules.Name(e, cur) + " " + PlanWords.State(r, e, cur);
        }

        /// <summary>Where lane <paramref name="e"/>'s element is and how fast it goes: its task lead when one flies, else its members'
        /// mean. False when it has none.</summary>
        internal static bool From(WingService w, int e, out float x0, out float z0, out float speed)
        {
            x0 = z0 = speed = 0f;
            if (w == null || !w.Roster.InUse(e)) return false;
            WingPlanner p = w.PlannerOf(e);
            if (p != null && p.Active && p.Lead != null)
            {
                x0 = p.Lead.Position.X;
                z0 = p.Lead.Position.Z;
                speed = p.Lead.Speed;
                return true;
            }
            int n = 0;
            foreach (WingMember m in w.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null || w.ElementOf(m) != e) continue;
                x0 += m.Last.Pos.X;
                z0 += m.Last.Pos.Z;
                speed += m.Last.Vel.Length;
                n++;
            }
            if (n == 0) return false;
            x0 /= n;
            z0 /= n;
            speed /= n;
            return true;
        }

        private static void Toggle(AvButton b, bool on)
        {
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        private static void Hide(StepView sv)
        {
            GameObject row = sv.Button.transform.parent.gameObject;
            if (row.activeSelf) row.SetActive(false);
        }

        private void Changed()
        {
            version++;
            elementsKey = long.MinValue;
            if (last != null) RefreshElements(last);
        }

        // ---- The bar, tools and cue ----

        private void NewPlan()
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running) return;
            if (!planGate.Press("new", Time.unscaledTime))
            {
                WingToast.Show("NEW again to drop this plan");
                Changed();
                return;
            }
            plans.Clear();
            selLane = 0;
            selStep = -1;
            routeOpen = false;
            last?.Map.Disarm();
            Changed();
        }

        private void ExecuteOrAbort()
        {
            WingPlans plans = Plans;
            if (plans == null) return;
            if (plans.Running)
            {
                if (!planGate.Press("abort", Time.unscaledTime))
                {
                    WingToast.Show("ABORT again to stop the plan");
                    Changed();
                    return;
                }
                plans.Abort();
                WingToast.Show(plans.Plan.Name + " aborted");
                Changed();
                return;
            }
            last?.Map.Disarm();
            routeOpen = false;
            List<string> errors = plans.Execute();
            if (errors != null && errors.Count > 0)
                WingToast.Show("Cannot run: " + errors[0] + (errors.Count > 1 ? " (+" + (errors.Count - 1) + " more)" : ""));
            else WingToast.Show(plans.Plan.Name + " running");
            Changed();
        }

        private void PressTool(int i)
        {
            WmcContext c = last;
            PlanTool tool = ToolOrder[i];
            if (c == null || tool == PlanTool.Off) return;
            if (c.Map.Tool == tool)
            {
                c.Map.Disarm();
                routeOpen = false;
                Changed();
                return;
            }
            if (tool == PlanTool.Replace && selStep < 0)
            {
                WingToast.Show("Select a step first");
                return;
            }
            if (tool == PlanTool.Cap || tool == PlanTool.Sweep) toolRadius = tool == PlanTool.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius;
            c.Map.ToolLane = LaneName(c.Client ? null : c.Wing, selLane);
            routeOpen = false;
            c.Map.ArmTool(c, tool);
            Changed();
        }

        private void StepToolRadius(int dir)
        {
            toolRadius = AreaGuard.Clamp(toolRadius + dir * 1000f);
            Changed();
        }

        private void EndRoute()
        {
            routeOpen = false;
            last?.Map.Disarm();
            Changed();
        }

        /// <summary>A tool's right-click (spec bezel v2 §6): a step after the selected one in the selected lane; ROUTE adds points to
        /// the route it started, ATTACK with SHIFT adds a target, RE-PLACE moves the selected step.</summary>
        private void OnPlanClick(WmcContext c, GlobalPosition point, Unit unit, bool shift, float radius)
        {
            WingPlans plans = Plans;
            if (plans == null || c == null || c.Client) return;
            if (plans.Running)
            {
                WingToast.Show("ABORT the plan to change it");
                return;
            }
            WingPlan plan = plans.Plan;
            PlanTool tool = c.Map.Tool;
            bool enemy = unit != null && !unit.disabled && DynamicMap.GetFactionMode(unit.NetworkHQ, false) == FactionMode.Enemy;
            PlanStep selected = selStep >= 0 && selStep < plan.Steps[selLane].Count ? plan.Steps[selLane][selStep] : null;
            switch (tool)
            {
                case PlanTool.Replace:
                    if (selected == null) return;
                    if (selected.Kind == PlanKind.Attack)
                    {
                        if (!enemy)
                        {
                            WingToast.Show("Right-click an enemy on the map");
                            return;
                        }
                        selected.Targets = new[] { unit.persistentID.Id };
                    }
                    else
                    {
                        PlanEdit.Replace(selected, point.x, point.z);
                        if (radius > 0f && (selected.Kind == PlanKind.Cap || selected.Kind == PlanKind.Sweep)) selected.Radius = radius;
                    }
                    // Review P2: a route re-placed starts again from its first point; the next right-clicks add the rest.
                    if (selected.Kind == PlanKind.Route && c.Map.ArmTool(c, PlanTool.Route))
                    {
                        routeOpen = true;
                        WingToast.Show("Right-click the route's next points; DONE ends it");
                    }
                    else c.Map.Disarm();
                    break;
                case PlanTool.Attack:
                    if (!enemy)
                    {
                        WingToast.Show("Right-click an enemy on the map");
                        return;
                    }
                    if (shift && selected != null && selected.Kind == PlanKind.Attack)
                    {
                        if (!PlanEdit.AddTarget(selected, unit.persistentID.Id)) WingToast.Show("That target is in, or the step is full");
                        break;
                    }
                    Insert(plan, PlanEdit.Attack(unit.persistentID.Id));
                    break;
                case PlanTool.Route:
                    if (routeOpen && selected != null && selected.Kind == PlanKind.Route)
                    {
                        if (!PlanEdit.AddPoint(selected, point.x, point.z)) WingToast.Show("The route is full (16 points)");
                        break;
                    }
                    routeOpen = Insert(plan, PlanEdit.NewStep(PlanTool.Route, point.x, point.z, float.NaN, 0f));
                    break;
                default:
                    Insert(plan, PlanEdit.NewStep(tool, point.x, point.z, float.NaN, radius > 0f ? radius : toolRadius));
                    break;
            }
            WmcPanel.Instance?.Overlay.Ping(point);
            Changed();
        }

        private bool Insert(WingPlan plan, PlanStep step)
        {
            if (step == null) return false;
            int at = PlanEdit.Insert(plan, selLane, selStep, step);
            if (at < 0)
            {
                WingToast.Show("Lane " + ElementRoster.Letter(selLane) + " is full (" + WingPlan.MaxSteps + " steps)");
                return false;
            }
            selStep = at;
            return true;
        }

        // ---- Lanes and steps ----

        private void SelectStep(int lane, int step)
        {
            if (selLane == lane && selStep == step) selStep = -1;
            else
            {
                selLane = lane;
                selStep = step;
            }
            routeOpen = false;
            if (last != null && last.Map.Tool == PlanTool.Replace) last.Map.Disarm();
            Changed();
        }

        private void AddStep(int lane, PlanKind kind)
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running || last == null || last.Client) return;
            if (selLane != lane)
            {
                selLane = lane;
                selStep = plans.Plan.Steps[lane].Count - 1;
            }
            Insert(plans.Plan, new PlanStep { Kind = kind });
            Changed();
        }

        private void LaneAct(int lane, int act)
        {
            PlanRunner r = Plans?.Runner;
            if (r == null || !r.Running) return;
            if (act == 0) r.Resume(lane);
            else if (act == 1) r.Retry(lane);
            else r.Skip(lane, WingService.Instance?.MissionTime ?? 0f);
            Changed();
        }

        private void SelectElement(int e)
        {
            WmcContext c = last;
            if (c == null) return;
            selLane = e;
            selStep = -1;
            routeOpen = false;
            elementIds.Clear();
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) elementIds.Add(c.Rows[i].Id);
            if (elementIds.Count > 0)
            {
                c.Selection.SelectElement(e, elementIds);
                c.Rescope();
            }
            Changed();
            WmcPanel.Instance?.Refresh();
        }

        private void FormElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.FormUp, e == 0 ? WingScope.Wing : WingScope.OfElement(e))));

        /// <summary>The selection becomes an element of its own, orbiting where it is (the grid's DETACH), so it has a lane.</summary>
        private void AddElement()
        {
            WmcContext c = last;
            if (c == null || c.Selection.Count == 0) return;
            Vec3 at = WmcMapInput.From(c, out _);
            WmcUi.Order(c, () =>
            {
                OrderResult r = WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(Waypoint.At(at.X, at.Z)), c.Scope));
                if (r.Accepted && r.Element >= 0)
                {
                    selLane = r.Element;
                    selStep = -1;
                }
            });
            Changed();
        }

        /// <summary>The element's members, its task legs and its plan, framed on the map.</summary>
        private void FitElement(int e)
        {
            WmcContext c = last;
            if (c?.Wing == null || c.Client) return;
            var box = new MapBox();
            foreach (WingMember m in c.Wing.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && c.Wing.ElementOf(m) == e) box.Add(m.Last.Pos.X, m.Last.Pos.Z);
            WingPlanner p = c.Wing.PlannerOf(e);
            if (p != null && p.Active)
            {
                cardLegs.Clear();
                cardRings.Clear();
                RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, cardLegs, cardRings);
                foreach (RouteLeg l in cardLegs) box.Add(l.ToX, l.ToZ);
                foreach (RouteRing r in cardRings)
                {
                    box.Add(r.X - r.Radius, r.Z - r.Radius);
                    box.Add(r.X + r.Radius, r.Z + r.Radius);
                }
            }
            WingPlan plan = Plans?.Plan;
            if (plan != null)
                foreach (PlanStep s in plan.Steps[e])
                {
                    if (s.Points == null) continue;
                    foreach (Waypoint w in s.Points) box.Add(w.X, w.Z);
                    if (s.Radius > 0f && s.Points.Length > 0)
                    {
                        box.Add(s.Points[0].X - s.Radius, s.Points[0].Z - s.Radius);
                        box.Add(s.Points[0].X + s.Radius, s.Points[0].Z + s.Radius);
                    }
                }
            WmcMap.Fit(box);
        }

        // ---- The step editor ----

        private PlanStep Selected()
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running || selStep < 0 || selStep >= plans.Plan.Steps[selLane].Count) return null;
            return plans.Plan.Steps[selLane][selStep];
        }

        private void PickStart(int k)
        {
            PlanStep p = Selected();
            if (p == null) return;
            if (Starts[k] == PlanStart.After)
            {
                if (!PlanEdit.NextAfter(Plans.Plan, selLane, selStep)) WingToast.Show("No other lane's step it could wait for");
            }
            else
            {
                p.Start = Starts[k];
                p.AfterLane = p.AfterStep = -1;
                if (p.Start != PlanStart.TPlus) p.Delay = 0f;
            }
            Changed();
        }

        private void PickEnd(int k)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.End = Ends[k];
            if (p.End == PlanEnd.Time && p.EndSeconds <= 0f) p.EndSeconds = PlanEdit.OrbitSeconds;
            Changed();
        }

        private void StepDelay(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.Delay = Mathf.Clamp(p.Delay + dir * 15f, 0f, 3600f);
            Changed();
        }

        private void StepTime(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.EndSeconds = Mathf.Clamp(p.EndSeconds + dir * 30f, 30f, 7200f);
            Changed();
        }

        private void StepAlt(int dir)
        {
            PlanStep p = Selected();
            if (p?.Points == null || p.Points.Length == 0) return;
            float a = p.Points[0].Altitude;
            a = float.IsNaN(a) ? (dir > 0 ? 1000f : float.NaN) : a + dir * 500f;
            if (!float.IsNaN(a) && a < 500f) a = float.NaN;
            if (!float.IsNaN(a)) a = Mathf.Min(a, 12000f);
            for (int i = 0; i < p.Points.Length; i++) p.Points[i].Altitude = a;
            Changed();
        }

        private void StepRadius(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.Radius = AreaGuard.Clamp(p.Radius + dir * 1000f);
            Changed();
        }

        private void MoveSelected(int dir)
        {
            if (Selected() == null) return;
            if (PlanEdit.MoveStep(Plans.Plan, selLane, selStep, dir)) selStep += dir;
            Changed();
        }

        private void DeleteSelected()
        {
            if (Selected() == null) return;
            if (!planGate.Press("del" + selLane + "." + selStep, Time.unscaledTime))
            {
                Changed();
                return;
            }
            PlanEdit.Remove(Plans.Plan, selLane, selStep);
            selStep = Mathf.Min(selStep, Plans.Plan.Steps[selLane].Count - 1);
            if (last != null && last.Map.Tool == PlanTool.Replace) last.Map.Disarm();
            Changed();
        }
    }
}
