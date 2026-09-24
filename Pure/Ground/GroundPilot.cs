using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum GroundPhase : byte { Parked, TaxiOut, HoldShort, LineUp, Roll, ClimbOut, LiftOff, Done, Aborted, TaxiIn, Stand }

    /// <summary>What holds a taxiing member where it stops (diagnostics).</summary>
    internal enum GroundStop : byte { None, Claims, Member, Foreign, Blocked, Goal, Relocating, PulledAside }

    /// <summary>One member from its spawn on a field until it is airborne and the formation pilot takes over (spec M3 §3).
    /// <list type="bullet">
    /// <item>Parked: brakes for <see cref="ParkedSeconds"/>, then routes to the departure hold-short.</item>
    /// <item>TaxiOut / HoldShort: pure pursuit along the route, claiming <see cref="ClaimAhead"/> of it; it stops where its
    /// claims end, <see cref="FollowGap"/> short of another member and <see cref="ObstacleGap"/> short of a foreign
    /// aircraft within <see cref="ObstacleCorridor"/> of its path or ahead of its nose (whatever edges they hold). Two
    /// members stopped for each other: the lower id goes first when its path passes the other with its span plus
    /// <see cref="PassMargin"/> to spare; when it cannot, neither wait counts as legitimate (the watchdog acts). On
    /// the last edge, or <see cref="QueueReach"/> from the hold-short along its route (a queue longer than the last
    /// edge), it joins the departure queue (HoldShort) and creeps to the hold-short node.</item>
    /// <item>LineUp: once the sequencer lets it, from the hold-short across the threshold to its row and lane, aligned
    /// over the last <see cref="LineupRunIn"/>; it releases its taxi claims once clear of the hold-short and tells the
    /// sequencer when it is past the threshold. Stopped on its slot it is lined up when within
    /// <see cref="LineupAlignDeg"/> of the runway heading or after <see cref="AlignSettleSeconds"/> (the roll steers the
    /// rest).</item>
    /// <item>Roll: on the sequencer's word, full throttle along its lane to the takeoff speed; braked while a foreign
    /// aircraft is in the lane ahead.</item>
    /// <item>Aborted: not lined up within <see cref="LineUpSeconds"/>, or not at takeoff speed
    /// <see cref="RollSeconds"/> after starting the roll, it leaves the field (the runway lock goes with it) and the
    /// engine returns it to the reserve.</item>
    /// <item>ClimbOut: the flight pipeline flies the runway's extended centreline climbing to
    /// <see cref="ClimbOutAboveRunway"/>; done at <see cref="ClimbOutHeight"/> radar altitude or after
    /// <see cref="ClimbOutSeconds"/>.</item>
    /// <item>Helicopters and tiltwings lift off in place (LiftOff), hover-taxiing out first when a roof is overhead; a
    /// helicopter first spools its rotor to <see cref="SpoolRpm"/> at <see cref="SpoolCollective"/> (at most
    /// <see cref="SpoolSeconds"/>).</item>
    /// <item>TaxiIn (spec M3 §4): after a landing, or recalled before lining up, to the nearest free stand (a service
    /// point, else a hangar exit, else the node nearest the field centre), from the runway exit ahead when on a runway;
    /// it claims with Landing priority while on a runway, TaxiIn off it; the same taxiing rules apply. Stand: stopped
    /// there, holding the node, until it departs again (<see cref="Depart"/>) or leaves the field.</item>
    /// <item>Routes avoid blocked edges and edges in use the other way (<see cref="OppositeCost"/>) when they can. A
    /// blocked edge within its claims, a deadlock naming it the victim, or the watchdog (no progress) reroutes it: short
    /// of the edge to avoid, it keeps its route and claims up to the node before it and takes another way from there;
    /// on that edge, it turns back to that node when nobody follows it. With no other way it keeps its route (a victim
    /// tells the field, which picks another) — unless it can pull aside: onto an edge at the node it is at (or just
    /// passed, when nobody follows it) that the member it waits for will not use, <see cref="PullAsideMetres"/> in,
    /// where it waits until that member has passed the node (at most <see cref="PullAsideSeconds"/>, which the
    /// watchdog counts), then carries on. The watchdog then relocates it once to the hold-short, as soon as the
    /// hold-short is free (no claim, nobody within <see cref="RelocateClearRadius"/>) and claimed; it keeps trying to
    /// taxi meanwhile, and a member that gets going again (<see cref="RelocateCancelMetres"/>) is not moved.</item>
    /// </list></summary>
    internal sealed class GroundPilot
    {
        public static float ParkedSeconds = 2f, ClaimAhead = 80f, NodeClearRadius = 15f, FollowGap = 40f, QueueReach = 400f;
        public static float ObstacleCorridor = 15f, ObstacleGap = 30f, ObstacleLookahead = 150f, PassMargin = 2f;
        public static float ThresholdEntry = 10f, ClearedPastThreshold = 30f, LineupRunIn = 25f, AlignTail = 80f;
        public static float LineupTolerance = 3f, LineupAlignDeg = 10f, StoppedSpeed = 0.5f;
        public static float LiftOffHeight = 30f, HoverExitHeight = 3f, HoverExitReached = 5f;
        public static float ClimbOutHeight = 150f, ClimbOutSeconds = 30f, ClimbOutAboveRunway = 300f, ClimbOutSpeedFactor = 1.3f;
        /// <summary>The takeoff roll rotates from <see cref="RotateFraction"/> × the takeoff speed toward
        /// <see cref="RotatePitchDeg"/> nose-up (pitch stick <see cref="RotateGain"/> per degree short), as the game's own
        /// takeoff aims up from 0.7 × takeoff speed; climb-out begins only above <see cref="WheelsOffHeight"/> radar altitude
        /// (the fly-by-wire is off on the wheels) and holds full throttle, as the game's does.</summary>
        public static float RotateFraction = 0.7f, RotatePitchDeg = 10f, RotateGain = 0.08f, WheelsOffHeight = 1.5f;
        public static float RerouteCost = 1e4f, OppositeCost = 1000f, RelocateClearRadius = 30f, RelocateCancelMetres = 10f;
        public static float AlignSettleSeconds = 5f, LineUpSeconds = 120f, RollSeconds = 60f;
        public static float PullAsideMetres = 45f, PullAsideSeconds = 90f;
        public static float SpoolRpm = 0.9f, SpoolCollective = 0.05f, SpoolSeconds = 30f;

        public readonly int Owner;
        public readonly StuckWatchdog Watchdog = new StuckWatchdog();
        public GroundPhase Phase { get; private set; } = GroundPhase.Parked;
        public bool Done => Phase == GroundPhase.Done;
        public FieldTraffic Field => traffic;
        /// <summary>While taxiing: what its stop point is set by, and whose (a member or a claim holder, −1: none).</summary>
        public GroundStop Stop { get; private set; }
        public int StopWho { get; private set; } = -1;
        /// <summary>Set by the engine at spawn: a structure overhead (a helicopter must leave the hangar first).</summary>
        public bool RoofOverhead;
        public ControlOutput LastOutput;

        private readonly FieldTraffic traffic;
        private readonly AirframeClass cls;
        private Pose spawn, lastPose;
        private int hangar, startNode, standNode = -1;
        private bool arriving, pulledAside;
        private int asideFor = -1, asideNode = -1, asideBack = -1;
        private float asideSince;
        private readonly GroundController controller = new GroundController();
        private readonly List<int> nodes = new List<int>(), edges = new List<int>();
        private readonly List<int> nodePathIndex = new List<int>();
        private readonly int[] holdClaim = new int[1], noEdges = new int[0];
        private readonly List<int> candidateNodes = new List<int>(), candidateEdges = new List<int>();
        private int waitEdge = -1, waitNode = -1, deadEndEdge = -1, backEdge = -1;
        private Vec3[] path = new Vec3[0];
        private float[] cum = new float[0];
        private int progress;
        private float phaseStart = float.NaN, liftoffTime = float.NaN, settleStart = float.NaN, rollStart = float.NaN;
        private int restands;
        private bool enqueued, taxiReleased, clearedThreshold, rolling, airborneReported, exitedHangar, relocationPending, relocationWanted;
        private Pose relocation;
        private Vec3 relocateFrom;
        private int lineupSlotIndex;

        /// <summary>A member launched from hangar <paramref name="hangarIndex"/> (−1: none) joins the taxi graph at that
        /// hangar's exit, else at <paramref name="startNode"/> (a service spot's), else at the node nearest to it.</summary>
        public GroundPilot(int owner, FieldTraffic field, AirframeClass cls, Pose spawn, int hangarIndex, int startNode = -1)
        {
            Owner = owner;
            traffic = field;
            this.cls = cls;
            this.spawn = spawn;
            hangar = hangarIndex;
            this.startNode = startNode;
            lastPose = spawn;
        }

        /// <summary>A fallback stand keeps this far outside every runway's edges; a stand found taken on the way is given up
        /// for another at most <see cref="MaxRestands"/> times.</summary>
        public static float StandRunwayMargin = 30f;
        public static int MaxRestands = 2;

        /// <summary>The stand it taxis to or stands on (−1: none, or standing where it stopped).</summary>
        public int StandNode => standNode;

        /// <summary>A helicopter launched under a roof has hovered out of the hangar door.</summary>
        public bool ExitedHangar => exitedHangar;

        /// <summary>Arriving and still on a runway (it keeps the field's departure runway busy, review M3b I3).</summary>
        public bool ArrivingOnRunway => arriving && Phase == GroundPhase.TaxiIn && OnRunway(lastPose.Pos, 0f);

        private int StartNode(Vec3 pos) =>
            hangar >= 0 ? traffic.Graph.HangarExit(hangar) : startNode >= 0 ? startNode : traffic.Graph.NearestNode(pos);

        private bool Vertical => cls != AirframeClass.FixedWing;

        /// <summary>Where the route ends: the departure hold-short, or the stand when arriving.</summary>
        private int Goal => arriving ? standNode : traffic.Graph.HoldShort(traffic.RunwayIndex, traffic.Reverse);

        /// <summary>Turns the member round for a stand (spec M3 §4): after a landing (a new pilot) or while parked or
        /// taxiing out; it leaves the departure. False when it is past that (lining up, rolling, airborne) or the field
        /// has no stand for it.</summary>
        public bool TaxiIn(in AircraftState s, float time, WingEventRing events, int slot)
        {
            if (Phase == GroundPhase.Stand || Phase == GroundPhase.TaxiIn) return true;
            if (Vertical || (Phase != GroundPhase.Parked && Phase != GroundPhase.TaxiOut && Phase != GroundPhase.HoldShort)) return false;
            int stand = ChooseStand(s.Pos);
            if (stand < 0) return false;
            traffic.Departures.Remove(Owner);
            traffic.Reservations.ReleaseAll(Owner);
            traffic.TakeStand(Owner, stand);
            arriving = true;
            standNode = stand;
            enqueued = relocationWanted = false;
            restands = 0;
            backEdge = deadEndEdge = -1;
            RouteFrom(new[] { s.Pos }, ArrivalStart(s));
            Watchdog.Rearm(s.Pos);
            Enter(GroundPhase.TaxiIn, time);
            Log(events, time, slot, WingEventKind.Taxiing);
            return true;
        }

        /// <summary>Stands where it is (a helicopter down on a pad, or a jet with no stand to taxi to).</summary>
        public void StandHere(Pose at, float time)
        {
            traffic.Departures.Remove(Owner);
            lastPose = at;
            arriving = true;
            standNode = -1;
            Enter(GroundPhase.Stand, time);
        }

        /// <summary>From its stand, out again: parked, then the departure as from a spawn (the engine expects it at the
        /// field's departures).</summary>
        public void Depart(float time)
        {
            if (Phase != GroundPhase.Stand) return;
            traffic.LeaveStand(Owner);
            arriving = false;
            startNode = standNode;
            standNode = hangar = -1;
            spawn = lastPose;
            RoofOverhead = false;
            enqueued = taxiReleased = clearedThreshold = rolling = airborneReported = exitedHangar = false;
            liftoffTime = settleStart = rollStart = float.NaN;
            Phase = GroundPhase.Parked;
            phaseStart = float.NaN;
        }

        /// <summary>The nearest stand nobody else has (other than <paramref name="except"/>): service points, else hangar
        /// exits, else a taxiway node at least <see cref="StandRunwayMargin"/> off every runway — a dead end (parking)
        /// first. −1 when there is none.</summary>
        private int ChooseStand(Vec3 pos, int except = -1)
        {
            TaxiGraph g = traffic.Graph;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < traffic.Field.ServicePoints.Length; i++) ConsiderStand(g.ServiceNode(i), except, pos, ref best, ref bestD);
            if (best < 0)
                for (int h = 0; h < traffic.Field.Hangars.Length; h++) ConsiderStand(g.HangarExit(h), except, pos, ref best, ref bestD);
            if (best >= 0) return best;
            int end = -1;
            float endD = float.MaxValue;
            for (int n = 0; n < g.NodeCount; n++)
            {
                if (g.Kind(n) != NodeKind.Road || OnRunway(g.NodePos(n), StandRunwayMargin)) continue;
                if (g.EdgesOf(n).Count == 1) ConsiderStand(n, except, pos, ref end, ref endD);
                else ConsiderStand(n, except, pos, ref best, ref bestD);
            }
            return end >= 0 ? end : best;
        }

        private void ConsiderStand(int node, int except, Vec3 pos, ref int best, ref float bestD)
        {
            if (node < 0 || node == except || !traffic.StandFree(node, Owner)) return;
            int owner = traffic.Reservations.OwnerOfNode(node);
            Vec3 at = traffic.Graph.NodePos(node);
            if ((owner >= 0 && owner != Owner) || traffic.Occupied(at, ServiceSpots.ClearRadius, Owner)) return;
            float d = (at - pos).Horizontal.Length;
            if (d >= bestD) return;
            bestD = d;
            best = node;
        }

        private bool OnRunway(Vec3 pos, float margin)
        {
            foreach (RunwaySample r in traffic.Field.Runways)
                if (r.Contains(pos, margin)) return true;
            return false;
        }

        /// <summary>The stand was taken on the way (review M3b I6): another one; else, once off the runway, it stands where
        /// it is (the engine then treats it as on its stand). On a runway it tries again at the watchdog's next cycle.</summary>
        private bool Restand(in AircraftState s, float time, WingEventRing events, int slot)
        {
            relocationWanted = false;
            int next = restands < MaxRestands ? ChooseStand(s.Pos, standNode) : -1;
            if (next >= 0)
            {
                restands++;
                traffic.Reservations.ReleaseAll(Owner);
                traffic.TakeStand(Owner, next);
                standNode = next;
                backEdge = deadEndEdge = -1;
                RouteFrom(new[] { s.Pos }, ArrivalStart(s));
                Watchdog.Rearm(s.Pos);
                Log(events, time, slot, WingEventKind.Taxiing);
                return true;
            }
            if (OnRunway(s.Pos, 0f))
            {
                Watchdog.Rearm(s.Pos);
                return false;
            }
            traffic.Reservations.ReleaseAll(Owner);
            traffic.LeaveStand(Owner);
            holdClaim[0] = traffic.Graph.NearestNode(s.Pos);
            traffic.Reservations.TryAdvance(Owner, TaxiPriority.TaxiIn, holdClaim, noEdges, 0, 0);
            StandHere(new Pose(s.Pos, s.Fwd), time);
            Log(events, time, slot, WingEventKind.Parked);
            return true;
        }

        /// <summary>On a runway: the nearest runway exit ahead (else the nearest one); elsewhere the nearest node.</summary>
        private int ArrivalStart(in AircraftState s)
        {
            TaxiGraph g = traffic.Graph;
            bool onRunway = false;
            foreach (RunwaySample r in traffic.Field.Runways) onRunway |= r.Contains(s.Pos, 0f);
            if (!onRunway || g.RunwayExits.Count == 0) return g.NearestNode(s.Pos);
            Vec3 fwd = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            int ahead = -1, any = -1;
            float aheadD = float.MaxValue, anyD = float.MaxValue;
            foreach (int n in g.RunwayExits)
            {
                Vec3 d = (g.NodePos(n) - s.Pos).Horizontal;
                float length = d.Length;
                if (length < anyD)
                {
                    anyD = length;
                    any = n;
                }
                if (Vec3.Dot(d, fwd) > 0f && length < aheadD)
                {
                    aheadD = length;
                    ahead = n;
                }
            }
            return ahead >= 0 ? ahead : any;
        }

        private TaxiPriority Priority(Vec3 pos)
        {
            if (!arriving) return TaxiPriority.Departing;
            foreach (RunwaySample r in traffic.Field.Runways)
                if (r.Contains(pos, 0f)) return TaxiPriority.Landing;
            return TaxiPriority.TaxiIn;
        }

        public ControlOutput Step(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            traffic.Report(Owner, s.Pos);
            lastPose = new Pose(s.Pos, s.Fwd);
            traffic.ReportArriving(Owner, ArrivingOnRunway);
            ControlOutput o;
            switch (Phase)
            {
                case GroundPhase.Parked:
                    // A helicopter spools its rotor up at low collective first, as the game's own takeoff does: full
                    // collective on a stopped rotor keeps it from ever reaching speed.
                    o = new ControlOutput { Brake = 1f, Throttle = Vertical ? SpoolCollective : 0f };
                    if (float.IsNaN(phaseStart)) phaseStart = time;
                    bool spooled = cls != AirframeClass.Rotary || s.RotorRpm >= SpoolRpm || time - phaseStart >= SpoolSeconds;
                    if (time - phaseStart >= ParkedSeconds && (!Vertical || spooled))
                    {
                        if (Vertical)
                        {
                            pipeline.Track(s, LastOutput, p);
                            Enter(GroundPhase.LiftOff, time);
                        }
                        else
                        {
                            RouteFrom(new[] { s.Pos }, StartNode(s.Pos));
                            Enter(GroundPhase.TaxiOut, time);
                            Log(events, time, slot, WingEventKind.Taxiing);
                        }
                    }
                    break;
                case GroundPhase.TaxiOut:
                case GroundPhase.HoldShort:
                case GroundPhase.TaxiIn:
                    o = pulledAside ? Aside(s, p, time, dt) : Taxi(s, p, time, dt, events, slot);
                    break;
                case GroundPhase.Stand:
                    o = new ControlOutput { Brake = 1f };
                    break;
                case GroundPhase.LineUp:
                    o = LineUp(s, p, time, dt, events, slot);
                    break;
                case GroundPhase.Roll:
                    o = Roll(s, p, pipeline, time, events, slot);
                    break;
                case GroundPhase.ClimbOut:
                    o = ClimbOut(s, p, pipeline, time, dt, events, slot);
                    break;
                case GroundPhase.LiftOff:
                    o = LiftOff(s, p, pipeline, time, dt, events, slot);
                    break;
                case GroundPhase.Aborted:
                    o = new ControlOutput { Brake = 1f };
                    break;
                default:
                    o = LastOutput;
                    break;
            }
            LastOutput = o;
            return o;
        }

        /// <summary>A pending relocation (the engine moves the aircraft there, once).</summary>
        public bool TakeRelocation(out Pose pose)
        {
            pose = relocation;
            if (!relocationPending) return false;
            relocationPending = false;
            return true;
        }

        /// <summary>A member released now goes back to the reserve rather than to the game's AI (spec M3 §3.6): it is
        /// still on the surface (parked, taxiing, lining up, rolling) or a helicopter below half the lift-off height. The
        /// game's combat AI ejects a pilot that sits still on the ground.</summary>
        public bool DespawnOnRelease(in AircraftState s) =>
            Phase <= GroundPhase.Roll || Phase == GroundPhase.Aborted || Phase == GroundPhase.TaxiIn || Phase == GroundPhase.Stand ||
            (Phase == GroundPhase.LiftOff && s.RadarAlt < 0.5f * LiftOffHeight);

        /// <summary>Off the field for good (airborne, dead, released).</summary>
        public void Leave() => traffic.Leave(Owner);

        private ControlOutput Taxi(in AircraftState s, AirframeProfile p, float time, float dt, WingEventRing events, int slot)
        {
            if (relocationWanted)
            {
                if ((s.Pos - relocateFrom).Horizontal.Length > RelocateCancelMetres) relocationWanted = false;
                else if (Relocate(s, time, events, slot)) return new ControlOutput { Brake = 1f };
            }
            float along = Along(s.Pos);
            int step = StepAt(along);
            ReleaseBehind(along, step);
            traffic.ReportRoute(Owner, nodes, edges, step);

            float total = cum[cum.Length - 1];
            float stopAt = total;
            GroundStop binding = GroundStop.None;
            bool reservationWait = false;
            float obstacle = ObstacleStop(along, s, p, out int member);
            if (edges.Count > 0)
            {
                int steps = 1;
                while (step + steps < edges.Count && NodeAt(step + steps) < along + ClaimAhead) steps++;
                if (member >= 0)
                {
                    // Never claim past a member ahead on the path: what lies beyond it is its to take first.
                    int limit = 0;
                    while (step + limit < edges.Count && NodeAt(step + limit + 1) < obstacle + FollowGap) limit++;
                    steps = Math.Min(steps, limit);
                }
                for (int k = step; k < edges.Count; k++)
                {
                    if (!traffic.Reservations.Blocked(edges[k])) continue;
                    // A wreck or a stuck aircraft anywhere ahead: find another way now, while the nodes before it are
                    // still ahead to turn off at, rather than wait for it.
                    if (edges[k] != deadEndEdge)
                    {
                        if (Reroute(s.Pos, k, -1, along, events, time, slot)) return new ControlOutput { Brake = 1f };
                        deadEndEdge = edges[k];
                    }
                    steps = Math.Min(steps, k - step);
                    stopAt = NodeAt(k);
                    binding = GroundStop.Blocked;
                    break;
                }
                // The last step's far node only once it is within the claim window (a long edge's far end is not held
                // from the other end of it).
                bool lastNode = NodeAt(step + steps) < along + ClaimAhead;
                int items = steps > 0
                    ? traffic.Reservations.TryAdvance(Owner, Priority(s.Pos), nodes, edges, step, steps,
                        along <= NodeAt(0) + NodeClearRadius, lastNode)
                    : 0;
                bool toGoal = step + steps >= edges.Count && items >= 2 * steps;
                waitEdge = waitNode = -1;
                if (!toGoal && steps > 0)
                {
                    int full = items / 2;
                    waitEdge = step + full;
                    float claimed;
                    if (items % 2 == 1)
                    {
                        waitNode = nodes[step + full + 1];
                        claimed = NodeAt(step + full + 1) - NodeClearRadius;
                    }
                    else if (items == 0 && along < NodeAt(0)) claimed = NodeAt(0) - NodeClearRadius;
                    else claimed = NodeAt(step + full);
                    if (claimed < stopAt)
                    {
                        stopAt = claimed;
                        binding = GroundStop.Claims;
                    }
                    // Waiting for a holder that waits for us (a mixed cycle the deadlock check cannot see) is not
                    // legitimate: the watchdog acts.
                    reservationWait = stopAt - along < 3f && traffic.WaitsFor(traffic.Reservations.WaitingFor(Owner)) != Owner;
                }
            }
            traffic.ReportWait(Owner, obstacle < stopAt ? member : -1);
            if (obstacle < stopAt)
            {
                // Stopped behind a member: the edge it stands on is what a deadlock victim would go round.
                if (member >= 0 && edges.Count > 0)
                {
                    waitEdge = Math.Min(StepAt(obstacle + FollowGap), edges.Count - 1);
                    waitNode = -1;
                }
                stopAt = obstacle;
                binding = member >= 0 ? GroundStop.Member : GroundStop.Foreign;
                // Queued behind another member is a legitimate wait (unless it waits for us too); stuck behind a foreign
                // aircraft is not.
                reservationWait |= member >= 0 && traffic.WaitsFor(member) != Owner && stopAt - along < 3f;
            }

            if (!arriving && !enqueued && (edges.Count == 0 || along >= NodeAt(edges.Count - 1) || total - along <= QueueReach))
            {
                enqueued = true;
                traffic.Departures.Enqueue(Owner, time);
                Enter(GroundPhase.HoldShort, time);
                Log(events, time, slot, WingEventKind.HoldingShort);
            }
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            bool atGoal = total - along < LineupTolerance + 2f && speed < StoppedSpeed;
            Stop = relocationWanted ? GroundStop.Relocating : atGoal ? GroundStop.Goal : binding;
            StopWho = Stop == GroundStop.Member ? member : Stop == GroundStop.Claims ? traffic.Reservations.WaitingForAny(Owner) : -1;
            if (arriving && atGoal)
            {
                // On the stand: only the stand node stays held.
                traffic.Reservations.ReleaseAll(Owner);
                holdClaim[0] = standNode;
                traffic.Reservations.TryAdvance(Owner, TaxiPriority.TaxiIn, holdClaim, noEdges, 0, 0);
                traffic.ReportRoute(Owner, nodes, edges, nodes.Count);
                Enter(GroundPhase.Stand, time);
                Log(events, time, slot, WingEventKind.Parked);
                return new ControlOutput { Brake = 1f };
            }
            if (Phase == GroundPhase.HoldShort && atGoal && traffic.Departures.MayLineUp(Owner, time))
            {
                BeginLineUp(s.Pos, time);
                Log(events, time, slot, WingEventKind.LiningUp);
                return new ControlOutput { Brake = 1f };
            }

            if (traffic.Victim == Owner)
            {
                traffic.ConsumeVictim(Owner);
                if (waitEdge < 0 || !Reroute(s.Pos, waitEdge, waitNode, along, events, time, slot))
                {
                    if (PullAside(s, along, time, events, slot)) return new ControlOutput { Brake = 1f };
                    traffic.NoDetour(Owner);
                }
            }
            switch (Watchdog.Update(s.Pos, reservationWait || atGoal, dt))
            {
                case WatchdogAction.Reroute:
                    Reroute(s.Pos, step, -1, along, events, time, slot);
                    break;
                case WatchdogAction.Relocate:
                    relocationWanted = true;
                    relocateFrom = s.Pos;
                    if (Relocate(s, time, events, slot)) return new ControlOutput { Brake = 1f };
                    break;
            }

            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, stopAt - along);
            return controller.Step(c, s, p, dt);
        }

        /// <summary>A deadlock victim with no other way: off the conflict onto a side edge at the node it is at (or just
        /// passed, turning back when nobody follows it), one the member it waits for will not use.</summary>
        private bool PullAside(in AircraftState s, float along, float time, WingEventRing events, int slot)
        {
            int other = traffic.Reservations.WaitingForAny(Owner);
            if (other < 0 || edges.Count == 0) return false;
            TaxiGraph g = traffic.Graph;
            int on = StepAt(along);
            bool atNode = along < NodeAt(on) + 0.5f;
            int u = nodes[on];
            if (!atNode && !traffic.Reservations.SoleUser(Owner, edges[on])) return false;
            foreach (int e in g.EdgesOf(u))
            {
                bool ahead = false;
                for (int k = on; k < edges.Count; k++) ahead |= edges[k] == e;
                if (ahead || traffic.RouteUses(other, e) || traffic.Reservations.Blocked(e) || !traffic.Reservations.SoleUser(Owner, e)) continue;
                Vec3 refuge = PointAlong(e, u, PullAsideMetres);
                int back = atNode ? -1 : edges[on], far = atNode ? -1 : nodes[on + 1];
                traffic.Reservations.ReleaseAll(Owner);
                if (back >= 0) traffic.Reservations.TryClaimEdge(Owner, back, far);
                holdClaim[0] = u;
                traffic.Reservations.TryAdvance(Owner, Priority(s.Pos), holdClaim, noEdges, 0, 0);
                traffic.Reservations.TryClaimEdge(Owner, e, u);
                SetPath(atNode ? new[] { s.Pos, refuge } : new[] { s.Pos, g.NodePos(u), refuge });
                traffic.ReportWait(Owner, -1);
                pulledAside = true;
                asideFor = other;
                asideNode = u;
                asideBack = back;
                asideSince = time;
                Log(events, time, slot, WingEventKind.PulledAside);
                return true;
            }
            return false;
        }

        /// <summary>Pulled aside: to the refuge and stopped there, letting go of the node once clear of it; back on the
        /// route when the other member has passed the node (or after <see cref="PullAsideSeconds"/>).</summary>
        private ControlOutput Aside(in AircraftState s, AirframeProfile p, float time, float dt)
        {
            Stop = GroundStop.PulledAside;
            StopWho = asideFor;
            // Clear of the node on the refuge side: the node and the way back to it are the other's now.
            float fromNode = (s.Pos - traffic.Graph.NodePos(asideNode)).Horizontal.Length;
            if (progress >= path.Length - 2 && fromNode > NodeClearRadius + 5f)
            {
                traffic.Reservations.ReleaseNode(Owner, asideNode);
                if (asideBack >= 0) traffic.Reservations.ReleaseEdge(Owner, asideBack);
                asideBack = -1;
            }
            bool passed = !traffic.RouteAhead(asideFor, asideNode) || !traffic.Positions.ContainsKey(asideFor);
            if (passed || time - asideSince > PullAsideSeconds)
            {
                pulledAside = false;
                traffic.Reservations.ReleaseAll(Owner);
                RouteFrom(new[] { s.Pos }, asideNode);
                return new ControlOutput { Brake = 1f };
            }
            if (Watchdog.Update(s.Pos, false, dt) == WatchdogAction.Relocate)
            {
                pulledAside = false;
                relocationWanted = true;
                relocateFrom = s.Pos;
            }
            float along = Along(s.Pos);
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, cum[cum.Length - 1] - along);
            return controller.Step(c, s, p, dt);
        }

        /// <summary>The point <paramref name="metres"/> along edge <paramref name="e"/> from its end <paramref name="from"/>
        /// (at most half the edge).</summary>
        private Vec3 PointAlong(int e, int from, float metres)
        {
            TaxiGraph g = traffic.Graph;
            Vec3[] pts = g.EdgePoints(e);
            bool forward = g.EdgeFrom(e) == from;
            float left = Math.Min(metres, 0.5f * g.EdgeLength(e));
            for (int i = 1; i < pts.Length; i++)
            {
                Vec3 a = forward ? pts[i - 1] : pts[pts.Length - i], b = forward ? pts[i] : pts[pts.Length - 1 - i];
                float segment = (b - a).Horizontal.Length;
                if (segment >= left || i == pts.Length - 1) return a + (b - a) * (segment > 1e-4f ? Math.Min(1f, left / segment) : 0f);
                left -= segment;
            }
            return g.NodePos(from);
        }

        private void BeginLineUp(Vec3 pos, float time)
        {
            RunwaySample r = traffic.Runway;
            Vec3 dir = r.Direction(traffic.Reverse);
            Vec3 threshold = traffic.Reverse ? r.End : r.Start;
            traffic.Departures.SlotOf(Owner, out int row, out int column);
            Vec3 lane = LineupPlanner.Slot(r, traffic.Reverse, row, column, traffic.Departures.Abreast, traffic.Departures.Rows);
            SetPath(new[] { pos, threshold + dir * ThresholdEntry, lane - dir * LineupRunIn, lane, lane + dir * AlignTail });
            lineupSlotIndex = 3;
            taxiReleased = clearedThreshold = false;
            settleStart = float.NaN;
            Enter(GroundPhase.LineUp, time);
        }

        private ControlOutput LineUp(in AircraftState s, AirframeProfile p, float time, float dt, WingEventRing events, int slot)
        {
            if (time - phaseStart > LineUpSeconds) return Abort(time, events, slot);
            float along = Along(s.Pos);
            if (!taxiReleased && along > NodeClearRadius)
            {
                taxiReleased = true;
                traffic.Reservations.ReleaseAll(Owner);
            }
            if (!clearedThreshold && along > cum[1] + ClearedPastThreshold)
            {
                clearedThreshold = true;
                traffic.Departures.ClearedThreshold(Owner);
            }
            float toSlot = cum[lineupSlotIndex] - along;
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            Vec3 dir = traffic.Runway.Direction(traffic.Reverse);
            float heading = Math.Abs(Scalar.Wrap180(Vec3.HeadingDeg(s.Fwd) - Vec3.HeadingDeg(dir)));
            bool stoppedOnSlot = toSlot < LineupTolerance && speed < StoppedSpeed;
            if (!stoppedOnSlot) settleStart = float.NaN;
            else if (float.IsNaN(settleStart)) settleStart = time;
            if (stoppedOnSlot && (heading < LineupAlignDeg || time - settleStart >= AlignSettleSeconds))
            {
                if (!clearedThreshold)
                {
                    clearedThreshold = true;
                    traffic.Departures.ClearedThreshold(Owner);
                }
                traffic.Departures.LinedUp(Owner);
                Enter(GroundPhase.Roll, time);
                return new ControlOutput { Brake = 1f };
            }
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, toSlot);
            return controller.Step(c, s, p, dt);
        }

        private ControlOutput Roll(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, WingEventRing events, int slot)
        {
            if (!rolling)
            {
                if (!traffic.Departures.MayRoll(Owner, time)) return new ControlOutput { Brake = 1f };
                rolling = true;
                rollStart = time;
                Vec3 dir = traffic.Runway.Direction(traffic.Reverse);
                SetPath(new[] { s.Pos, s.Pos + dir * Math.Max(500f, traffic.Runway.Length) });
                Log(events, time, slot, WingEventKind.Rolling);
            }
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, 1e6f);
            ControlOutput o = controller.Step(c, s, p, 0f);
            // Anything foreign in the lane ahead (the whole runway: one long segment): hold or reject the takeoff.
            float along = Along(s.Pos);
            bool blocked = false;
            foreach (Vec3 obstacle in traffic.Obstacles) blocked |= OnPathAt(obstacle, along, s, out _) < float.MaxValue;
            o.Throttle = blocked ? 0f : 1f;
            o.Brake = blocked ? 1f : 0f;
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            if (!blocked && speed >= RotateFraction * p.TakeoffSpeed)
                o.Pitch = Scalar.Clamp((RotatePitchDeg - s.PitchDeg) * RotateGain, 0f, 1f);
            if (s.RadarAlt > WheelsOffHeight)
            {
                pipeline.Track(s, o, p);
                liftoffTime = time;
                Enter(GroundPhase.ClimbOut, time);
                return o;
            }
            // Still on the wheels this long: reject the takeoff rather than report a jet in the grass as airborne.
            if (time - rollStart > RollSeconds) return Abort(time, events, slot);
            return o;
        }

        private ControlOutput ClimbOut(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            RunwaySample r = traffic.Runway;
            Vec3 dir = r.Direction(traffic.Reverse);
            Vec3 threshold = traffic.Reverse ? r.End : r.Start;
            float along = Vec3.Dot(s.Pos - threshold, dir);
            Vec3 line = threshold + dir * along;
            var intent = new FlightIntent
            {
                Ref = new RefState(new Vec3(line.X, threshold.Y + ClimbOutAboveRunway, line.Z), dir * (ClimbOutSpeedFactor * p.TakeoffSpeed), Vec3.Zero),
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, true, false),
                Precision = 1f,
                Aggression = 0.3f,
                HasHeading = true,
                HeadingDeg = Vec3.HeadingDeg(dir),
            };
            GuidanceCommand g = pipeline.Guide(intent, s, p);
            ControlOutput o = pipeline.Step(g, s, new LimitContext { FloorY = float.NaN, Aggression = 0.3f }, p, dt);
            o.Throttle = 1f;
            o.Airbrake = false;
            if (!airborneReported && s.RadarAlt > DepartureSequencer.ClearHeight)
            {
                airborneReported = true;
                traffic.Departures.Airborne(Owner);
                Log(events, time, slot, WingEventKind.Airborne);
            }
            if (s.RadarAlt >= ClimbOutHeight || time - liftoffTime > ClimbOutSeconds) Finish(time, events, slot);
            return o;
        }

        private ControlOutput LiftOff(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            Vec3 fwd = spawn.Fwd.Horizontal.Normalized;
            Vec3 exit = spawn.Pos + fwd * TaxiGraph.HangarExitDistance;
            if (RoofOverhead && !exitedHangar && (s.Pos - exit).Horizontal.Length < HoverExitReached) exitedHangar = true;
            bool outside = !RoofOverhead || exitedHangar;
            Vec3 over = RoofOverhead ? exit : spawn.Pos;
            Vec3 target = new Vec3(over.X, spawn.Pos.Y + (outside ? LiftOffHeight : HoverExitHeight), over.Z);
            var intent = new FlightIntent
            {
                Ref = new RefState(target, Vec3.Zero, Vec3.Zero),
                Limits = new SpeedLimits(0f, p.CruiseSpeed, false, true),
                Precision = 1f,
                Aggression = 0.3f,
                HasHeading = true,
                HeadingDeg = Vec3.HeadingDeg(fwd),
            };
            GuidanceCommand g = pipeline.Guide(intent, s, p);
            ControlOutput o = pipeline.Step(g, s, new LimitContext { FloorY = float.NaN, Aggression = 0.3f }, p, dt);
            if (outside && s.RadarAlt >= LiftOffHeight - 3f)
            {
                Log(events, time, slot, WingEventKind.Airborne);
                Finish(time, events, slot);
            }
            return o;
        }

        /// <summary>Gives up the departure: off the field (claims, queue place, runway lock), braked, for the engine to
        /// return it to the reserve.</summary>
        private ControlOutput Abort(float time, WingEventRing events, int slot)
        {
            traffic.Leave(Owner);
            Enter(GroundPhase.Aborted, time);
            Log(events, time, slot, WingEventKind.DepartureAborted);
            return new ControlOutput { Brake = 1f };
        }

        private void Finish(float time, WingEventRing events, int slot)
        {
            if (!airborneReported && !Vertical) traffic.Departures.Airborne(Owner);
            airborneReported = true;
            traffic.Leave(Owner);
            Enter(GroundPhase.Done, time);
        }

        /// <summary>Another way round route edge <paramref name="k"/> (and node <paramref name="avoidNode"/> when ≥ 0):
        /// from the furthest node still ahead, back to the nearest, the first with a way round keeps the route and its
        /// claims up to that node and takes the new way from there; with none, a member on an edge it has to itself turns
        /// back to the edge's first node. False, with nothing changed, when every way still needs what it avoids or a
        /// blocked edge.</summary>
        private bool Reroute(Vec3 pos, int k, int avoidNode, float along, WingEventRing events, float time, int slot)
        {
            if (edges.Count == 0) return false;
            k = Math.Max(0, Math.Min(k, edges.Count - 1));
            int avoidEdge = edges[k];
            for (int j = k; j >= 0 && along < NodeAt(j) + 0.5f; j--)
            {
                if (!TryRoute(nodes[j], j, avoidEdge, avoidNode, -1)) continue;
                Divert(j);
                Log(events, time, slot, WingEventKind.Rerouted);
                return true;
            }
            int on = StepAt(along);
            int behind = nodes[on], owner = traffic.Reservations.OwnerOfNode(behind);
            if (along < NodeAt(on) + 0.5f || (owner >= 0 && owner != Owner) || !traffic.Reservations.SoleUser(Owner, edges[on]) ||
                !TryRoute(behind, 0, avoidEdge, avoidNode, edges[on])) return false;
            // Turn back along the edge to its first node, holding the edge and that node for the way back.
            int back = edges[on], far = nodes[on + 1];
            traffic.Reservations.ReleaseAll(Owner);
            backEdge = traffic.Reservations.TryClaimEdge(Owner, back, far) ? back : -1;
            nodes.Clear();
            nodes.AddRange(candidateNodes);
            edges.Clear();
            edges.AddRange(candidateEdges);
            traffic.Reservations.TryAdvance(Owner, TaxiPriority.Departing, nodes, edges, 0, 0);
            SetRoutePath(new[] { pos });
            deadEndEdge = -1;
            Log(events, time, slot, WingEventKind.Rerouted);
            return true;
        }

        /// <summary>A route from <paramref name="start"/> to the hold-short into the candidate lists that uses none of: the
        /// first <paramref name="keptEdges"/> route edges, <paramref name="avoidEdge"/>, <paramref name="alsoAvoid"/>, an
        /// edge touching <paramref name="avoidNode"/>, a blocked edge.</summary>
        private bool TryRoute(int start, int keptEdges, int avoidEdge, int avoidNode, int alsoAvoid)
        {
            TaxiGraph g = traffic.Graph;
            Func<int, bool> avoid = e => e == avoidEdge || e == alsoAvoid || traffic.Reservations.Blocked(e) || Kept(e, keptEdges) ||
                                         (avoidNode >= 0 && (g.EdgeFrom(e) == avoidNode || g.EdgeTo(e) == avoidNode));
            if (!TaxiRouter.Route(g, start, Goal, (e, from) => RouteCost(e, from) + (avoid(e) ? RerouteCost : 0f),
                    candidateNodes, candidateEdges)) return false;
            foreach (int e in candidateEdges)
                if (avoid(e)) return false;
            return true;
        }

        /// <summary>Keeps the route to node <paramref name="j"/> (and its claims) and continues along the candidate route;
        /// only what lay beyond node j is let go.</summary>
        private void Divert(int j)
        {
            TaxiGraph g = traffic.Graph;
            for (int i = j; i < edges.Count; i++) traffic.Reservations.ReleaseEdge(Owner, edges[i]);
            for (int i = j + 1; i < nodes.Count; i++) traffic.Reservations.ReleaseNode(Owner, nodes[i]);
            Vec3[] tail = g.PathPoints(candidateNodes, candidateEdges);
            int keep = nodePathIndex[j] + 1;
            var points = new Vec3[keep + tail.Length - 1];
            Array.Copy(path, points, keep);
            Array.Copy(tail, 1, points, keep, tail.Length - 1);
            nodes.RemoveRange(j + 1, nodes.Count - j - 1);
            for (int i = 1; i < candidateNodes.Count; i++) nodes.Add(candidateNodes[i]);
            edges.RemoveRange(j, edges.Count - j);
            edges.AddRange(candidateEdges);
            nodePathIndex.RemoveRange(j + 1, nodePathIndex.Count - j - 1);
            int index = nodePathIndex[j];
            foreach (int e in candidateEdges)
            {
                index += g.EdgePoints(e).Length - 1;
                nodePathIndex.Add(index);
            }
            int kept = progress;
            SetPath(points);
            progress = Math.Min(kept, points.Length - 2);
            deadEndEdge = -1;
        }

        private bool Kept(int edge, int keptEdges)
        {
            for (int j = 0; j < keptEdges; j++)
                if (edges[j] == edge) return true;
            return false;
        }

        /// <summary>What an edge costs beyond its length, entered from <paramref name="from"/>: blocked, or in use the
        /// other way.</summary>
        private float RouteCost(int e, int from) =>
            (traffic.Reservations.Blocked(e) ? RerouteCost : 0f) + (traffic.Reservations.Against(e, from) ? OppositeCost : 0f);

        /// <summary>Once, when the goal (hold-short or stand) is free: claimed, then the engine moves the aircraft there.
        /// False while it is not free (an arriving member looks for another stand instead, <see cref="Restand"/>).</summary>
        private bool Relocate(in AircraftState s, float time, WingEventRing events, int slot)
        {
            int hold = Goal;
            Vec3 at = traffic.Graph.NodePos(hold);
            int owner = traffic.Reservations.OwnerOfNode(hold);
            if ((owner >= 0 && owner != Owner) || traffic.Occupied(at, RelocateClearRadius, Owner) ||
                traffic.RelocatedNear(at, RelocateClearRadius, Owner))
                return arriving && Restand(s, time, events, slot);
            traffic.Reservations.ReleaseAll(Owner);
            holdClaim[0] = hold;
            traffic.Reservations.TryAdvance(Owner, arriving ? TaxiPriority.TaxiIn : TaxiPriority.Departing, holdClaim, noEdges, 0, 0);
            if (traffic.Reservations.OwnerOfNode(hold) != Owner) return false;
            relocationWanted = false;
            Vec3 threshold = traffic.Reverse ? traffic.Runway.End : traffic.Runway.Start;
            Vec3 facing = arriving ? lastPose.Fwd.Horizontal : (threshold - at).Horizontal;
            relocation = new Pose(at, facing.SqrLength > 1e-4f ? facing.Normalized : Vec3.Forward);
            relocationPending = true;
            traffic.NoteRelocation(Owner, at);
            backEdge = -1;
            RouteFrom(new[] { at }, hold);
            Watchdog.Restart(at);
            Log(events, time, slot, WingEventKind.Relocated);
            return true;
        }

        /// <summary>The path is <paramref name="prefix"/> (from where the aircraft is) followed by the route from
        /// <paramref name="start"/> to the goal (see <see cref="RouteCost"/>).</summary>
        private void RouteFrom(Vec3[] prefix, int start)
        {
            int goal = Goal;
            if (start < 0 || goal < 0 || !TaxiRouter.Route(traffic.Graph, start, goal, RouteCost, nodes, edges))
            {
                nodes.Clear();
                edges.Clear();
                SetPath(new[] { prefix[0], goal >= 0 ? traffic.Graph.NodePos(goal) : prefix[0] + spawn.Fwd * 10f });
                nodePathIndex.Clear();
                return;
            }
            SetRoutePath(prefix);
        }

        /// <summary>The path for the current route: <paramref name="prefix"/>, then the route's points.</summary>
        private void SetRoutePath(Vec3[] prefix)
        {
            Vec3[] pts = traffic.Graph.PathPoints(nodes, edges);
            var full = new Vec3[prefix.Length + pts.Length];
            Array.Copy(prefix, 0, full, 0, prefix.Length);
            Array.Copy(pts, 0, full, prefix.Length, pts.Length);
            SetPath(full);
            nodePathIndex.Clear();
            int index = prefix.Length;
            nodePathIndex.Add(index);
            for (int k = 0; k < edges.Count; k++)
            {
                index += traffic.Graph.EdgePoints(edges[k]).Length - 1;
                nodePathIndex.Add(index);
            }
        }

        private void SetPath(Vec3[] points)
        {
            path = points;
            cum = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cum[i] = cum[i - 1] + (points[i] - points[i - 1]).Horizontal.Length;
            progress = 0;
        }

        private float NodeAt(int k) => k < nodePathIndex.Count ? cum[nodePathIndex[k]] : cum[cum.Length - 1];

        /// <summary>The route step (edge index) at a distance along the path.</summary>
        private int StepAt(float along)
        {
            int step = 0;
            for (int k = 0; k < edges.Count; k++)
                if (NodeAt(k) <= along + 0.5f) step = k;
            return step;
        }

        private void ReleaseBehind(float along, int step)
        {
            if (backEdge >= 0 && along > NodeAt(0) + NodeClearRadius)
            {
                traffic.Reservations.ReleaseEdge(Owner, backEdge);
                backEdge = -1;
            }
            for (int k = 0; k <= step && k < nodes.Count; k++)
                if (along > NodeAt(k) + NodeClearRadius) traffic.Reservations.ReleaseNode(Owner, nodes[k]);
            for (int k = 0; k < step; k++)
                if (along > NodeAt(k + 1) + NodeClearRadius) traffic.Reservations.ReleaseEdge(Owner, edges[k]);
        }

        private float Along(Vec3 pos)
        {
            if (path.Length < 2) return 0f;
            int i = Math.Min(progress, path.Length - 2);
            Vec3 a = path[i].Horizontal, b = path[i + 1].Horizontal, ab = b - a;
            float len = ab.Length;
            float t = len < 1e-4f ? 0f : Scalar.Clamp(Vec3.Dot(pos.Horizontal - a, ab) / len, 0f, len);
            return cum[i] + t;
        }

        /// <summary>Where to stop for another aircraft (a very long distance when none): foreign ones
        /// <see cref="ObstacleGap"/> short, members <see cref="FollowGap"/> short (<paramref name="member"/>: a member
        /// sets it).</summary>
        /// <summary>Where to stop for another aircraft (a very long distance when none): foreign ones
        /// <see cref="ObstacleGap"/> short, members <see cref="FollowGap"/> short (<paramref name="member"/>: the member
        /// that sets it, −1 when none).</summary>
        private float ObstacleStop(float along, in AircraftState s, AirframeProfile p, out int member)
        {
            float stop = float.MaxValue;
            member = -1;
            if (path.Length < 2) return stop;
            foreach (Vec3 o in traffic.Obstacles)
            {
                float at = OnPathAt(o, along, s, out _);
                if (at - ObstacleGap < stop) stop = at - ObstacleGap;
            }
            foreach (KeyValuePair<int, Vec3> m in traffic.Positions)
            {
                if (m.Key == Owner) continue;
                float at = OnPathAt(m.Value, along, s, out float lateral);
                if (at == float.MaxValue) continue;
                // It waits for us: the lower id goes first, when it passes clear of it.
                if (Owner < m.Key && traffic.WaitsFor(m.Key) == Owner && lateral >= p.SpanM + PassMargin) continue;
                if (at - FollowGap >= stop) continue;
                stop = at - FollowGap;
                member = m.Key;
            }
            return stop;
        }

        /// <summary>How far along the path this one would reach an aircraft at <paramref name="o"/> (float.MaxValue: never):
        /// one within <see cref="ObstacleCorridor"/> of the path ahead (up to <see cref="ObstacleLookahead"/>), or of the
        /// line ahead of the nose; <paramref name="lateral"/> is how far it stands off that line.</summary>
        private float OnPathAt(Vec3 o, float along, in AircraftState s, out float lateral)
        {
            float at = float.MaxValue;
            lateral = float.MaxValue;
            Vec3 nose = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            Vec3 d = (o - s.Pos).Horizontal;
            float ahead = Vec3.Dot(d, nose), side = Math.Abs(Vec3.Dot(d, Vec3.Cross(Vec3.Up, nose)));
            if (ahead > 0f && ahead < ObstacleLookahead && side < ObstacleCorridor)
            {
                at = along + ahead;
                lateral = side;
            }
            for (int i = Math.Min(progress, path.Length - 2); i < path.Length - 1 && cum[i] < along + ObstacleLookahead; i++)
            {
                Vec3 a = path[i].Horizontal, ab = path[i + 1].Horizontal - a;
                float len = ab.Length;
                if (len < 1e-4f) continue;
                Vec3 u = ab / len, ao = o.Horizontal - a;
                float t = Vec3.Dot(ao, u), off = (ao - u * t).Length;
                if (t < 0f || t > len || off >= ObstacleCorridor || cum[i] + t <= along) continue;
                at = Math.Min(at, cum[i] + t);
                lateral = Math.Min(lateral, off);
                break;
            }
            return at;
        }

        private void Enter(GroundPhase phase, float time)
        {
            Phase = phase;
            phaseStart = time;
        }

        private void Log(WingEventRing events, float time, int slot, WingEventKind kind) =>
            events?.Push(new WingEvent { Time = time, Member = slot, Kind = kind });
    }
}
