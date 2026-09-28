using System;

namespace WingCommand
{
    /// <summary>A PLAN map tool (spec bezel v2 §6): what the next right-click adds to the selected lane. RE-PLACE puts the selected
    /// step's point, area or targets somewhere else. At most one tool or one TACTICAL order mode is armed.</summary>
    internal enum PlanTool : byte { Off, Move, Route, Orbit, Cap, Sweep, Attack, Land, Cargo, Replace }

    /// <summary>Edits a <see cref="WingPlan"/> from the map's tools and the step editor (spec bezel v2 §5 PLAN › ELEMENTS): new steps
    /// with an end that can happen, insertion after the selected step, removal and reordering that keep every AFTER on the step it
    /// named, and AFTER cycling through the other lanes' steps that would not wait for themselves.</summary>
    internal static class PlanEdit
    {
        /// <summary>An ORBIT step's default time, s.</summary>
        public static float OrbitSeconds = 180f;
        public const int MaxRoutePoints = 16;

        /// <summary>The step a point tool places (null for ATTACK, RE-PLACE and OFF): MOVE, ROUTE, LAND and CARGO end on arrival,
        /// ORBIT after <see cref="OrbitSeconds"/>, CAP and SWEEP at bingo. <paramref name="alt"/> NaN: the task's own altitude;
        /// <paramref name="radius"/> 0: the area's default.</summary>
        public static PlanStep NewStep(PlanTool tool, float x, float z, float alt, float radius)
        {
            var p = new PlanStep { Points = new[] { new Waypoint { X = x, Z = z, Altitude = alt, Speed = float.NaN } } };
            switch (tool)
            {
                case PlanTool.Move: p.Kind = PlanKind.Move; break;
                case PlanTool.Route: p.Kind = PlanKind.Route; break;
                case PlanTool.Land: p.Kind = PlanKind.Land; break;
                case PlanTool.Cargo: p.Kind = PlanKind.Cargo; break;
                case PlanTool.Orbit:
                    p.Kind = PlanKind.Orbit;
                    p.End = PlanEnd.Time;
                    p.EndSeconds = OrbitSeconds;
                    break;
                case PlanTool.Cap:
                case PlanTool.Sweep:
                    p.Kind = tool == PlanTool.Cap ? PlanKind.Cap : PlanKind.Sweep;
                    p.End = PlanEnd.Bingo;
                    p.Radius = AreaGuard.Clamp(radius > 0f ? radius : tool == PlanTool.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius);
                    break;
                default: return null;
            }
            return p;
        }

        /// <summary>An ATTACK step on one target, ending when its targets are down.</summary>
        public static PlanStep Attack(uint target) =>
            new PlanStep { Kind = PlanKind.Attack, Targets = new[] { target }, End = PlanEnd.TargetsDown };

