using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>The words of PLAN › ELEMENTS (spec bezel v2 §5): a step row's START, KIND, detail, END and state, the cue for the
    /// selected step or the armed tool, and the plan bar. Safe glyphs only (· and +).</summary>
    internal static class PlanWords
    {
        public static string Start(WingPlan plan, int lane, int step)
        {
            PlanStep p = plan.Steps[lane][step];
            switch (p.Start)
            {
                case PlanStart.TPlus: return "T+" + WmcText.Clock(p.Delay);
                case PlanStart.After:
                    return "AFTER " + PlanRules.Name(p.AfterLane, p.AfterStep) + (p.Delay > 0f ? " +" + WmcText.Clock(p.Delay) : "");
                default:
                    // Later steps chain in their lane: they go when the one before is done.
                    if (step > 0) return "AFTER " + PlanRules.Name(lane, step - 1);
                    return p.Start == PlanStart.Now ? "NOW" : "EXEC";
            }
        }

        public static string Kind(PlanKind k)
        {
            switch (k)
            {
                case PlanKind.FormUp: return "FORM UP";
                default: return k.ToString().ToUpperInvariant();
            }
        }

        /// <summary>What the step covers from (<paramref name="fromX"/>, <paramref name="fromZ"/>): points, distance and time at
        /// <paramref name="speed"/>, an area's radius, targets, altitude when set.</summary>
        public static string Detail(PlanStep p, float fromX, float fromZ, float speed)
        {
            switch (p.Kind)
            {
                case PlanKind.Attack: return Targets(p);
                case PlanKind.Cap:
                case PlanKind.Sweep: return Join(Radius(p), Alt(p));
                case PlanKind.Rtb:
                case PlanKind.Refit:
                case PlanKind.FormUp: return "";
            }
            float m = 0f, x = fromX, z = fromZ;
            if (p.Points != null)
                foreach (Waypoint w in p.Points)
                {
                    float dx = w.X - x, dz = w.Z - z;
                    m += (float)Math.Sqrt(dx * dx + dz * dz);
                    x = w.X;
                    z = w.Z;
                }
            string km = (m / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM";
            string eta = speed > 1f ? WmcText.Clock(m / speed) : "";
            string points = p.Kind == PlanKind.Route ? (p.Points?.Length ?? 0).ToString(CultureInfo.InvariantCulture) + " PTS" : "";
            return Join(Join(Join(points, km), eta), Alt(p));
        }

        public static string End(PlanStep p)
        {
            switch (p.End)
            {
                case PlanEnd.Time: return WmcText.Clock(p.EndSeconds);
                case PlanEnd.Bingo: return "BINGO";
                case PlanEnd.Winchester: return "WINCH";
                case PlanEnd.TargetsDown: return "TGT DN";
                default: return "ARRIVE";
            }
        }

        /// <summary>WAIT before EXECUTE; then RUN, DONE, HELD, BLOCKED, SKIP, NEXT for the step after the one running.</summary>
        public static string State(PlanRunner r, int lane, int step)
        {
            if (r == null) return "WAIT";
            switch (r.State(lane, step))
            {
                case StepState.Running: return "RUN";
                case StepState.Done: return "DONE";
                case StepState.Held: return "HELD";
                case StepState.Blocked: return "BLOCKED";
                case StepState.Skipped: return "SKIP";
                default:
                    int current = r.Current(lane);
                    return r.Running && current >= 0 && (step == current || step == current + 1) ? "NEXT" : "WAIT";
            }
        }

        /// <summary>"B2 ATTACK · AFTER B1 · 2 TGT · ENDS TGT DN".</summary>
        public static string Cue(WingPlan plan, int lane, int step)
        {
            PlanStep p = plan.Steps[lane][step];
            string head = PlanRules.Name(lane, step) + " " + Kind(p.Kind);
            return Join(Join(head + " · " + Start(plan, lane, step), Short(p)), "ENDS " + End(p));
        }

        /// <summary>"STRIKE NORTH · DRAFT · 7 STEPS".</summary>
        public static string Bar(WingPlan plan, bool running, bool completed)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            string state = running ? "RUNNING" : completed ? "DONE" : "DRAFT";
            return plan.Name + " · " + state + " · " + n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " STEP" : " STEPS");
        }

        /// <summary>What the armed tool wants next, for the lane named <paramref name="lane"/>.</summary>
        public static string ToolCue(PlanTool tool, string lane)
        {
            switch (tool)
            {
                case PlanTool.Off: return "";
                case PlanTool.Cap:
                case PlanTool.Sweep: return "PLAN " + tool.ToString().ToUpperInvariant() + " · " + lane + " · RIGHT-PRESS, DRAG FOR THE RADIUS";
                case PlanTool.Route: return "PLAN ROUTE · " + lane + " · RIGHT-CLICK POINTS, DONE ENDS IT";
                case PlanTool.Attack: return "PLAN ATTACK · " + lane + " · RIGHT-CLICK AN ENEMY (SHIFT ADDS)";
                case PlanTool.Replace: return "RE-PLACE · " + lane + " · RIGHT-CLICK THE NEW POINT OR TARGET";
                default: return "PLAN " + tool.ToString().ToUpperInvariant() + " · " + lane + " · RIGHT-CLICK THE MAP";
            }
        }

        private static string Short(PlanStep p)
        {
            switch (p.Kind)
            {
                case PlanKind.Route: return Join((p.Points?.Length ?? 0).ToString(CultureInfo.InvariantCulture) + " PTS", Alt(p));
                case PlanKind.Cap:
                case PlanKind.Sweep: return Join(Radius(p), Alt(p));
                case PlanKind.Attack: return Targets(p);
                default: return Alt(p);
            }
        }

        private static string Targets(PlanStep p) => (p.Targets?.Length ?? 0).ToString(CultureInfo.InvariantCulture) + " TGT";

        private static string Radius(PlanStep p) =>
            "R " + ((p.Radius > 0f ? p.Radius : p.Kind == PlanKind.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius) / 1000f)
            .ToString("0.#", CultureInfo.InvariantCulture) + " KM";

        private static string Alt(PlanStep p)
        {
            float a = p.Points != null && p.Points.Length > 0 ? p.Points[0].Altitude : float.NaN;
            return float.IsNaN(a) ? "" : a.ToString("N0", CultureInfo.InvariantCulture) + " M";
        }

        private static string Join(string a, string b) =>
            string.IsNullOrEmpty(a) ? b ?? "" : string.IsNullOrEmpty(b) ? a : a + " · " + b;
    }
}
