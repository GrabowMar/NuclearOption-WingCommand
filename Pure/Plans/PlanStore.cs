using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    internal sealed class SavedPlan
    {
        public string Name, Theatre;
        public WingPlan Plan;
    }

    /// <summary>Saved plans (spec WMC rebuild §PLAN PlanStore; bezel v2 §5 PLANS ›), at most <see cref="Max"/>, as <c>plans.user.json</c>:
    /// each with its theatre (only that theatre's are listed), its lanes and steps. Targets are never saved — an ATTACK is picked again
    /// after loading. A plan is saved and loaded as a copy. Reading skips a step it cannot fly (an unknown kind, a point off the map)
    /// with a reason, and an AFTER to a step that is not there starts on EXECUTE; a file that is not JSON loads nothing.</summary>
    internal sealed class PlanStore
    {
        public const int Max = 16, MaxName = 16;
        public const float MaxCoord = 2e6f;
        private readonly List<SavedPlan> plans = new List<SavedPlan>();

        public IReadOnlyList<SavedPlan> Plans => plans;

        /// <summary>Saves a copy under the plan's name (a free "PLAN n" when it has none), replacing this theatre's plan of that name;
        /// false when the store is full.</summary>
        public bool Save(WingPlan plan, string theatre, out string name)
        {
            name = Clean(plan?.Name);
            if (plan == null) return false;
            if (name.Length == 0) name = FreeName(theatre);
            for (int i = 0; i < plans.Count; i++)
                if (plans[i].Theatre == theatre && plans[i].Name == name)
                {
                    plans[i].Plan = Copy(plan, name);
                    return true;
                }
            if (plans.Count >= Max) return false;
            plans.Add(new SavedPlan { Name = name, Theatre = theatre, Plan = Copy(plan, name) });
            return true;
        }

        public bool Remove(int i)
        {
            if (i < 0 || i >= plans.Count) return false;
            plans.RemoveAt(i);
            return true;
        }

        /// <summary>A copy of plan <paramref name="i"/> to edit and run (null when there is none).</summary>
        public WingPlan Load(int i) => i >= 0 && i < plans.Count ? Copy(plans[i].Plan, plans[i].Name) : null;

        /// <summary>The indices of <paramref name="theatre"/>'s plans, in saved order.</summary>
        public List<int> For(string theatre)
        {
            var list = new List<int>();
            for (int i = 0; i < plans.Count; i++)
                if (plans[i].Theatre == theatre) list.Add(i);
            return list;
        }

        private string FreeName(string theatre)
        {
            for (int n = 1; ; n++)
            {
                string name = "PLAN " + n.ToString(CultureInfo.InvariantCulture);
                bool used = false;
                foreach (SavedPlan p in plans) used |= p.Theatre == theatre && p.Name == name;
                if (!used) return name;
            }
        }

        private static string Clean(string name)
        {
            name = (name ?? "").Trim().ToUpperInvariant();
            return name.Length > MaxName ? name.Substring(0, MaxName) : name;
        }

        /// <summary>A deep copy, without targets when <paramref name="keepTargets"/> is false.</summary>
        public static WingPlan Copy(WingPlan from, string name, bool keepTargets = false)
        {
            var to = new WingPlan { Name = name ?? from.Name };
            for (int l = 0; l < WingPlan.Lanes; l++)
                foreach (PlanStep s in from.Steps[l])
                    to.Steps[l].Add(new PlanStep
                    {
                        Kind = s.Kind, Points = s.Points != null ? (Waypoint[])s.Points.Clone() : new Waypoint[0], Radius = s.Radius,
                        Targets = keepTargets && s.Targets != null ? (uint[])s.Targets.Clone() : new uint[0],
                        Start = s.Start, Delay = s.Delay, AfterLane = s.AfterLane, AfterStep = s.AfterStep, End = s.End, EndSeconds = s.EndSeconds,
                    });
            return to;
        }

        public string ToJson()
        {
            var sb = new StringBuilder("{\"plans\":[");
            for (int i = 0; i < plans.Count; i++)
            {
                SavedPlan sp = plans[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(MiniJson.Escape(sp.Name)).Append("\",\"theatre\":\"").Append(MiniJson.Escape(sp.Theatre ?? ""))
                    .Append("\",\"lanes\":[");
                for (int l = 0; l < WingPlan.Lanes; l++)
                {
                    if (l > 0) sb.Append(',');
                    sb.Append('[');
                    List<PlanStep> steps = sp.Plan.Steps[l];
                    for (int s = 0; s < steps.Count; s++)
                    {
                        PlanStep p = steps[s];
                        if (s > 0) sb.Append(',');
                        sb.Append("{\"kind\":\"").Append(p.Kind).Append("\",\"radius\":").Append(Num(p.Radius))
                            .Append(",\"start\":\"").Append(p.Start).Append("\",\"delay\":").Append(Num(p.Delay))
                            .Append(",\"afterLane\":").Append(p.AfterLane.ToString(CultureInfo.InvariantCulture))
                            .Append(",\"afterStep\":").Append(p.AfterStep.ToString(CultureInfo.InvariantCulture))
                            .Append(",\"end\":\"").Append(p.End).Append("\",\"endSeconds\":").Append(Num(p.EndSeconds)).Append(",\"points\":[");
                        for (int k = 0; k < (p.Points?.Length ?? 0); k++)
                        {
                            Waypoint w = p.Points[k];
                            if (k > 0) sb.Append(',');
                            sb.Append("{\"x\":").Append(Num(w.X)).Append(",\"z\":").Append(Num(w.Z)).Append(",\"alt\":").Append(Num(w.Altitude)).Append('}');
                        }
                        sb.Append("]}");
                    }
                    sb.Append(']');
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        private static string Num(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>The store in <paramref name="json"/>; each skipped plan or step, or a file that is not JSON, adds to
        /// <paramref name="errors"/>. Null or empty text (no file yet) is an empty store.</summary>
        public static PlanStore FromJson(string json, List<string> errors)
        {
            var store = new PlanStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            if (!MiniJson.TryParse(json, out object root) || !(root is Dictionary<string, object> top) || !MiniJson.TryGetList(top, "plans", out List<object> list))
            {
                errors.Add("not a plans file; nothing loaded");
                return store;
            }
            foreach (object o in list)
            {
                if (store.plans.Count >= Max)
                {
                    errors.Add("more than " + Max + " plans; the rest ignored");
                    break;
                }
                if (!(o is Dictionary<string, object> d) || !MiniJson.TryGetList(d, "lanes", out List<object> lanes))
                {
                    errors.Add("a plan without lanes");
                    continue;
                }
                string theatre = MiniJson.GetString(d, "theatre") ?? "";
                string name = Clean(MiniJson.GetString(d, "name"));
                if (name.Length == 0) name = store.FreeName(theatre);
                var plan = new WingPlan { Name = name };
                for (int l = 0; l < WingPlan.Lanes && l < lanes.Count; l++)
                {
                    if (!(lanes[l] is List<object> steps)) continue;
                    foreach (object so in steps)
                    {
                        string why = Step(so as Dictionary<string, object>, out PlanStep step);
                        if (why != null) errors.Add(name + " " + ElementRoster.Letter(l) + ": " + why);
                        else if (!plan.Add(l, step)) errors.Add(name + " " + ElementRoster.Letter(l) + ": more than " + WingPlan.MaxSteps + " steps");
                    }
                }
                // An AFTER to a step that is not there (skipped, or a hand-edited file) starts on EXECUTE.
                for (int l = 0; l < WingPlan.Lanes; l++)
                    foreach (PlanStep s in plan.Steps[l])
                        if (s.Start == PlanStart.After && (s.AfterLane < 0 || s.AfterLane >= WingPlan.Lanes || s.AfterLane == l
                                                           || s.AfterStep < 0 || s.AfterStep >= plan.Steps[s.AfterLane].Count))
                        {
                            s.Start = PlanStart.Exec;
                            s.AfterLane = s.AfterStep = -1;
                        }
                store.plans.Add(new SavedPlan { Name = name, Theatre = theatre, Plan = plan });
            }
            return store;
        }

        private static string Step(Dictionary<string, object> d, out PlanStep step)
        {
            step = null;
            if (d == null) return "a step that is not an object";
            if (!Enum.TryParse(MiniJson.GetString(d, "kind") ?? "", out PlanKind kind) || !Enum.IsDefined(typeof(PlanKind), kind))
                return "a step of an unknown kind";
            var s = new PlanStep { Kind = kind };
            if (MiniJson.TryGetList(d, "points", out List<object> list))
            {
                if (list.Count > PlanEdit.MaxRoutePoints) return "more than " + PlanEdit.MaxRoutePoints + " points";
                s.Points = new Waypoint[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is Dictionary<string, object> p)) return "a point that is not an object";
                    float x = Number(p, "x"), z = Number(p, "z");
                    if (float.IsNaN(x) || float.IsNaN(z) || Math.Abs(x) > MaxCoord || Math.Abs(z) > MaxCoord) return "a point off the map";
                    Waypoint w = Waypoint.At(x, z);
                    w.Altitude = Number(p, "alt");
                    s.Points[i] = w;
                }
            }
            float radius = Number(d, "radius");
            s.Radius = float.IsNaN(radius) || radius <= 0f ? 0f : AreaGuard.Clamp(radius);
            if (Enum.TryParse(MiniJson.GetString(d, "start") ?? "", out PlanStart start) && Enum.IsDefined(typeof(PlanStart), start)) s.Start = start;
            if (Enum.TryParse(MiniJson.GetString(d, "end") ?? "", out PlanEnd end) && Enum.IsDefined(typeof(PlanEnd), end)) s.End = end;
            s.Delay = NonNegative(Number(d, "delay"));
            s.EndSeconds = NonNegative(Number(d, "endSeconds"));
            s.AfterLane = MiniJson.GetInt(d, "afterLane", -1);
            s.AfterStep = MiniJson.GetInt(d, "afterStep", -1);
            step = s;
            return null;
        }

        private static float NonNegative(float v) => float.IsNaN(v) || v < 0f ? 0f : Math.Min(v, 7200f);

        /// <summary>A finite number, else NaN (missing, null, text, infinite).</summary>
        private static float Number(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return float.NaN;
            double x;
            switch (v)
            {
                case double dv: x = dv; break;
                case long lv: x = lv; break;
                case int iv: x = iv; break;
                default: return float.NaN;
            }
            return double.IsNaN(x) || double.IsInfinity(x) ? float.NaN : (float)x;
        }
    }
}
