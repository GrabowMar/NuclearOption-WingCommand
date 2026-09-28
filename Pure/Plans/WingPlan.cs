using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>What a plan step does (spec WMC rebuild §PLAN; the tools of PLAN › ELEMENTS).</summary>
    internal enum PlanKind : byte { Move, Route, Orbit, Cap, Sweep, Attack, Land, Cargo, Rtb, Refit, FormUp }

    /// <summary>When a step starts: with EXECUTE (NOW is the same once executed), T+ seconds after it, or AFTER another lane's step
    /// (+ a delay). A step never starts before the one before it in its lane is done.</summary>
    internal enum PlanStart : byte { Now, Exec, TPlus, After }

    /// <summary>When a step ends: its task completed (ARRIVE), after seconds (TIME), at bingo, Winchester, or its targets down.</summary>
    internal enum PlanEnd : byte { Arrive, Time, Bingo, Winchester, TargetsDown }

    internal sealed class PlanStep
    {
        public PlanKind Kind;
        public Waypoint[] Points = new Waypoint[0];
        /// <summary>CAP and SWEEP: the guarded radius, m (0: the order's default).</summary>
        public float Radius;
        /// <summary>ATTACK: the targets' ids (never saved: a saved plan picks them again).</summary>
        public uint[] Targets = new uint[0];
        public PlanStart Start = PlanStart.Exec;
        /// <summary>T+: seconds after EXECUTE; AFTER: seconds after the other step is done.</summary>
        public float Delay;
        public int AfterLane = -1, AfterStep = -1;
        public PlanEnd End = PlanEnd.Arrive;
        public float EndSeconds;
    }

    /// <summary>A wing plan: a lane of steps per element (A–D), up to <see cref="MaxSteps"/> each.</summary>
    internal sealed class WingPlan
    {
        public const int Lanes = ElementRoster.MaxElements, MaxSteps = 12;
        public string Name = "PLAN";
        public readonly List<PlanStep>[] Steps = { new List<PlanStep>(), new List<PlanStep>(), new List<PlanStep>(), new List<PlanStep>() };

        public bool Add(int lane, PlanStep step)
        {
            if (lane < 0 || lane >= Lanes || step == null || Steps[lane].Count >= MaxSteps) return false;
            Steps[lane].Add(step);
            return true;
        }
    }

    /// <summary>A plan's names and its checks (spec WMC rebuild §PLAN PlanRules.Validate).</summary>
    internal static class PlanRules
    {
        /// <summary>"B2": lane B's second step.</summary>
        public static string Name(int lane, int step) => ElementRoster.Letter(lane) + (step + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static List<string> Validate(WingPlan plan)
        {
            var errors = new List<string>();
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    PlanStep p = plan.Steps[l][s];
                    string name = Name(l, s) + " ";
                    int points = p.Points?.Length ?? 0;
                    switch (p.Kind)
                    {
                        case PlanKind.Route:
                            if (points < 2) errors.Add(name + "needs two points");
                            break;
                        case PlanKind.Attack:
                            if ((p.Targets?.Length ?? 0) == 0) errors.Add(name + "needs a target");
                            break;
                        case PlanKind.Rtb:
                        case PlanKind.Refit:
                        case PlanKind.FormUp:
                            break;
                        default:
                            if (points < 1) errors.Add(name + "needs a point");
                            break;
                    }
                    if (p.End == PlanEnd.Arrive && (p.Kind == PlanKind.Orbit || p.Kind == PlanKind.Cap || p.Kind == PlanKind.Sweep))
                        errors.Add(name + "never arrives: end it by time, bingo or Winchester");
                    if (p.End == PlanEnd.TargetsDown && (p.Kind != PlanKind.Attack || (p.Targets?.Length ?? 0) == 0))
                        errors.Add(name + "ends on targets down but has no targets");
                    if ((p.Kind == PlanKind.Cap || p.Kind == PlanKind.Sweep) && p.Radius > 0f &&
                        (p.Radius < AreaGuard.MinRadius || p.Radius > AreaGuard.MaxRadius))
                        errors.Add(name + "radius 2 to 40 km");
                    if (p.Start != PlanStart.After) continue;
                    if (p.AfterLane < 0 || p.AfterLane >= WingPlan.Lanes || p.AfterStep < 0 || p.AfterStep >= plan.Steps[p.AfterLane].Count)
                        errors.Add(name + "waits for a step that does not exist");
                    else if (WaitsFor(plan, p.AfterLane, p.AfterStep, l, s, 0))
                        errors.Add(name + "waits for itself");
                }
            return errors;
        }

        /// <summary>Whether a player's order of this kind to a plan element holds its lane (spec WMC rebuild §PLAN): the
        /// orders that change what the element does. Settings, reactions, ECM, radio and one member's ejection do not.</summary>
        public static bool Holds(OrderKind kind)
        {
            switch (kind)
            {
                case OrderKind.Task:
                case OrderKind.FormUp:
                case OrderKind.Rtb:
                case OrderKind.Refit:
                case OrderKind.Engage:
                case OrderKind.Attack:
                case OrderKind.Splash:
                case OrderKind.BreakOff:
                case OrderKind.LandHere:
                case OrderKind.TakeOff:
                case OrderKind.DeliverCargo:
                case OrderKind.Rescue:
                case OrderKind.EscortMe:
                case OrderKind.EscortTarget:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether step (l, s) — or anything it waits for — waits for (tl, ts): the step before it in its lane, and its AFTER.</summary>
        internal static bool WaitsFor(WingPlan plan, int l, int s, int tl, int ts, int depth = 0)
        {
            if (l == tl && s == ts) return true;
            if (depth > WingPlan.Lanes * WingPlan.MaxSteps) return true;
            if (s > 0 && WaitsFor(plan, l, s - 1, tl, ts, depth + 1)) return true;
            PlanStep p = plan.Steps[l][s];
            if (p.Start != PlanStart.After || p.AfterLane < 0 || p.AfterLane >= WingPlan.Lanes || p.AfterStep < 0
                || p.AfterStep >= plan.Steps[p.AfterLane].Count) return false;
            return WaitsFor(plan, p.AfterLane, p.AfterStep, tl, ts, depth + 1);
        }
    }

    /// <summary>A step as the order its element is given (spec WMC rebuild §PLAN PlanCompile), marked as the plan's. Lane A is
    /// element A, never the wing: a wing-scoped task merges every other element back into A.</summary>
    internal static class PlanCompile
    {
        public static WingOrder Order(PlanStep p, int element)
        {
            WingScope scope = WingScope.OfElement(element);
            Waypoint at = p.Points != null && p.Points.Length > 0 ? p.Points[0] : default;
            WingOrder o;
            switch (p.Kind)
            {
                case PlanKind.Route: o = WingOrder.Tasked(WingTask.Route(p.Points), scope); break;
                case PlanKind.Orbit: o = WingOrder.Tasked(WingTask.Orbit(at), scope); break;
                case PlanKind.Cap: o = WingOrder.Tasked(WingTask.Cap(at, p.Radius > 0f ? p.Radius : AreaGuard.CapRadius), scope); break;
                case PlanKind.Sweep: o = WingOrder.Tasked(WingTask.Sweep(at, p.Radius > 0f ? p.Radius : AreaGuard.SweepRadius), scope); break;
                case PlanKind.Attack: o = new WingOrder { Kind = OrderKind.Attack, Units = p.Targets, Scope = scope }; break;
                case PlanKind.Land:
                    at.Action = ArrivalAction.Land;
                    o = WingOrder.Tasked(WingTask.Move(at), scope);
                    break;
                case PlanKind.Cargo:
                    at.Action = ArrivalAction.Cargo;
                    o = WingOrder.Tasked(WingTask.Move(at), scope);
                    break;
                case PlanKind.Rtb: o = WingOrder.Of(OrderKind.Rtb, scope); break;
                case PlanKind.Refit: o = WingOrder.Of(OrderKind.Refit, scope); break;
                case PlanKind.FormUp: o = WingOrder.Of(OrderKind.FormUp, scope); break;
                default: o = WingOrder.Tasked(WingTask.Move(at), scope); break;
            }
            o.Source = OrderSource.Plan;
            return o;
        }
    }
}