        /// <summary>Inserts after step <paramref name="after"/> of the lane (-1 or its last: at the end); the index, or -1 when the
        /// lane is full. AFTERs on the lane's later steps follow them.</summary>
        public static int Insert(WingPlan plan, int lane, int after, PlanStep step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step == null || plan.Steps[lane].Count >= WingPlan.MaxSteps) return -1;
            int count = plan.Steps[lane].Count;
            int at = after < 0 || after >= count - 1 ? count : after + 1;
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane == lane && s.AfterStep >= at) s.AfterStep++;
            });
            plan.Steps[lane].Insert(at, step);
            return at;
        }

        /// <summary>Removes a step: what waited for it starts on EXECUTE, AFTERs on the lane's later steps follow them.</summary>
        public static void Remove(WingPlan plan, int lane, int step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step < 0 || step >= plan.Steps[lane].Count) return;
            plan.Steps[lane].RemoveAt(step);
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane != lane) return;
                if (s.AfterStep == step)
                {
                    s.Start = PlanStart.Exec;
                    s.AfterLane = s.AfterStep = -1;
                    s.Delay = 0f;
                }
                else if (s.AfterStep > step) s.AfterStep--;
            });
        }

        /// <summary>Swaps a step with its neighbour (<paramref name="dir"/> -1 up, +1 down); AFTERs follow the steps they named.</summary>
        public static bool MoveStep(WingPlan plan, int lane, int step, int dir)
        {
            if (lane < 0 || lane >= WingPlan.Lanes) return false;
            var steps = plan.Steps[lane];
            int to = step + dir;
            if (step < 0 || step >= steps.Count || to < 0 || to >= steps.Count) return false;
            PlanStep moved = steps[step];
            steps[step] = steps[to];
            steps[to] = moved;
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane != lane) return;
                if (s.AfterStep == step) s.AfterStep = to;
                else if (s.AfterStep == to) s.AfterStep = step;
            });
            return true;
        }

        /// <summary>Makes the step wait for the next of the other lanes' steps (lane by lane, wrapping) that does not already wait
        /// for it; false, with nothing changed, when none may.</summary>
        public static bool NextAfter(WingPlan plan, int lane, int step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step < 0 || step >= plan.Steps[lane].Count) return false;
            PlanStep p = plan.Steps[lane][step];
            // The candidates in order: every step of the other lanes; start past the current one.
            int total = 0, current = -1;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                if (l == lane) continue;
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    if (p.Start == PlanStart.After && p.AfterLane == l && p.AfterStep == s) current = total;
                    total++;
                }
            }
            for (int k = 1; k <= total; k++)
            {
                int want = (current + k) % total, n = 0;
                for (int l = 0; l < WingPlan.Lanes; l++)
                {
                    if (l == lane) continue;
                    for (int s = 0; s < plan.Steps[l].Count; s++, n++)
                    {
                        if (n != want) continue;
                        if (PlanRules.WaitsFor(plan, l, s, lane, step)) goto next;
                        p.Start = PlanStart.After;
                        p.AfterLane = l;
                        p.AfterStep = s;
                        return true;
                    }
                }
                next:;
            }
            return false;
        }

        /// <summary>The step's point is put somewhere else (its altitude and arrival kept); a route starts again from it.</summary>
        public static void Replace(PlanStep p, float x, float z)
        {
            Waypoint w = p.Points != null && p.Points.Length > 0 ? p.Points[0] : new Waypoint { Altitude = float.NaN, Speed = float.NaN };
            w.X = x;
            w.Z = z;
            p.Points = new[] { w };
        }

        /// <summary>A ROUTE's next point, at the first point's altitude; false when it has <see cref="MaxRoutePoints"/>.</summary>
        public static bool AddPoint(PlanStep p, float x, float z)
        {
            int n = p.Points?.Length ?? 0;
            if (n >= MaxRoutePoints) return false;
            var points = new Waypoint[n + 1];
            if (n > 0) Array.Copy(p.Points, points, n);
            points[n] = new Waypoint { X = x, Z = z, Altitude = n > 0 ? p.Points[0].Altitude : float.NaN, Speed = float.NaN };
            p.Points = points;
            return true;
        }

        /// <summary>One more target for an ATTACK (up to <see cref="WingOrder.MaxUnits"/>); false when it has it or is full.</summary>
        public static bool AddTarget(PlanStep p, uint id)
        {
            int n = p.Targets?.Length ?? 0;
            if (n >= WingOrder.MaxUnits || Array.IndexOf(p.Targets ?? new uint[0], id) >= 0) return false;
            var targets = new uint[n + 1];
            if (n > 0) Array.Copy(p.Targets, targets, n);
            targets[n] = id;
            p.Targets = targets;
            return true;
        }

        /// <summary>Where the step leaves its element: its last point (false for ATTACK, RTB, REFIT and FORM UP).</summary>
        public static bool EndPoint(PlanStep p, out float x, out float z)
        {
            x = z = 0f;
            if (p?.Points == null || p.Points.Length == 0 || p.Kind == PlanKind.Attack) return false;
            Waypoint w = p.Points[p.Points.Length - 1];
            x = w.X;
            z = w.Z;
            return true;
        }

        private static void ForEachLink(WingPlan plan, Action<PlanStep> act)
        {
            for (int l = 0; l < WingPlan.Lanes; l++)
                foreach (PlanStep s in plan.Steps[l])
                    if (s.Start == PlanStart.After) act(s);
        }
    }
}
