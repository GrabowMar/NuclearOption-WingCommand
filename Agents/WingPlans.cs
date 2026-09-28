using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Runs the wing's plan on the host (spec WMC rebuild §PLAN runner; bezel v2 §6): at 2 Hz of mission time it
    /// tells the <see cref="PlanRunner"/> what each lane's aircraft are doing and sends the orders it emits through the
    /// executor as the plan's. A player's order to a lane's aircraft holds the lane; a refusal, or its task failing, blocks it
    /// with the reason. A lane starts as its element (lane B is element B) and then follows the aircraft its last step went to:
    /// an element that merged into A (every member recovering, review P2) is regrouped by id for its next step, and its RTB and
    /// REFIT end on those aircraft, not on an empty letter. Nothing runs on a client, and nothing allocates while no plan runs.</summary>
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
        /// <summary>Mission time the last run went through its last step (the TIMELINE's DONE stops there).</summary>
        public float FinishedAt { get; private set; }
        public int Sent { get; private set; }
        /// <summary>Each step's planned start and end, s after EXECUTE, frozen then (the TIMELINE's PLAN; NaN: open).</summary>
        public readonly float[,] PlannedStart = new float[WingPlan.Lanes, WingPlan.MaxSteps], PlannedEnd = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[] fromX = new float[WingPlan.Lanes], fromZ = new float[WingPlan.Lanes], fromSpeed = new float[WingPlan.Lanes];

        private readonly LaneFacts[] facts = new LaneFacts[WingPlan.Lanes];
        private readonly List<PlanEmit> emits = new List<PlanEmit>(WingPlan.Lanes);
        private readonly bool[] ordered = new bool[WingPlan.Lanes];
        // Per lane: the event count when its step's order went out, the last TaskCompleted and TaskFailed of its element, the
        // element its aircraft are in and the aircraft themselves (as its last step went out).
        private readonly long[] sentAt = new long[WingPlan.Lanes];
        private readonly long[] completedAt = new long[WingPlan.Lanes];
        private readonly long[] failedAt = new long[WingPlan.Lanes];
        private readonly TransitionReason[] failedWhy = new TransitionReason[WingPlan.Lanes];
        private readonly int[] laneElement = new int[WingPlan.Lanes];
        private readonly List<uint>[] laneIds =
            { new List<uint>(FormationCatalog.MaxSlots), new List<uint>(FormationCatalog.MaxSlots), new List<uint>(FormationCatalog.MaxSlots), new List<uint>(FormationCatalog.MaxSlots) };
        // Per element (event element index): the last TaskCompleted and TaskFailed seen.
        private readonly long[] elementCompleted = new long[WingPlan.Lanes];
        private readonly long[] elementFailed = new long[WingPlan.Lanes];
        private readonly TransitionReason[] elementFailedWhy = new TransitionReason[WingPlan.Lanes];
        private readonly List<uint> scratch = new List<uint>(FormationCatalog.MaxSlots), elementScratch = new List<uint>(FormationCatalog.MaxSlots);
        private readonly bool[] laneActive = new bool[WingPlan.Lanes];
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

        /// <summary>A finished or aborted run is forgotten (the plan is being edited).</summary>
        public void ForgetRun()
        {
            if (Running) return;
            Runner = null;
            Completed = false;
        }

        /// <summary>Loads <paramref name="plan"/> in place of the one drawn (a running one is aborted).</summary>
        public void Load(WingPlan plan)
        {
            Abort();
            Plan = plan ?? new WingPlan();
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
            // A new run owns nothing yet: the last run's aircraft must not keep lane A from its own element.
            foreach (List<uint> ids in laneIds) ids.Clear();
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                ordered[l] = false;
                sentAt[l] = completedAt[l] = failedAt[l] = elementCompleted[l] = elementFailed[l] = -1;
                laneElement[l] = l;
                Capture(w, l, l);
            }
            for (int l = 0; l < WingPlan.Lanes; l++)
                if (!WmcPlan.From(w, l, out fromX[l], out fromZ[l], out fromSpeed[l])) fromSpeed[l] = 150f;
            PlanTimeline.Planned(Plan, fromX, fromZ, fromSpeed, PlannedStart, PlannedEnd);
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

        /// <summary>The lanes a player's order to <paramref name="scope"/> reaches, as bits (0 while no plan runs): those whose element
        /// it names, or whose aircraft it names.</summary>
        public int Reaches(in WingScope scope)
        {
            if (!Running) return 0;
            WingService w = WingService.Instance;
            int bits = 0;
            switch (scope.Kind)
            {
                case ScopeKind.Element:
                    for (int l = 0; l < WingPlan.Lanes; l++)
                        if (laneElement[l] == scope.Element) bits |= 1 << l;
                    return bits;
                case ScopeKind.Members:
                    if (scope.Members == null) return 0;
                    foreach (uint id in scope.Members)
                        for (int l = 0; l < WingPlan.Lanes; l++)
                            if (PlanLanes.Reaches(laneIds[l], laneElement[l], id, w != null ? w.Roster.ElementOf(id) : -1)) bits |= 1 << l;
                    return bits;
                default: return (1 << WingPlan.Lanes) - 1;
            }
        }

        /// <summary>The player ordered these lanes' aircraft (bits from <see cref="Reaches"/>): they hold on the next step.</summary>
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
                facts[l] = Facts(w, l, out int alive);
                int s = Runner.Current(l);
                if (s < 0 || Runner.State(l, s) != StepState.Running || facts[l].TaskDone) continue;
                // Review P2: a running step whose task failed (nobody left to fly it, bingo home) waits for SKIP with the reason; so
                // does one whose aircraft are all gone.
                if (TaskKind(Plan.Steps[l][s].Kind) && failedAt[l] >= sentAt[l] && sentAt[l] >= 0)
                    Block(l, s, "its task failed" + (failedWhy[l] == TransitionReason.None ? "" : " (" + failedWhy[l] + ")"));
                else if (alive == 0) Block(l, s, $"lane {ElementRoster.Letter(l)} has no aircraft left");
            }
            emits.Clear();
            Runner.Tick(now, facts, emits);
            foreach (PlanEmit e in emits) Send(w, e);
            if (!Runner.Finished) return;
            Completed = true;
            FinishedAt = now;
            Runner.Abort();
            Plugin.Logger.LogInfo($"[Plan] {Plan.Name} complete");
            WingToast.Show($"{Plan.Name} complete");
        }

        /// <summary>Steps whose end comes from their task (the element's planner): its failure is theirs.</summary>
        private static bool TaskKind(PlanKind k) =>
            k == PlanKind.Move || k == PlanKind.Route || k == PlanKind.Orbit || k == PlanKind.Cap || k == PlanKind.Sweep
            || k == PlanKind.Land || k == PlanKind.Cargo;

        private void ReadEvents(WingService w)
        {
            while (cursor.Next(w.Events, out WingEvent e))
            {
                if (e.Element >= WingPlan.Lanes) continue;
                if (e.Kind == WingEventKind.TaskCompleted) elementCompleted[e.Element] = cursor.Seen - 1;
                else if (e.Kind == WingEventKind.TaskFailed)
                {
                    elementFailed[e.Element] = cursor.Seen - 1;
                    elementFailedWhy[e.Element] = e.Reason;
                }
            }
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                completedAt[l] = elementCompleted[laneElement[l]];
                failedAt[l] = elementFailed[laneElement[l]];
                failedWhy[l] = elementFailedWhy[laneElement[l]];
            }
        }

        private void Send(WingService w, PlanEmit e)
        {
            int l = e.Lane;
            string name = PlanRules.Name(l, e.Step);
            WingOrder o = e.Order;
            // The lane's aircraft: its element while that holds exactly them, else its own aircraft by id — an element that merged
            // into A (all recovering), a letter reused by other aircraft, or A holding another lane's jets (review 2 [1], [4]).
            Alive(w, l, scratch);
            Members(w, laneElement[l], elementScratch);
            if (PlanLanes.ByElement(l, laneElement[l], w.Roster.InUse(laneElement[l]), scratch, elementScratch))
                o.Scope = WingScope.OfElement(laneElement[l]);
            else if (scratch.Count > 0) o.Scope = WingScope.OfMembers(scratch.ToArray());
            sentAt[l] = w.Events.Total;
            OrderResult r = OrderExecutor.Execute(o);
            if (!r.Accepted)
            {
                Block(l, e.Step, r.Reason);
                return;
            }
            bool inUse = r.Element >= 0 && r.Element < WingPlan.Lanes && w.Roster.InUse(r.Element);
            if (inUse) laneElement[l] = r.Element;
            // The aircraft the step went to (review 2 [2], [3]: not after an RTB or REFIT, an ATTACK, or a FORM UP into A).
            PlanKind kind = Plan.Steps[l][e.Step].Kind;
            if (PlanLanes.Recapture(l, r.Element, inUse, kind)) Capture(w, l, laneElement[l]);
            Sent++;
            Plugin.Logger.LogInfo($"[Plan] {name} {kind}: {r.Ack}");
        }

        /// <summary>Lane <paramref name="l"/>'s aircraft are element <paramref name="e"/>'s members now — less any flying for another
        /// lane that still has steps (review 2 [4]: lane A took B's recovering jets).</summary>
        private void Capture(WingService w, int l, int e)
        {
            laneIds[l].Clear();
            if (!w.Roster.InUse(e)) return;
            for (int k = 0; k < WingPlan.Lanes; k++) laneActive[k] = Runner != null && Runner.Current(k) >= 0;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && w.ElementOf(m) == e
                    && !PlanLanes.OwnedElsewhere(l, m.Aircraft.persistentID.Id, laneIds, laneActive))
                    laneIds[l].Add(m.Aircraft.persistentID.Id);
        }

        /// <summary>Element <paramref name="e"/>'s live members, into <paramref name="into"/>.</summary>
        private static void Members(WingService w, int e, List<uint> into)
        {
            into.Clear();
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && w.ElementOf(m) == e) into.Add(m.Aircraft.persistentID.Id);
        }

        /// <summary>The lane's aircraft still flying with the wing, into <paramref name="into"/>.</summary>
        private void Alive(WingService w, int l, List<uint> into)
        {
            into.Clear();
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && laneIds[l].Contains(m.Aircraft.persistentID.Id))
                    into.Add(m.Aircraft.persistentID.Id);
        }

        private void Block(int lane, int step, string why)
        {
            Runner.Refused(lane, why);
            string name = PlanRules.Name(lane, step);
            Plugin.Logger.LogInfo($"[Plan] {name} blocked: {why}");
            WingToast.Show($"PLAN {name} BLOCKED · {why}");
        }

        /// <summary>What lane <paramref name="l"/>'s aircraft are doing, for its current step; <paramref name="alive"/> of them fly.</summary>
        private LaneFacts Facts(WingService w, int l, out int alive)
        {
            var f = new LaneFacts { PlayerOrdered = ordered[l] };
            ordered[l] = false;
            alive = 0;
            int s = Runner.Current(l);
            if (s < 0) return f;
            PlanStep step = Plan.Steps[l][s];
            int flying = 0, empty = 0, back = 0;
            foreach (WingMember m in w.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null || !laneIds[l].Contains(m.Aircraft.persistentID.Id)) continue;
                alive++;
                if (m.Bingo.Bingo) f.Bingo = true;
                if (WingService.AmmoFraction(m.Aircraft) <= 0f) empty++;
                if (m.Recovery == null || m.Recovery.Phase < RecoveryPhase.Ground) flying++;
                if (m.Recovery == null && !m.HasPendingRecovery && !m.OnGround) back++;
            }
            f.Winchester = alive > 0 && empty == alive;
            f.OnStation = OnStation(w, l, step);
            f.TargetsDown = step.Targets != null && step.Targets.Length > 0 && TargetsDown(step.Targets);
            switch (step.Kind)
            {
                case PlanKind.Attack: f.TaskDone = f.TargetsDown; break;
                // Night-2 review: on the lane's own aircraft — home (landed or back in the reserve), or all airborne again.
                case PlanKind.Rtb: f.TaskDone = flying == 0; break;
                // ponytail: a refit is over when every aircraft is airborne with no recovery (one still waiting to start reads as
                // back); a per-member latch if refits end early in game.
                case PlanKind.Refit: f.TaskDone = alive == 0 || back == alive; break;
                case PlanKind.FormUp: f.TaskDone = true; break;
                default: f.TaskDone = completedAt[l] >= sentAt[l] && sentAt[l] >= 0; break;
            }
            // An RTB or REFIT with no aircraft left is done, not blocked.
            if ((step.Kind == PlanKind.Rtb || step.Kind == PlanKind.Refit || step.Kind == PlanKind.FormUp) && alive == 0) alive = 1;
            return f;
        }

        /// <summary>How near a step's point counts as on its area (at least; an area step's own radius when larger).</summary>
        public static float StationMetres = 3000f;

        /// <summary>A lane aircraft is within the step's area (review 2 [6]: a timed ORBIT, CAP or SWEEP counts from here).</summary>
        private bool OnStation(WingService w, int l, PlanStep step)
        {
            if (step.Points == null || step.Points.Length == 0) return false;
            Waypoint p = step.Points[0];
            float r = System.Math.Max(StationMetres, step.Radius);
            foreach (WingMember m in w.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null || !laneIds[l].Contains(m.Aircraft.persistentID.Id)) continue;
                float dx = m.Last.Pos.X - p.X, dz = m.Last.Pos.Z - p.Z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        private static bool TargetsDown(uint[] ids)
        {
            foreach (uint id in ids)
                if (new PersistentID { Id = id }.TryGetUnit(out Unit u) && u != null && !u.disabled) return false;
            return true;
        }
    }
}
