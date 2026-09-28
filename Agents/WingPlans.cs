using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Runs the wing's plan on the host (spec WMC rebuild §PLAN runner; bezel v2 §6): at 2 Hz of mission time it
    /// tells the <see cref="PlanRunner"/> what each lane's element is doing and sends the orders it emits through the
    /// executor as the plan's. A player's order to a plan element holds its lane; a refusal blocks it with the executor's
    /// reason. Lane n is element n (ponytail: lanes by seat come with the PLAN editor). Nothing runs on a client, and
    /// nothing allocates while no plan runs.</summary>
    internal sealed class WingPlans : IWingService
    {
        public static WingPlans Instance { get; private set; }

        /// <summary>How often the plan is stepped, s of mission time.</summary>
        public static float TickSeconds = 0.5f;

        public string Name => "Plans";

        /// <summary>The plan being drawn (and, once executed, run).</summary>
        public WingPlan Plan { get; private set; } = new WingPlan();
        /// <summary>The run of <see cref="Plan"/> (null until EXECUTE).</summary>
        public PlanRunner Runner { get; private set; }
        public bool Running => Runner != null && Runner.Running;
        /// <summary>The last run went through every step.</summary>
        public bool Completed { get; private set; }
        public int Sent { get; private set; }

        private readonly LaneFacts[] facts = new LaneFacts[WingPlan.Lanes];
        private readonly List<PlanEmit> emits = new List<PlanEmit>(WingPlan.Lanes);
        private readonly bool[] ordered = new bool[WingPlan.Lanes];
        // Per lane: the event count when its step's order went out, and the last TaskCompleted of its element.
        private readonly long[] sentAt = new long[WingPlan.Lanes];
        private readonly long[] completedAt = new long[WingPlan.Lanes];
        private EventCursor cursor;
        private float lastTick = float.NegativeInfinity;

        public WingPlans() => Instance = this;

        public void Activate()
        {
            Plan = new WingPlan();
            Runner = null;
            Completed = false;
            Sent = 0;
        }

        public void Deactivate() => Runner?.Abort();

        public void FixedTick(float dt) { }

        /// <summary>A new, empty plan (a running one is aborted).</summary>
        public void Clear()
        {
            Abort();
            Plan = new WingPlan();
            Completed = false;
        }

        /// <summary>Runs the plan from now: null, or the checks it fails.</summary>
        public List<string> Execute()
        {
            WingService w = WingService.Instance;
            if (w == null) return new List<string> { "Wing Command is not ready" };
            if (WingNet.ClientOnly) return new List<string> { "the host plans this mission" };
            List<string> errors = PlanRules.Validate(Plan);
            if (errors.Count > 0) return errors;
            Runner = new PlanRunner(Plan);
            Completed = false;
            Sent = 0;
            cursor.Seen = w.Events.Total;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                ordered[l] = false;
                sentAt[l] = completedAt[l] = -1;
            }
            Runner.Execute(w.MissionTime);
            lastTick = float.NegativeInfinity;
            Plugin.Logger.LogInfo($"[Plan] {Plan.Name} executed");
            return null;
        }

        public void Abort()
        {
            if (!Running) return;
            Runner.Abort();
            Plugin.Logger.LogInfo($"[Plan] {Plan.Name} aborted");
        }

        /// <summary>The lanes a player's order to <paramref name="scope"/> reaches, as bits (0 while no plan runs).</summary>
        public int Reaches(in WingScope scope)
        {
            if (!Running) return 0;
            WingService w = WingService.Instance;
            switch (scope.Kind)
            {
                case ScopeKind.Element: return scope.Element >= 0 && scope.Element < WingPlan.Lanes ? 1 << scope.Element : 0;
                case ScopeKind.Members:
                {
                    int bits = 0;
                    if (scope.Members != null && w != null)
                        foreach (uint id in scope.Members)
                        {
                            int e = w.Roster.ElementOf(id);
                            if (e >= 0 && e < WingPlan.Lanes) bits |= 1 << e;
                        }
                    return bits;
                }
                default: return (1 << WingPlan.Lanes) - 1;
            }
        }

        /// <summary>The player ordered these lanes' elements (bits from <see cref="Reaches"/>): they hold on the next step.</summary>
        public void PlayerOrdered(int lanes)
        {
            for (int l = 0; l < WingPlan.Lanes; l++)
                if ((lanes & (1 << l)) != 0 && Runner?.Current(l) >= 0)
                {
                    ordered[l] = true;
                    Plugin.Logger.LogInfo($"[Plan] lane {ElementRoster.Letter(l)} held: the player ordered it");
                }
        }

        public void Tick(float dt)
        {
            if (!Running) return;
            WingService w = WingService.Instance;
            if (w == null || WingNet.ClientOnly) return;
            float now = w.MissionTime;
            if (now - lastTick < TickSeconds) return;
            lastTick = now;
            ReadEvents(w);
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                facts[l] = Facts(w, l);
                // A running step whose element is gone (lost, merged by the player) waits for SKIP; a FORM UP or RTB that
                // emptied it is simply done.
                int s = Runner.Current(l);
                if (s >= 0 && Runner.State(l, s) == StepState.Running && !facts[l].TaskDone && !w.Roster.InUse(l))
                    Block(l, s, $"element {ElementRoster.Letter(l)} is empty");
            }
            emits.Clear();
            Runner.Tick(now, facts, emits);
            foreach (PlanEmit e in emits) Send(w, e);
            if (!Runner.Finished) return;
            Completed = true;
            Runner.Abort();
            Plugin.Logger.LogInfo($"[Plan] {Plan.Name} complete");
            WingToast.Show($"{Plan.Name} complete");
        }

        private void ReadEvents(WingService w)
        {
            while (cursor.Next(w.Events, out WingEvent e))
                if (e.Kind == WingEventKind.TaskCompleted && e.Element < WingPlan.Lanes) completedAt[e.Element] = cursor.Seen - 1;
        }

        private void Send(WingService w, PlanEmit e)
        {
            string name = PlanRules.Name(e.Lane, e.Step);
            sentAt[e.Lane] = w.Events.Total;
            OrderResult r = OrderExecutor.Execute(e.Order);
            if (!r.Accepted)
            {
                Block(e.Lane, e.Step, r.Reason);
                return;
            }
            Sent++;
            Plugin.Logger.LogInfo($"[Plan] {name} {Plan.Steps[e.Lane][e.Step].Kind}: {r.Ack}");
        }

        private void Block(int lane, int step, string why)
        {
            Runner.Refused(lane, why);
            string name = PlanRules.Name(lane, step);
            Plugin.Logger.LogInfo($"[Plan] {name} blocked: {why}");
            WingToast.Show($"PLAN {name} BLOCKED · {why}");
        }

        /// <summary>What lane <paramref name="l"/>'s element is doing, for its current step.</summary>
        private LaneFacts Facts(WingService w, int l)
        {
            var f = new LaneFacts { PlayerOrdered = ordered[l] };
            ordered[l] = false;
            int s = Runner.Current(l);
            if (s < 0) return f;
            PlanStep step = Plan.Steps[l][s];
            int members = 0, flying = 0, empty = 0, back = 0;
            foreach (WingMember m in w.Members)
            {
                if (m.Released || !m.Alive || w.ElementOf(m) != l) continue;
                members++;
                if (m.Bingo.Bingo) f.Bingo = true;
                if (WingService.AmmoFraction(m.Aircraft) <= 0f) empty++;
                if (m.Recovery == null || m.Recovery.Phase < RecoveryPhase.Ground) flying++;
                if (m.Recovery == null && !m.HasPendingRecovery && !m.OnGround) back++;
            }
            f.Winchester = members > 0 && empty == members;
            f.TargetsDown = step.Targets != null && step.Targets.Length > 0 && TargetsDown(step.Targets);
            switch (step.Kind)
            {
                case PlanKind.Attack: f.TaskDone = f.TargetsDown; break;
                case PlanKind.Rtb: f.TaskDone = flying == 0; break;
                // ponytail: a refit is over when every member is airborne with no recovery (members still waiting to
                // start one read as back); a per-member latch if refits end early in game.
                case PlanKind.Refit: f.TaskDone = members == 0 || back == members; break;
                case PlanKind.FormUp: f.TaskDone = true; break;
                default: f.TaskDone = completedAt[l] >= sentAt[l] && sentAt[l] >= 0; break;
            }
            return f;
        }

        private static bool TargetsDown(uint[] ids)
        {
            foreach (uint id in ids)
                if (new PersistentID { Id = id }.TryGetUnit(out Unit u) && u != null && !u.disabled) return false;
            return true;
        }
    }
}
