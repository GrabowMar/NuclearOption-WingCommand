using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum GroundPhase : byte { Parked, TaxiOut, HoldShort, LineUp, Roll, ClimbOut, LiftOff, Done }

    /// <summary>One member from its spawn on a field until it is airborne and the formation pilot takes over (spec M3 §3).
    /// <list type="bullet">
    /// <item>Parked: brakes for <see cref="ParkedSeconds"/>, then routes to the departure hold-short.</item>
    /// <item>TaxiOut / HoldShort: pure pursuit along the route, claiming <see cref="ClaimAhead"/> of it; it stops where its
    /// claims end, <see cref="FollowGap"/> behind the member ahead on its edge, and <see cref="ObstacleGap"/> short of a
    /// foreign aircraft within <see cref="ObstacleCorridor"/> of its path. Reaching the last edge it joins the departure
    /// queue (HoldShort) and creeps to the hold-short node.</item>
    /// <item>LineUp: once the sequencer lets it, from the hold-short across the threshold to its row and lane, aligned
    /// over the last <see cref="LineupRunIn"/>; it releases its taxi claims once clear of the hold-short and tells the
    /// sequencer when it is past the threshold.</item>
    /// <item>Roll: on the sequencer's word, full throttle along its lane to the takeoff speed.</item>
    /// <item>ClimbOut: the flight pipeline flies the runway's extended centreline climbing to
    /// <see cref="ClimbOutAboveRunway"/>; done at <see cref="ClimbOutHeight"/> radar altitude or after
    /// <see cref="ClimbOutSeconds"/>.</item>
    /// <item>Helicopters and tiltwings lift off in place (LiftOff), hover-taxiing out first when a roof is overhead.</item>
    /// <item>The watchdog reroutes a member that makes no progress, then relocates it once to the hold-short.</item>
    /// </list></summary>
    internal sealed class GroundPilot
    {
        public static float ParkedSeconds = 2f, ClaimAhead = 80f, NodeClearRadius = 15f, FollowGap = 40f;
        public static float ObstacleCorridor = 15f, ObstacleGap = 30f, ObstacleLookahead = 150f;
        public static float ThresholdEntry = 10f, ClearedPastThreshold = 30f, LineupRunIn = 25f, AlignTail = 80f;
        public static float LineupTolerance = 3f, LineupAlignDeg = 10f, StoppedSpeed = 0.5f;
        public static float LiftOffHeight = 30f, HoverExitHeight = 3f, HoverExitReached = 5f;
        public static float ClimbOutHeight = 150f, ClimbOutSeconds = 30f, ClimbOutAboveRunway = 300f, ClimbOutSpeedFactor = 1.3f;
        public static float RerouteCost = 1e4f;

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
        private Vec3[] path = new Vec3[0];
        private float[] cum = new float[0];
        private int progress;
        private float phaseStart, liftoffTime = float.NaN;
        private bool enqueued, taxiReleased, clearedThreshold, rolling, airborneReported, exitedHangar, relocationPending;
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
                            RouteFrom(s.Pos, hangar >= 0 ? field.Graph.HangarExit(hangar) : field.Graph.NearestNode(s.Pos), null);
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
                    o = LineUp(s, p, time, dt);
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

        /// <summary>Off the field for good (airborne, dead, released).</summary>
        public void Leave() => field.Leave(Owner);

        private ControlOutput Taxi(in AircraftState s, AirframeProfile p, float time, float dt, WingEventRing events, int slot)
        {
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
                int items = field.Reservations.TryAdvance(Owner, TaxiPriority.Departing, nodes, edges, step, steps);
                bool toGoal = step + steps >= edges.Count && items >= 2 * steps;
                if (!toGoal)
                {
                    int full = items / 2;
                    if (items % 2 == 1) stopAt = NodeAt(step + full + 1) - NodeClearRadius;
                    else if (items == 0 && along < NodeAt(0)) stopAt = NodeAt(0) - NodeClearRadius;
                    else stopAt = NodeAt(step + full);
                    reservationWait = stopAt - along < 3f;
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
                BeginLineUp(s.Pos);
                Log(events, time, slot, WingEventKind.LiningUp);
                return new ControlOutput { Brake = 1f };
            }

            if (field.Victim == Owner)
            {
                field.ConsumeVictim(Owner);
                Reroute(s.Pos, step, along, events, time, slot);
            }
            switch (Watchdog.Update(s.Pos, reservationWait || atGoal, dt))
            {
                case WatchdogAction.Reroute:
                    Reroute(s.Pos, step, along, events, time, slot);
                    break;
                case WatchdogAction.Relocate:
                    Relocate(time, events, slot);
                    return new ControlOutput { Brake = 1f };
            }

            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, stopAt - along);
            return controller.Step(c, s, p, dt);
        }

        private void BeginLineUp(Vec3 pos)
        {
            RunwaySample r = field.Runway;
            Vec3 dir = r.Direction(field.Reverse);
            Vec3 threshold = field.Reverse ? r.End : r.Start;
            field.Departures.SlotOf(Owner, out int row, out int column);
            Vec3 lane = LineupPlanner.Slot(r, field.Reverse, row, column, field.Departures.Abreast, field.Departures.Rows);
            SetPath(new[] { pos, threshold + dir * ThresholdEntry, lane - dir * LineupRunIn, lane, lane + dir * AlignTail });
            lineupSlotIndex = 3;
            taxiReleased = clearedThreshold = false;
            Enter(GroundPhase.LineUp, 0f);
        }

        private ControlOutput LineUp(in AircraftState s, AirframeProfile p, float time, float dt)
        {
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
            if (toSlot < LineupTolerance && speed < StoppedSpeed && heading < LineupAlignDeg)
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
                Vec3 dir = field.Runway.Direction(field.Reverse);
                SetPath(new[] { s.Pos, s.Pos + dir * Math.Max(500f, field.Runway.Length) });
                Log(events, time, slot, WingEventKind.Rolling);
            }
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, s, 1e6f);
            ControlOutput o = controller.Step(c, s, p, 0f);
            o.Throttle = 1f;
            o.Brake = 0f;
            float speed = Vec3.Dot(s.Vel, s.Fwd.Horizontal.Normalized);
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

        private void Finish(float time, WingEventRing events, int slot)
        {
            if (!airborneReported && !Vertical) field.Departures.Airborne(Owner);
            airborneReported = true;
            field.Leave(Owner);
            Enter(GroundPhase.Done, time);
        }

        /// <summary>A new route from where the aircraft stands, the edge it is on (or waiting to enter) made expensive.</summary>
        private void Reroute(Vec3 pos, int step, float along, WingEventRing events, float time, int slot)
        {
            int avoid = edges.Count > 0 ? edges[Math.Min(edges.Count - 1, step)] : -1;
            field.Reservations.ReleaseAll(Owner);
            RouteFrom(pos, field.Graph.NearestNode(pos), e => e == avoid ? RerouteCost : 0f);
            Log(events, time, slot, WingEventKind.Rerouted);
        }

        /// <summary>Once: to the hold-short (the engine moves the aircraft), queued there.</summary>
        private void Relocate(float time, WingEventRing events, int slot)
        {
            int hold = field.Graph.HoldShort(field.RunwayIndex, field.Reverse);
            Vec3 at = field.Graph.NodePos(hold);
            Vec3 threshold = field.Reverse ? field.Runway.End : field.Runway.Start;
            relocation = new Pose(at, (threshold - at).Horizontal.Normalized);
            relocationPending = true;
            field.Reservations.ReleaseAll(Owner);
            RouteFrom(at, hold, null);
            Watchdog.Restart(at);
            Log(events, time, slot, WingEventKind.Relocated);
        }

        private void RouteFrom(Vec3 pos, int start, Func<int, float> extraCost)
        {
            int goal = field.Graph.HoldShort(field.RunwayIndex, field.Reverse);
            if (start < 0 || goal < 0 || !TaxiRouter.Route(field.Graph, start, goal, extraCost, nodes, edges))
            {
                nodes.Clear();
                edges.Clear();
                SetPath(new[] { pos, goal >= 0 ? field.Graph.NodePos(goal) : pos + spawn.Fwd * 10f });
                nodePathIndex.Clear();
                return;
            }
            Vec3[] pts = field.Graph.PathPoints(nodes, edges);
            var full = new Vec3[pts.Length + 1];
            full[0] = pos;
            Array.Copy(pts, 0, full, 1, pts.Length);
            SetPath(full);
            nodePathIndex.Clear();
            int index = 1;
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
