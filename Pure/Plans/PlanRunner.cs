using System.Collections.Generic;

namespace WingCommand
{
    internal enum StepState : byte { Pending, Running, Done, Held, Blocked, Skipped }

    /// <summary>What the engine knows about a lane's element this tick (spec WMC rebuild §PLAN runner contract).</summary>
    internal struct LaneFacts
    {
        /// <summary>The task this lane's running step gave completed (only a completion after the step started).</summary>
        public bool TaskDone;
        public bool Bingo, Winchester, TargetsDown;
        /// <summary>The player ordered this element since the last tick: the lane is HELD.</summary>
        public bool PlayerOrdered;
    }

    /// <summary>An order the runner wants sent for lane <see cref="Lane"/>'s step <see cref="Step"/>.</summary>
    internal struct PlanEmit
    {
        public int Lane, Step;
        public WingOrder Order;
    }

    /// <summary>Runs a <see cref="WingPlan"/> (spec WMC rebuild §PLAN runner; bezel v2 §6): after EXECUTE, each lane's steps in turn —
    /// a step starts when its trigger is met and the one before it is done, and is done when its end is met. A player order holds
    /// its lane (RESUME sends its step again); a refused order blocks it (RETRY, SKIP). The host ticks it at 2 Hz; nothing is sent
    /// or allocated while nothing changes.</summary>
    internal sealed class PlanRunner
    {
        private readonly WingPlan plan;
        private readonly StepState[,] state = new StepState[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[,] doneAt = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly int[] current = new int[WingPlan.Lanes];
        private readonly float[] startAt = new float[WingPlan.Lanes];
        private readonly bool[] resend = new bool[WingPlan.Lanes];
        // Whether a step's order went out (RESUME and RETRY send again only what went out; one held before it did still waits).
        private readonly bool[,] wentOut = new bool[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly string[] why = new string[WingPlan.Lanes];
        private float execAt;

        public PlanRunner(WingPlan plan) => this.plan = plan;

        public WingPlan Plan => plan;
        public bool Running { get; private set; }

        /// <summary>Executed, and every lane is through its steps.</summary>
        public bool Finished
        {
            get
            {
                if (!Running) return false;
                for (int l = 0; l < WingPlan.Lanes; l++)
                    if (Current(l) >= 0) return false;
                return true;
            }
        }
        public float ExecutedAt => execAt;

        public StepState State(int lane, int step) => state[lane, step];

        /// <summary>The lane's step on now (running, waiting, held or blocked); −1 once the lane is through.</summary>
        public int Current(int lane) => current[lane] < plan.Steps[lane].Count ? current[lane] : -1;

        /// <summary>Why the lane is blocked (the order's refusal), or null.</summary>
        public string Why(int lane) => why[lane];

        public void Execute(float time)
        {
            execAt = time;
            Running = true;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                current[l] = 0;
                resend[l] = false;
                why[l] = null;
                for (int s = 0; s < WingPlan.MaxSteps; s++)
                {
                    state[l, s] = StepState.Pending;
                    doneAt[l, s] = float.NaN;
                    wentOut[l, s] = false;
                }
            }
        }

        public void Abort() => Running = false;

        public int Tick(float time, LaneFacts[] facts, List<PlanEmit> into)
        {
            if (!Running) return 0;
            int sent = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                List<PlanStep> steps = plan.Steps[l];
                LaneFacts f = facts != null && l < facts.Length ? facts[l] : default;
                // Bounded: at most every step of the lane changes in one tick.
                for (int guard = 0; guard <= WingPlan.MaxSteps && current[l] < steps.Count; guard++)
                {
                    int i = current[l];
                    StepState st = state[l, i];
                    if (st == StepState.Held || st == StepState.Blocked) break;
                    if (f.PlayerOrdered && (st == StepState.Running || st == StepState.Pending))
                    {
                        state[l, i] = StepState.Held;
                        break;
                    }
                    if (st == StepState.Skipped || st == StepState.Done)
                    {
                        current[l]++;
                        continue;
                    }
                    if (st == StepState.Running)
                    {
                        if (!Ended(steps[i], f, time - startAt[l])) break;
                        state[l, i] = StepState.Done;
                        doneAt[l, i] = time;
                        current[l]++;
                        continue;
                    }
                    if (!resend[l] && !Ready(l, i, time)) break;
                    resend[l] = false;
                    state[l, i] = StepState.Running;
                    wentOut[l, i] = true;
                    startAt[l] = time;
                    into?.Add(new PlanEmit { Lane = l, Step = i, Order = PlanCompile.Order(steps[i], l) });
                    sent++;
                    break;
                }
            }
            return sent;
        }

        private bool Ready(int l, int i, float time)
        {
            PlanStep p = plan.Steps[l][i];
            switch (p.Start)
            {
                case PlanStart.TPlus: return time >= execAt + p.Delay;
                case PlanStart.After:
                {
                    if (p.AfterLane < 0 || p.AfterLane >= WingPlan.Lanes || p.AfterStep < 0 || p.AfterStep >= WingPlan.MaxSteps) return false;
                    StepState dep = state[p.AfterLane, p.AfterStep];
                    return (dep == StepState.Done || dep == StepState.Skipped) && time >= doneAt[p.AfterLane, p.AfterStep] + p.Delay;
                }
                default: return true;
            }
        }

        private static bool Ended(PlanStep p, in LaneFacts f, float elapsed)
        {
            switch (p.End)
            {
                case PlanEnd.Time: return elapsed >= p.EndSeconds;
                case PlanEnd.Bingo: return f.Bingo;
                case PlanEnd.Winchester: return f.Winchester;
                case PlanEnd.TargetsDown: return f.TargetsDown;
                default: return f.TaskDone;
            }
        }

        /// <summary>The lane's order was refused: it waits with the reason for RETRY or SKIP.</summary>
        public void Refused(int lane, string reason)
        {
            int i = current[lane];
            if (i >= plan.Steps[lane].Count) return;
            state[lane, i] = StepState.Blocked;
            why[lane] = reason;
        }

        public void Retry(int lane)
        {
            int i = current[lane];
            if (i >= plan.Steps[lane].Count || state[lane, i] != StepState.Blocked) return;
            state[lane, i] = StepState.Pending;
            why[lane] = null;
            resend[lane] = wentOut[lane, i];
        }

        /// <summary>The lane's step is passed over (it counts as done for the steps that wait for it).</summary>
        public void Skip(int lane, float time)
        {
            int i = current[lane];
            if (i >= plan.Steps[lane].Count) return;
            state[lane, i] = StepState.Skipped;
            doneAt[lane, i] = time;
            why[lane] = null;
            resend[lane] = false;
            current[lane]++;
        }

        public void Hold(int lane)
        {
            int i = current[lane];
            if (i < plan.Steps[lane].Count) state[lane, i] = StepState.Held;
        }

        /// <summary>A held lane goes on: its step's order is sent again.</summary>
        public void Resume(int lane)
        {
            int i = current[lane];
            if (i >= plan.Steps[lane].Count || state[lane, i] != StepState.Held) return;
            state[lane, i] = StepState.Pending;
            resend[lane] = wentOut[lane, i];
        }
    }
}
