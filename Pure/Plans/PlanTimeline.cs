using System;

namespace WingCommand
{
    /// <summary>The TIMELINE's planned times (spec WMC rebuild §PLAN timeline, frozen at EXECUTE): each step's start and end, in seconds
    /// after EXECUTE, from where each lane's element is and how fast it flies, the steps' distances, TIME ends, T+ and AFTER. An end that
    /// cannot be known in advance (bingo, Winchester, targets down, an RTB) is NaN, and so is every start that waits for it.</summary>
    internal static class PlanTimeline
    {
        /// <summary>Fills <paramref name="start"/> and <paramref name="end"/> ([lane, step]) for <paramref name="plan"/>; the lanes'
        /// elements are at (<paramref name="x"/>, <paramref name="z"/>) flying <paramref name="speed"/> m/s. No allocation.</summary>
        public static void Planned(WingPlan plan, float[] x, float[] z, float[] speed, float[,] start, float[,] end)
        {
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < WingPlan.MaxSteps; s++)
                    start[l, s] = end[l, s] = float.NaN;
            // Resolved steps per lane; a pass resolves what it can, AFTERs on later lanes on the next pass.
            Span<int> done = stackalloc int[WingPlan.Lanes];
            Span<float> fx = stackalloc float[WingPlan.Lanes], fz = stackalloc float[WingPlan.Lanes];
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                fx[l] = x[l];
                fz[l] = z[l];
            }
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int l = 0; l < WingPlan.Lanes; l++)
                    while (done[l] < plan.Steps[l].Count)
                    {
                        int s = done[l];
                        PlanStep p = plan.Steps[l][s];
                        float from = s == 0 ? 0f : end[l, s - 1];
                        float at;
                        bool wait = false;
                        switch (p.Start)
                        {
                            case PlanStart.TPlus:
                                at = Math.Max(from, p.Delay);
                                break;
                            case PlanStart.After:
                                if (p.AfterLane < 0 || p.AfterLane >= WingPlan.Lanes || p.AfterLane == l || p.AfterStep < 0
                                    || p.AfterStep >= plan.Steps[p.AfterLane].Count) at = float.NaN;
                                else if (done[p.AfterLane] <= p.AfterStep)
                                {
                                    at = float.NaN;
                                    wait = true;   // not resolved yet: this lane goes on in a later pass
                                }
                                else at = Math.Max(from, end[p.AfterLane, p.AfterStep] + p.Delay);
                                break;
                            default:
                                at = from;
                                break;
                        }
                        if (wait) break;
                        float px = fx[l], pz = fz[l];
                        float length = Duration(p, ref px, ref pz, speed[l]);
                        fx[l] = px;
                        fz[l] = pz;
                        start[l, s] = at;
                        end[l, s] = at + length;
                        done[l]++;
                        changed = true;
                    }
            }
        }

        /// <summary>How long a step lasts (NaN: open), from where the element is; the point it leaves the element at.</summary>
        private static float Duration(PlanStep p, ref float x, ref float z, float speed)
        {
            float metres = 0f;
            if (p.Points != null && p.Kind != PlanKind.Attack)
                foreach (Waypoint w in p.Points)
                {
                    float dx = w.X - x, dz = w.Z - z;
                    metres += (float)Math.Sqrt(dx * dx + dz * dz);
                    x = w.X;
                    z = w.Z;
                }
            float travel = metres <= 0f ? 0f : speed > 1f ? metres / speed : float.NaN;
            bool time = p.End == PlanEnd.Time;
            switch (p.Kind)
            {
                case PlanKind.Move:
                case PlanKind.Route:
                case PlanKind.Land:
                case PlanKind.Cargo:
                    return time ? p.EndSeconds : p.End == PlanEnd.Arrive ? travel : float.NaN;
                case PlanKind.Orbit:
                case PlanKind.Cap:
                case PlanKind.Sweep:
                    return time ? travel + p.EndSeconds : float.NaN;
                case PlanKind.FormUp:
                    return 0f;
                default:
                    return time ? p.EndSeconds : float.NaN;
            }
        }
    }
}
