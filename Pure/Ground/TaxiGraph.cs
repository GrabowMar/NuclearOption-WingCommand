using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum NodeKind : byte { Road, HangarExit, HoldShort, Threshold, RunwayExit, Service }

    /// <summary>An airbase's taxi network as nodes and polyline edges (spec M3 §2.1).
    /// <list type="bullet">
    /// <item>Road ends become nodes; ends within the merge radius are one node (as the game's own network merges
    /// them); each road is one edge carrying its polyline. Every edge can be travelled both ways.</item>
    /// <item>Synthetic nodes: a hangar exit <see cref="HangarExitDistance"/> in front of each hangar's spawn; per
    /// runway end usable for takeoff a threshold (where the roll starts) and a hold-short (the runway's entry point
    /// nearest that end, else one computed off the runway toward the field centre), joined by the lineup edge. A
    /// synthetic node that lands on no road end is snapped onto the nearest edge, splitting it.</item>
    /// <item>A field without roads joins each hangar exit straight to each hold-short.</item>
    /// </list></summary>
    internal sealed class TaxiGraph
    {
        public static float HangarExitDistance = 40f, HoldShortBack = 60f, HoldShortSide = 40f, EntrySearchRadius = 400f;

        private readonly List<Vec3> positions = new List<Vec3>();
        private readonly List<NodeKind> kinds = new List<NodeKind>();
        private readonly List<int> from = new List<int>(), to = new List<int>();
        private readonly List<Vec3[]> points = new List<Vec3[]>();
        private readonly List<float> lengths = new List<float>();
        private readonly List<List<int>> adjacency = new List<List<int>>();
        private int[] hangarExits = new int[0];
        private int[,] holdShorts = new int[0, 2], thresholds = new int[0, 2];
        private float mergeRadius;

        public int NodeCount => positions.Count;
        public int EdgeCount => from.Count;
        public Vec3 NodePos(int n) => positions[n];
        public NodeKind Kind(int n) => kinds[n];
        public int EdgeFrom(int e) => from[e];
        public int EdgeTo(int e) => to[e];
        public float EdgeLength(int e) => lengths[e];
        /// <summary>The edge's polyline from <see cref="EdgeFrom"/> to <see cref="EdgeTo"/>.</summary>
        public Vec3[] EdgePoints(int e) => points[e];
        public IReadOnlyList<int> EdgesOf(int node) => adjacency[node];
        public int OtherEnd(int e, int node) => from[e] == node ? to[e] : from[e];

        /// <summary>The exit node of hangar <paramref name="hangarIndex"/>, −1 when the field has no such hangar.</summary>
        public int HangarExit(int hangarIndex) => hangarIndex >= 0 && hangarIndex < hangarExits.Length ? hangarExits[hangarIndex] : -1;

        /// <summary>The hold-short before rolling on runway <paramref name="runway"/> in the given direction (−1: none).</summary>
        public int HoldShort(int runway, bool reverse) => runway >= 0 && runway < holdShorts.GetLength(0) ? holdShorts[runway, reverse ? 1 : 0] : -1;

        public int Threshold(int runway, bool reverse) => runway >= 0 && runway < thresholds.GetLength(0) ? thresholds[runway, reverse ? 1 : 0] : -1;

        public int NearestNode(Vec3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < positions.Count; n++)
            {
                float d = (positions[n] - p).SqrLength;
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>The polyline of a route (as <see cref="TaxiRouter.Route"/> returns it), each edge in travel order.</summary>
        public Vec3[] PathPoints(IReadOnlyList<int> nodes, IReadOnlyList<int> edges)
        {
            var path = new List<Vec3> { positions[nodes[0]] };
            for (int i = 0; i < edges.Count; i++)
            {
                Vec3[] pts = points[edges[i]];
                bool forward = from[edges[i]] == nodes[i];
                for (int k = 1; k < pts.Length; k++) path.Add(forward ? pts[k] : pts[pts.Length - 1 - k]);
            }
            return path.ToArray();
        }

        public static TaxiGraph Build(AirbaseSample field, float mergeRadius = 10f)
        {
            var g = new TaxiGraph { mergeRadius = mergeRadius };
            foreach (Vec3[] road in field.Roads)
                if (road != null && road.Length >= 2) g.AddEdge(g.NodeAt(road[0]), g.NodeAt(road[road.Length - 1]), road);
            bool roads = g.EdgeCount > 0;

            g.hangarExits = new int[field.Hangars.Length];
            for (int i = 0; i < field.Hangars.Length; i++)
            {
                Pose spawn = field.Hangars[i].Spawn;
                g.hangarExits[i] = g.Synthetic(spawn.Pos + spawn.Fwd.Horizontal.Normalized * HangarExitDistance, NodeKind.HangarExit);
            }

            g.holdShorts = new int[field.Runways.Length, 2];
            g.thresholds = new int[field.Runways.Length, 2];
            for (int r = 0; r < field.Runways.Length; r++)
                for (int d = 0; d < 2; d++)
                {
                    g.holdShorts[r, d] = g.thresholds[r, d] = -1;
                    RunwaySample runway = field.Runways[r];
                    bool reverse = d == 1;
                    if (!runway.Takeoff || (reverse && !runway.Reversable)) continue;
                    Vec3 threshold = reverse ? runway.End : runway.Start;
                    Vec3 hold = HoldShortPosition(field, runway, reverse, threshold);
                    int holdNode = g.Synthetic(hold, NodeKind.HoldShort);
                    int thresholdNode = g.AddNode(threshold, NodeKind.Threshold);
                    g.AddEdge(holdNode, thresholdNode, new[] { hold, threshold });
                    g.holdShorts[r, d] = holdNode;
                    g.thresholds[r, d] = thresholdNode;
                }

            if (!roads)
                foreach (int exit in g.hangarExits)
                    for (int r = 0; r < field.Runways.Length; r++)
                        for (int d = 0; d < 2; d++)
                            if (g.holdShorts[r, d] >= 0)
                                g.AddEdge(exit, g.holdShorts[r, d], new[] { g.positions[exit], g.positions[g.holdShorts[r, d]] });
            return g;
        }

        /// <summary>The runway's entry point nearest the threshold (within <see cref="EntrySearchRadius"/>), else a point
        /// <see cref="HoldShortBack"/> before the threshold and clear of the runway edge on the side of the field centre.</summary>
        private static Vec3 HoldShortPosition(AirbaseSample field, RunwaySample runway, bool reverse, Vec3 threshold)
        {
            float best = EntrySearchRadius;
            Vec3 found = Vec3.Zero;
            bool have = false;
            foreach (Pose entry in runway.Entries)
            {
                float d = (entry.Pos - threshold).Horizontal.Length;
                if (d < best)
                {
                    best = d;
                    found = entry.Pos;
                    have = true;
                }
            }
            if (have) return found;
            Vec3 dir = runway.Direction(reverse);
            Vec3 side = Vec3.Cross(Vec3.Up, dir);
            if (Vec3.Dot(field.Center - threshold, side) < 0f) side = -side;
            return threshold - dir * HoldShortBack + side * (0.5f * runway.Width + HoldShortSide);
        }

        private int NodeAt(Vec3 p)
        {
            for (int n = 0; n < positions.Count; n++)
                if ((positions[n] - p).Length <= mergeRadius) return n;
            return AddNode(p, NodeKind.Road);
        }

        /// <summary>A synthetic node: an existing node when one is within the merge radius (it takes the kind), else a
        /// new node snapped onto the nearest edge.</summary>
        private int Synthetic(Vec3 p, NodeKind kind)
        {
            for (int n = 0; n < positions.Count; n++)
                if ((positions[n] - p).Length <= mergeRadius)
                {
                    kinds[n] = kind;
                    return n;
                }
            int node = AddNode(p, kind);
            Snap(node);
            return node;
        }

        private int AddNode(Vec3 p, NodeKind kind)
        {
            positions.Add(p);
            kinds.Add(kind);
            adjacency.Add(new List<int>());
            return positions.Count - 1;
        }

        private void AddEdge(int a, int b, Vec3[] pts)
        {
            from.Add(a);
            to.Add(b);
            points.Add(pts);
            lengths.Add(Length(pts));
            adjacency[a].Add(from.Count - 1);
            if (b != a) adjacency[b].Add(from.Count - 1);
        }

        /// <summary>Joins <paramref name="node"/> to the nearest point of any edge with a straight connector, splitting
        /// that edge there (or using its end node when the point is within the merge radius of it).</summary>
        private void Snap(int node)
        {
            Vec3 p = positions[node];
            int bestEdge = -1, bestSegment = -1;
            float bestD = float.MaxValue;
            Vec3 bestPoint = Vec3.Zero;
            for (int e = 0; e < from.Count; e++)
            {
                Vec3[] pts = points[e];
                for (int k = 1; k < pts.Length; k++)
                {
                    Vec3 q = Closest(pts[k - 1], pts[k], p);
                    float d = (q - p).Length;
                    if (d < bestD)
                    {
                        bestD = d;
                        bestEdge = e;
                        bestSegment = k;
                        bestPoint = q;
                    }
                }
            }
            if (bestEdge < 0) return;
            int join = Split(bestEdge, bestSegment, bestPoint);
            if (join != node) AddEdge(node, join, new[] { p, positions[join] });
        }

        private int Split(int e, int segment, Vec3 at)
        {
            if ((positions[from[e]] - at).Length <= mergeRadius) return from[e];
            if ((positions[to[e]] - at).Length <= mergeRadius) return to[e];
            Vec3[] pts = points[e];
            int middle = AddNode(at, NodeKind.Road);
            var first = new List<Vec3>();
            for (int k = 0; k < segment; k++) first.Add(pts[k]);
            first.Add(at);
            var second = new List<Vec3> { at };
            for (int k = segment; k < pts.Length; k++) second.Add(pts[k]);
            int end = to[e];
            adjacency[end].Remove(e);
            to[e] = middle;
            points[e] = first.ToArray();
            lengths[e] = Length(points[e]);
            adjacency[middle].Add(e);
            AddEdge(middle, end, second.ToArray());
            return middle;
        }

        private static Vec3 Closest(Vec3 a, Vec3 b, Vec3 p)
        {
            Vec3 ab = b - a;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) return a;
            float t = Scalar.Clamp(Vec3.Dot(p - a, ab) / len2, 0f, 1f);
            return a + ab * t;
        }

        private static float Length(Vec3[] pts)
        {
            float length = 0f;
            for (int k = 1; k < pts.Length; k++) length += (pts[k] - pts[k - 1]).Length;
            return length;
        }
    }

    /// <summary>Shortest routes over a <see cref="TaxiGraph"/> (A* on edge length plus an optional extra cost per edge and
    /// the node it is entered from: in use against the travel direction, blocked, or an edge to avoid).</summary>
    internal static class TaxiRouter
    {
        public static bool Route(TaxiGraph g, int start, int goal, Func<int, int, float> extraCost, List<int> nodes, List<int> edges)
        {
            nodes.Clear();
            edges.Clear();
            if (start < 0 || goal < 0) return false;
            int n = g.NodeCount;
            var cost = new float[n];
            var viaEdge = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.MaxValue;
                viaEdge[i] = -1;
            }
            cost[start] = 0f;
            Vec3 target = g.NodePos(goal);
            while (true)
            {
                int current = -1;
                float bestF = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (closed[i] || cost[i] == float.MaxValue) continue;
                    float f = cost[i] + (g.NodePos(i) - target).Length;
                    if (f < bestF)
                    {
                        bestF = f;
                        current = i;
                    }
                }
                if (current < 0) return false;
                if (current == goal) break;
                closed[current] = true;
                foreach (int e in g.EdgesOf(current))
                {
                    int next = g.OtherEnd(e, current);
                    if (closed[next]) continue;
                    float c = cost[current] + g.EdgeLength(e) + (extraCost != null ? extraCost(e, current) : 0f);
                    if (c < cost[next])
                    {
                        cost[next] = c;
                        viaEdge[next] = e;
                    }
                }
            }
            for (int at = goal; at != start; at = g.OtherEnd(viaEdge[at], at))
            {
                nodes.Add(at);
                edges.Add(viaEdge[at]);
            }
            nodes.Add(start);
            nodes.Reverse();
            edges.Reverse();
            return true;
        }
    }
}
