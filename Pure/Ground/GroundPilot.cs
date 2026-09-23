using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum GroundPhase : byte { Parked, TaxiOut, HoldShort, LineUp, Roll, ClimbOut, LiftOff, Done, Aborted }

    /// <summary>One member from its spawn on a field until it is airborne and the formation pilot takes over (spec M3 §3).
    /// <list type="bullet">
    /// <item>Parked: brakes for <see cref="ParkedSeconds"/>, then routes to the departure hold-short.</item>
    /// <item>TaxiOut / HoldShort: pure pursuit along the route, claiming <see cref="ClaimAhead"/> of it; it stops where its
    /// claims end, <see cref="FollowGap"/> behind the member ahead on its edge, and <see cref="ObstacleGap"/> short of a
    /// foreign aircraft within <see cref="ObstacleCorridor"/> of its path. Reaching the last edge it joins the departure
    /// queue (HoldShort) and creeps to the hold-short node.</item>
    /// <item>LineUp: once the sequencer lets it, from the hold-short across the threshold to its row and lane, aligned
    /// over the last <see cref="LineupRunIn"/>; it releases its taxi claims once clear of the hold-short and tells the
    /// sequencer when it is past the threshold. Stopped on its slot it is lined up when within
    /// <see cref="LineupAlignDeg"/> of the runway heading or after <see cref="AlignSettleSeconds"/> (the roll steers the
    /// rest).</item>
    /// <item>Roll: on the sequencer's word, full throttle along its lane to the takeoff speed.</item>
    /// <item>Aborted: not lined up within <see cref="LineUpSeconds"/>, or not at takeoff speed
    /// <see cref="RollSeconds"/> after starting the roll, it leaves the field (the runway lock goes with it) and the
    /// engine returns it to the reserve.</item>
    /// <item>ClimbOut: the flight pipeline flies the runway's extended centreline climbing to
    /// <see cref="ClimbOutAboveRunway"/>; done at <see cref="ClimbOutHeight"/> radar altitude or after
    /// <see cref="ClimbOutSeconds"/>.</item>
    /// <item>Helicopters and tiltwings lift off in place (LiftOff), hover-taxiing out first when a roof is overhead.</item>
    /// <item>Routes avoid blocked edges and edges in use the other way (<see cref="OppositeCost"/>) when they can. A
    /// blocked edge within its claims, a deadlock naming it the victim, or the watchdog (no progress) reroutes it: short
    /// of the edge to avoid, it keeps its route and claims up to the node before it and takes another way from there;
    /// on that edge, it turns back to that node when nobody follows it. With no other way it keeps its route (a victim
    /// tells the field, which picks another). The watchdog then relocates it once to the hold-short, braked until the
    /// hold-short is free (no claim, nobody within <see cref="RelocateClearRadius"/>) and claimed.</item>
    /// </list></summary>
    internal sealed class GroundPilot
    {
        public static float ParkedSeconds = 2f, ClaimAhead = 80f, NodeClearRadius = 15f, FollowGap = 40f;
        public static float ObstacleCorridor = 15f, ObstacleGap = 30f, ObstacleLookahead = 150f;
        public static float ThresholdEntry = 10f, ClearedPastThreshold = 30f, LineupRunIn = 25f, AlignTail = 80f;
        public static float LineupTolerance = 3f, LineupAlignDeg = 10f, StoppedSpeed = 0.5f;
        public static float LiftOffHeight = 30f, HoverExitHeight = 3f, HoverExitReached = 5f;
        public static float ClimbOutHeight = 150f, ClimbOutSeconds = 30f, ClimbOutAboveRunway = 300f, ClimbOutSpeedFactor = 1.3f;
        public static float RerouteCost = 1e4f, OppositeCost = 1000f, RelocateClearRadius = 30f;
        public static float AlignSettleSeconds = 5f, LineUpSeconds = 120f, RollSeconds = 60f;

        public readonly int Owner;
        public readonly StuckWatchdog Watchdog = new StuckWatchdog();
        public GroundPhase Phase { get; private set; } = GroundPhase.Parked;
        public bool Done => Phase == GroundPhase.Done;
        /// <summary>Set by the engine at spawn: a structure overhead (a helicopter must leave the hangar first).</summary>
        public bool RoofOverhead;
        public ControlOutput LastOutput;

        private readonly FieldTraffic field;
        private readonly AirframeClass cls;
        private readonly Pose spawn;
        private readonly int hangar;
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
        private bool enqueued, taxiReleased, clearedThreshold, rolling, airborneReported, exitedHangar, relocationPending, relocationWanted;
        private Pose relocation;
        private int lineupSlotIndex;

        public GroundPilot(int owner, FieldTraffic field, AirframeClass cls, Pose spawn, int hangarIndex)
        {
            Owner = owner;
            this.field = field;
            this.cls = cls;
            this.spawn = spawn;
            hangar = hangarIndex;
        }

        private bool Vertical => cls != AirframeClass.FixedWing;

        public ControlOutput Step(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            field.Report(Owner, s.Pos);
            ControlOutput o;
            switch (Phase)
            {
                case GroundPhase.Parked:
                    o = new ControlOutput { Brake = 1f };
                    if (float.IsNaN(phaseStart)) phaseStart = time;
                    if (time - phaseStart >= ParkedSeconds)
                    {
                        if (Vertical)
                        {
                            pipeline.Track(s, LastOutput, p);
                            Enter(GroundPhase.LiftOff, time);
                        }
                        else
                        {
                            RouteFrom(new[] { s.Pos }, hangar >= 0 ? field.Graph.HangarExit(hangar) : field.Graph.NearestNode(s.Pos));
                            Enter(GroundPhase.TaxiOut, time);
                            Log(events, time, slot, WingEventKind.Taxiing);
                        }
                    }
                    break;
                case GroundPhase.TaxiOut:
                case GroundPhase.HoldShort:
                    o = Taxi(s, p, time, dt, events, slot);
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
            Phase <= GroundPhase.Roll || Phase == GroundPhase.Aborted ||
            (Phase == GroundPhase.LiftOff && s.RadarAlt < 0.5f * LiftOffHeight);

        /// <summary>Off the field for good (airborne, dead, released).</summary>
        public void Leave() => field.Leave(Owner);

        private ControlOutput Taxi(in AircraftState s, AirframeProfile p, float time, float dt, WingEventRing events, int slot)
        {
            if (relocationWanted)
            {
                Relocate(time, events, slot);
                return new ControlOutput { Brake = 1f };
            }
            float along = Along(s.Pos);
            int step = StepAt(along);
            ReleaseBehind(along, step);

            float total = cum[cum.Length - 1];
            float stopAt = total;
            bool reservationWait = false;
            if (edges.Count > 0)
            {
                int steps = 1;
                while (step + steps < edges.Count && NodeAt(step + steps) < along + ClaimAhead) steps++;
                for (int k = step; k < edges.Count; k++)
                {
                    if (!field.Reservations.Blocked(edges[k])) continue;
                    // A wreck or a stuck aircraft anywhere ahead: find another way now, while the nodes before it are
                    // still ahead to turn off at, rather than wait for it.
                    if (edges[k] != deadEndEdge)
                    {
                        if (Reroute(s.Pos, k, -1, along, events, time, slot)) return new ControlOutput { Brake = 1f };
                        deadEndEdge = edges[k];
                    }
                    steps = Math.Min(steps, k - step);
                    stopAt = NodeAt(k);
                    break;
                }
                int items = steps > 0 ? field.Reservations.TryAdvance(Owner, TaxiPriority.Departing, nodes, edges, step, steps) : 0;
                bool toGoal = step + steps >= edges.Count && items >= 2 * steps;
                waitEdge = waitNode = -1;
                if (!toGoal)
                {
                    int full = items / 2;
                    waitEdge = step + full;
                    if (items % 2 == 1)
                    {
                        waitNode = nodes[step + full + 1];
                        stopAt = Math.Min(stopAt, NodeAt(step + full + 1) - NodeClearRadius);
                    }
                    else if (items == 0 && along < NodeAt(0)) stopAt = Math.Min(stopAt, NodeAt(0) - NodeClearRadius);
                    else stopAt = Math.Min(stopAt, NodeAt(step + full));
                    reservationWait = steps > 0 && stopAt - along < 3f;
                }
                int ahead = field.Reservations.Ahead(Owner, edges[Math.Min(step, edges.Count - 1)]);
                if (ahead >= 0 && field.TryGetPosition(ahead, out Vec3 at))
                {
                    float follow = along + (at - s.Pos).Horizontal.Length - FollowGap;
                    if (follow < stopAt)
                    {
                        stopAt = follow;
                        reservationWait = true;
                    }
                }
            }
            stopAt = Math.Min(stopAt, ObstacleStop(along, s));

            if (!enqueued && (edges.Count == 0 || along >= NodeAt(edges.Count - 1)))
            {
                enqueued = true;
                field.Departures.Enqueue(Owner, time);
                Enter(GroundPhase.HoldShort, time);
                Log(events, time, slot, WingEventKind.HoldingShort);
            }
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            bool atGoal = total - along < LineupTolerance + 2f && speed < StoppedSpeed;
            if (Phase == GroundPhase.HoldShort && atGoal && field.Departures.MayLineUp(Owner, time))
            {
                BeginLineUp(s.Pos, time);
                Log(events, time, slot, WingEventKind.LiningUp);
                return new ControlOutput { Brake = 1f };
            }

            if (field.Victim == Owner)
            {
                field.ConsumeVictim(Owner);
                if (waitEdge < 0 || !Reroute(s.Pos, waitEdge, waitNode, along, events, time, slot)) field.NoDetour(Owner);
            }
            switch (Watchdog.Update(s.Pos, reservationWait || atGoal, dt))
            {
                case WatchdogAction.Reroute:
                    Reroute(s.Pos, step, -1, along, events, time, slot);
                    break;
                case WatchdogAction.Relocate:
                    relocationWanted = true;
                    Relocate(time, events, slot);
                    return new ControlOutput { Brake = 1f };
            }

            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, stopAt - along);
            return controller.Step(c, s, p, dt);
        }

        private void BeginLineUp(Vec3 pos, float time)
        {
            RunwaySample r = field.Runway;
            Vec3 dir = r.Direction(field.Reverse);
            Vec3 threshold = field.Reverse ? r.End : r.Start;
            field.Departures.SlotOf(Owner, out int row, out int column);
            Vec3 lane = LineupPlanner.Slot(r, field.Reverse, row, column, field.Departures.Abreast, field.Departures.Rows);
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
                field.Reservations.ReleaseAll(Owner);
            }
            if (!clearedThreshold && along > cum[1] + ClearedPastThreshold)
            {
                clearedThreshold = true;
                field.Departures.ClearedThreshold(Owner);
            }
            float toSlot = cum[lineupSlotIndex] - along;
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            Vec3 dir = field.Runway.Direction(field.Reverse);
            float heading = Math.Abs(Scalar.Wrap180(Vec3.HeadingDeg(s.Fwd) - Vec3.HeadingDeg(dir)));
            bool stoppedOnSlot = toSlot < LineupTolerance && speed < StoppedSpeed;
            if (!stoppedOnSlot) settleStart = float.NaN;
            else if (float.IsNaN(settleStart)) settleStart = time;
            if (stoppedOnSlot && (heading < LineupAlignDeg || time - settleStart >= AlignSettleSeconds))
            {
                if (!clearedThreshold)
                {
                    clearedThreshold = true;
                    field.Departures.ClearedThreshold(Owner);
                }
                field.Departures.LinedUp(Owner);
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
                if (!field.Departures.MayRoll(Owner, time)) return new ControlOutput { Brake = 1f };
                rolling = true;
                rollStart = time;
                Vec3 dir = field.Runway.Direction(field.Reverse);
                SetPath(new[] { s.Pos, s.Pos + dir * Math.Max(500f, field.Runway.Length) });
                Log(events, time, slot, WingEventKind.Rolling);
            }
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, 1e6f);
            ControlOutput o = controller.Step(c, s, p, 0f);
            o.Throttle = 1f;
            o.Brake = 0f;
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
            if (speed < p.TakeoffSpeed && time - rollStart > RollSeconds) return Abort(time, events, slot);
            if (speed >= p.TakeoffSpeed)
            {
                pipeline.Track(s, o, p);
                liftoffTime = time;
                Enter(GroundPhase.ClimbOut, time);
            }
            return o;
        }

        private ControlOutput ClimbOut(in AircraftState s, AirframeProfile p, IFlightPipeline pipeline, float time, float dt,
            WingEventRing events, int slot)
        {
            RunwaySample r = field.Runway;
            Vec3 dir = r.Direction(field.Reverse);
            Vec3 threshold = field.Reverse ? r.End : r.Start;
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
            if (!airborneReported && s.RadarAlt > DepartureSequencer.ClearHeight)
            {
                airborneReported = true;
                field.Departures.Airborne(Owner);
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
            field.Leave(Owner);
            Enter(GroundPhase.Aborted, time);
            Log(events, time, slot, WingEventKind.DepartureAborted);
            return new ControlOutput { Brake = 1f };
        }

        private void Finish(float time, WingEventRing events, int slot)
        {
            if (!airborneReported && !Vertical) field.Departures.Airborne(Owner);
            airborneReported = true;
            field.Leave(Owner);
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
            int behind = nodes[on], owner = field.Reservations.OwnerOfNode(behind);
            if (along < NodeAt(on) + 0.5f || (owner >= 0 && owner != Owner) || !field.Reservations.SoleUser(Owner, edges[on]) ||
                !TryRoute(behind, 0, avoidEdge, avoidNode, edges[on])) return false;
            // Turn back along the edge to its first node, holding the edge and that node for the way back.
            int back = edges[on], far = nodes[on + 1];
            field.Reservations.ReleaseAll(Owner);
            backEdge = field.Reservations.TryClaimEdge(Owner, back, far) ? back : -1;
            nodes.Clear();
            nodes.AddRange(candidateNodes);
            edges.Clear();
            edges.AddRange(candidateEdges);
            field.Reservations.TryAdvance(Owner, TaxiPriority.Departing, nodes, edges, 0, 0);
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
            TaxiGraph g = field.Graph;
            Func<int, bool> avoid = e => e == avoidEdge || e == alsoAvoid || field.Reservations.Blocked(e) || Kept(e, keptEdges) ||
                                         (avoidNode >= 0 && (g.EdgeFrom(e) == avoidNode || g.EdgeTo(e) == avoidNode));
            if (!TaxiRouter.Route(g, start, g.HoldShort(field.RunwayIndex, field.Reverse), (e, from) => RouteCost(e, from) + (avoid(e) ? RerouteCost : 0f),
                    candidateNodes, candidateEdges)) return false;
            foreach (int e in candidateEdges)
                if (avoid(e)) return false;
            return true;
        }

        /// <summary>Keeps the route to node <paramref name="j"/> (and its claims) and continues along the candidate route;
        /// only what lay beyond node j is let go.</summary>
        private void Divert(int j)
        {
            TaxiGraph g = field.Graph;
            for (int i = j; i < edges.Count; i++) field.Reservations.ReleaseEdge(Owner, edges[i]);
            for (int i = j + 1; i < nodes.Count; i++) field.Reservations.ReleaseNode(Owner, nodes[i]);
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
            (field.Reservations.Blocked(e) ? RerouteCost : 0f) + (field.Reservations.Against(e, from) ? OppositeCost : 0f);

        /// <summary>Once, when the hold-short is free: claimed, then the engine moves the aircraft there.</summary>
        private void Relocate(float time, WingEventRing events, int slot)
        {
            int hold = field.Graph.HoldShort(field.RunwayIndex, field.Reverse);
            Vec3 at = field.Graph.NodePos(hold);
            int owner = field.Reservations.OwnerOfNode(hold);
            if ((owner >= 0 && owner != Owner) || field.Occupied(at, RelocateClearRadius, Owner)) return;
            field.Reservations.ReleaseAll(Owner);
            holdClaim[0] = hold;
            if (field.Reservations.TryAdvance(Owner, TaxiPriority.Departing, holdClaim, noEdges, 0, 0) == 0 &&
                field.Reservations.OwnerOfNode(hold) != Owner) return;
            relocationWanted = false;
            Vec3 threshold = field.Reverse ? field.Runway.End : field.Runway.Start;
            relocation = new Pose(at, (threshold - at).Horizontal.Normalized);
            relocationPending = true;
            backEdge = -1;
            RouteFrom(new[] { at }, hold);
            Watchdog.Restart(at);
            Log(events, time, slot, WingEventKind.Relocated);
        }

        /// <summary>The path is <paramref name="prefix"/> (from where the aircraft is) followed by the route from
        /// <paramref name="start"/> to the departure hold-short (see <see cref="RouteCost"/>).</summary>
        private void RouteFrom(Vec3[] prefix, int start)
        {
            int goal = field.Graph.HoldShort(field.RunwayIndex, field.Reverse);
            if (start < 0 || goal < 0 || !TaxiRouter.Route(field.Graph, start, goal, RouteCost, nodes, edges))
            {
                nodes.Clear();
                edges.Clear();
                SetPath(new[] { prefix[0], goal >= 0 ? field.Graph.NodePos(goal) : prefix[0] + spawn.Fwd * 10f });
                nodePathIndex.Clear();
                return;
            }
            SetRoutePath(prefix);
        }

        /// <summary>The path for the current route: <paramref name="prefix"/>, then the route's points.</summary>
        private void SetRoutePath(Vec3[] prefix)
        {
            Vec3[] pts = field.Graph.PathPoints(nodes, edges);
            var full = new Vec3[prefix.Length + pts.Length];
            Array.Copy(prefix, 0, full, 0, prefix.Length);
            Array.Copy(pts, 0, full, prefix.Length, pts.Length);
            SetPath(full);
            nodePathIndex.Clear();
            int index = prefix.Length;
            nodePathIndex.Add(index);
            for (int k = 0; k < edges.Count; k++)
            {
                index += field.Graph.EdgePoints(edges[k]).Length - 1;
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
                field.Reservations.ReleaseEdge(Owner, backEdge);
                backEdge = -1;
            }
            for (int k = 0; k <= step && k < nodes.Count; k++)
                if (along > NodeAt(k) + NodeClearRadius) field.Reservations.ReleaseNode(Owner, nodes[k]);
            for (int k = 0; k < step; k++)
                if (along > NodeAt(k + 1) + NodeClearRadius) field.Reservations.ReleaseEdge(Owner, edges[k]);
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

        /// <summary>Where to stop for a foreign aircraft (a very long distance when none): on the path ahead, and whatever
        /// the path says, anything in the corridor ahead of the nose.</summary>
        private float ObstacleStop(float along, in AircraftState s)
        {
            float stop = float.MaxValue;
            if (field.Obstacles.Count == 0 || path.Length < 2) return stop;
            Vec3 nose = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            Vec3 side = Vec3.Cross(Vec3.Up, nose);
            foreach (Vec3 o in field.Obstacles)
            {
                Vec3 d = (o - s.Pos).Horizontal;
                float ahead = Vec3.Dot(d, nose);
                if (ahead > 0f && Math.Abs(Vec3.Dot(d, side)) < ObstacleCorridor) stop = Math.Min(stop, along + ahead - ObstacleGap);
            }
            for (int i = Math.Min(progress, path.Length - 2); i < path.Length - 1 && cum[i] < along + ObstacleLookahead; i++)
            {
                Vec3 a = path[i].Horizontal, b = path[i + 1].Horizontal, ab = b - a;
                float len = ab.Length;
                if (len < 1e-4f) continue;
                Vec3 u = ab / len;
                foreach (Vec3 o in field.Obstacles)
                {
                    Vec3 ao = o.Horizontal - a;
                    float t = Vec3.Dot(ao, u);
                    if (t < 0f || t > len) continue;
                    float lateral = (ao - u * t).Length;
                    float at = cum[i] + t;
                    if (lateral < ObstacleCorridor && at > along) stop = Math.Min(stop, at - ObstacleGap);
                }
            }
            return stop;
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
