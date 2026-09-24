using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One airbase's Wing Command traffic (spec M3 §2.2): its graph, the reservations and departures every member
    /// there shares, the departure runway and direction, where each member is, and foreign aircraft standing on the
    /// field (<see cref="Obstacles"/>, refreshed by the engine). Stepped once per tick before its members: the departure
    /// runway is busy while a foreign aircraft is on it (within <see cref="RunwayMargin"/>); every
    /// <see cref="DeadlockPeriod"/> it blocks each taxi edge a foreign aircraft has stood on (within
    /// <see cref="BlockCorridor"/>, still for <see cref="BlockSeconds"/>) until it moves, checks for a reservation
    /// deadlock and names the member that must back off.</summary>
    internal sealed class FieldTraffic
    {
        public static float DeadlockPeriod = 1f, RunwayMargin = 5f, BlockSeconds = 20f, BlockCorridor = 10f, StandingRadius = 3f;
        public static float PassedRadius = 20f;
        /// <summary>A jet handed to the game's landing has the runway to itself until it is down, its landing fails, or this
        /// long has passed (a turn never given back lapses). In game a 60 s spacing let two jets into the pattern, and the
        /// game's final approach aborts when another aircraft on the landing list is ahead and closing.</summary>
        public static float LandingHoldMaxSeconds = 300f;
        /// <summary>A spot a member was relocated to is nobody else's to be relocated to for this long, whatever the
        /// positions say (a report can lag the move).</summary>
        public static float RelocationGuardSeconds = 20f;

        public readonly AirbaseSample Field;
        public readonly TaxiGraph Graph;
        public readonly TaxiReservations Reservations;
        public readonly DepartureSequencer Departures = new DepartureSequencer();
        public readonly int RunwayIndex;
        public readonly bool Reverse;
        public readonly List<Vec3> Obstacles = new List<Vec3>();
        private readonly Dictionary<int, Vec3> positions = new Dictionary<int, Vec3>();
        private readonly Dictionary<int, int> waitsFor = new Dictionary<int, int>();
        private readonly Dictionary<int, int> stands = new Dictionary<int, int>();   // owner → stand node
        private readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();

        private struct Route
        {
            public IReadOnlyList<int> Nodes, Edges;
            public int Step;
        }
        private List<Vec3> standing = new List<Vec3>(), nextStanding = new List<Vec3>();
        private List<float> standingFor = new List<float>(), nextStandingFor = new List<float>();
        private readonly bool[] blockedHere;
        private float sinceCheck;
        private int lander = -1;
        private float clock;
        private readonly List<RelocationMark> relocations = new List<RelocationMark>();

        private struct RelocationMark
        {
            public int Owner;
            public Vec3 At;
            public float Until;
        }
        private readonly HashSet<int> onRunway = new HashSet<int>();
        private float landerSince;

        public FieldTraffic(AirbaseSample field, int runwayIndex, bool reverse)
        {
            Field = field;
            Graph = TaxiGraph.Build(field);
            Reservations = new TaxiReservations(Graph);
            blockedHere = new bool[Graph.EdgeCount];
            RunwayIndex = runwayIndex;
            Reverse = reverse;
        }

        public RunwaySample Runway => Field.Runways[RunwayIndex];

        /// <summary>The member to back off and reroute out of a deadlock, −1 when there is none.</summary>
        public int Victim { get; private set; } = -1;

        /// <summary>The first runway usable for takeoff (and its direction: the end nearer the field centre starts the roll,
        /// so the taxi is short), or false when the field has none.</summary>
        public static bool TryPickRunway(AirbaseSample field, out int index, out bool reverse)
        {
            for (int r = 0; r < field.Runways.Length; r++)
            {
                RunwaySample runway = field.Runways[r];
                if (!runway.Takeoff) continue;
                index = r;
                reverse = runway.Reversable && (runway.End - field.Center).Horizontal.Length < (runway.Start - field.Center).Horizontal.Length;
                return true;
            }
            index = -1;
            reverse = false;
            return false;
        }

        public void Report(int owner, Vec3 pos) => positions[owner] = pos;

        /// <summary>The field's traffic in one line (the in-game ground trace and field dump): the departures, which member
        /// holds which node, blocked edges, who waits for whom, foreign aircraft standing on the field, the deadlock victim.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder(Departures.Describe());
            sb.Append(" claims=[");
            bool first = true;
            for (int n = 0; n < Graph.NodeCount; n++)
            {
                int o = Reservations.OwnerOfNode(n);
                if (o < 0) continue;
                sb.Append(first ? "" : ",").Append('n').Append(n).Append(':').Append(o);
                first = false;
            }
            sb.Append("] blocked=[");
            first = true;
            for (int e = 0; e < Graph.EdgeCount; e++)
            {
                if (!Reservations.Blocked(e)) continue;
                sb.Append(first ? "" : ",").Append('e').Append(e);
                first = false;
            }
            sb.Append("] waits=[");
            first = true;
            foreach (KeyValuePair<int, int> w in waitsFor)
            {
                if (w.Value < 0) continue;
                sb.Append(first ? "" : ",").Append(w.Key).Append('>').Append(w.Value);
                first = false;
            }
            sb.Append("] obstacles=[");
            for (int i = 0; i < Obstacles.Count; i++)
                sb.Append(i == 0 ? "" : ",").Append('(').Append(Whole(Obstacles[i].X)).Append(',').Append(Whole(Obstacles[i].Z)).Append(')');
            sb.Append("] victim=").Append(Victim);
            return sb.ToString();
        }

        private static string Whole(float v) => ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>An arriving member still on a runway (it keeps the departure runway busy while on it).</summary>
        public void ReportArriving(int owner, bool onTheRunway)
        {
            if (onTheRunway) onRunway.Add(owner);
            else onRunway.Remove(owner);
        }

        /// <summary>The runway is <paramref name="owner"/>'s to land on (spec M3 §4, review M3b C1): nobody else holds the
        /// game's landing here (held until released, at most <see cref="LandingHoldMaxSeconds"/>), or it is its own.</summary>
        public bool TryClaimLanding(int owner, float time)
        {
            if (lander >= 0 && lander != owner && time - landerSince < LandingHoldMaxSeconds) return false;
            if (lander != owner) landerSince = time;
            lander = owner;
            return true;
        }

        /// <summary>A member was relocated to <paramref name="at"/>: nobody else is for <see cref="RelocationGuardSeconds"/>.</summary>
        public void NoteRelocation(int owner, Vec3 at) =>
            relocations.Add(new RelocationMark { Owner = owner, At = at, Until = clock + RelocationGuardSeconds });

        /// <summary>Another member was relocated within <paramref name="radius"/> of <paramref name="at"/> in the last
        /// <see cref="RelocationGuardSeconds"/>.</summary>
        public bool RelocatedNear(Vec3 at, float radius, int except)
        {
            foreach (RelocationMark m in relocations)
                if (m.Owner != except && m.Until > clock && (m.At - at).Horizontal.Length < radius) return true;
            return false;
        }

        public void ReleaseLanding(int owner)
        {
            if (lander == owner) lander = -1;
        }

        public bool TryGetPosition(int owner, out Vec3 pos) => positions.TryGetValue(owner, out pos);

        /// <summary>Where each member on the field last was.</summary>
        public Dictionary<int, Vec3> Positions => positions;

        /// <summary>The member <paramref name="owner"/> is stopped for (−1: none), as it last reported.</summary>
        public int WaitsFor(int owner) => waitsFor.TryGetValue(owner, out int other) ? other : -1;

        public void ReportWait(int owner, int other)
        {
            waitsFor[owner] = other;
            Reservations.WaitForMember(owner, other);
        }

        /// <summary>The stand node is nobody else's (chosen by an arriving member, even before it holds the node).</summary>
        public bool StandFree(int node, int owner)
        {
            foreach (KeyValuePair<int, int> s in stands)
                if (s.Value == node && s.Key != owner) return false;
            return true;
        }

        public void TakeStand(int owner, int node) => stands[owner] = node;

        /// <summary>The member's route (its own lists, read live) and the edge it is on.</summary>
        public void ReportRoute(int owner, IReadOnlyList<int> nodes, IReadOnlyList<int> edges, int step) =>
            routes[owner] = new Route { Nodes = nodes, Edges = edges, Step = step };

        /// <summary>The member still has <paramref name="node"/> ahead, or is still within <see cref="PassedRadius"/> of
        /// it.</summary>
        public bool RouteAhead(int owner, int node)
        {
            if (!routes.TryGetValue(owner, out Route r)) return false;
            if (positions.TryGetValue(owner, out Vec3 at) && (at - Graph.NodePos(node)).Horizontal.Length < PassedRadius) return true;
            for (int i = Math.Max(0, r.Step + 1); i < r.Nodes.Count; i++)
                if (r.Nodes[i] == node) return true;
            return false;
        }

        /// <summary>The member's route ahead (from the edge it is on) uses <paramref name="edge"/>.</summary>
        public bool RouteUses(int owner, int edge)
        {
            if (!routes.TryGetValue(owner, out Route r)) return false;
            for (int i = Math.Max(0, r.Step); i < r.Edges.Count; i++)
                if (r.Edges[i] == edge) return true;
            return false;
        }

        public void LeaveStand(int owner) => stands.Remove(owner);

        /// <summary>Another member or a foreign aircraft stands within <paramref name="radius"/> of <paramref name="at"/>.</summary>
        public bool Occupied(Vec3 at, float radius, int except)
        {
            foreach (KeyValuePair<int, Vec3> p in positions)
                if (p.Key != except && (p.Value - at).Horizontal.Length < radius) return true;
            foreach (Vec3 o in Obstacles)
                if ((o - at).Horizontal.Length < radius) return true;
            return false;
        }

        public void Step(float dt)
        {
            clock += dt;
            for (int i = relocations.Count - 1; i >= 0; i--)
                if (relocations[i].Until <= clock) relocations.RemoveAt(i);
            bool busy = false;
            foreach (Vec3 o in Obstacles)
                if (Runway.Contains(o, RunwayMargin)) busy = true;
            foreach (int owner in onRunway)
                if (positions.TryGetValue(owner, out Vec3 at) && Runway.Contains(at, RunwayMargin)) busy = true;
            Departures.RunwayBusy = busy;
            TrackStanding(dt);
            sinceCheck += dt;
            if (sinceCheck < DeadlockPeriod) return;
            sinceCheck = 0f;
            BlockWhereStanding();
            Victim = Reservations.FindDeadlock(out int victim) ? victim : -1;
        }

        /// <summary>How long each foreign aircraft has stood where it is (one that moved more than
        /// <see cref="StandingRadius"/> starts again).</summary>
        private void TrackStanding(float dt)
        {
            nextStanding.Clear();
            nextStandingFor.Clear();
            foreach (Vec3 o in Obstacles)
            {
                float since = 0f;
                for (int i = 0; i < standing.Count; i++)
                    if ((standing[i] - o).Horizontal.Length < StandingRadius) since = Math.Max(since, standingFor[i] + dt);
                nextStanding.Add(o);
                nextStandingFor.Add(since);
            }
            (standing, nextStanding) = (nextStanding, standing);
            (standingFor, nextStandingFor) = (nextStandingFor, standingFor);
        }

        private void BlockWhereStanding()
        {
            for (int e = 0; e < Graph.EdgeCount; e++)
            {
                bool block = false;
                for (int i = 0; i < standing.Count && !block; i++)
                    block = standingFor[i] >= BlockSeconds && Near(Graph.EdgePoints(e), standing[i], BlockCorridor);
                if (block == blockedHere[e]) continue;
                blockedHere[e] = block;
                Reservations.Block(e, block);
            }
        }

        private static bool Near(Vec3[] polyline, Vec3 p, float radius)
        {
            Vec3 q = p.Horizontal;
            for (int k = 1; k < polyline.Length; k++)
            {
                Vec3 a = polyline[k - 1].Horizontal, ab = polyline[k].Horizontal - a;
                float len2 = ab.SqrLength;
                float t = len2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, Vec3.Dot(q - a, ab) / len2));
                if ((a + ab * t - q).Length < radius) return true;
            }
            return false;
        }

        public void ConsumeVictim(int owner)
        {
            if (Victim == owner) Victim = -1;
        }

        /// <summary>The victim found no other way: the next deadlock check picks another member.</summary>
        public void NoDetour(int owner) => Reservations.NoDetour(owner);

        /// <summary>A member done with the field (airborne, dead, released): its claims and its place go.</summary>
        public void Leave(int owner)
        {
            Reservations.ReleaseAll(owner);
            Departures.Remove(owner);
            positions.Remove(owner);
            waitsFor.Remove(owner);
            stands.Remove(owner);
            routes.Remove(owner);
            ConsumeVictim(owner);
            ReleaseLanding(owner);
            onRunway.Remove(owner);
        }
    }
}
