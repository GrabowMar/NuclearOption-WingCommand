using System.Collections.Generic;

namespace WingCommand
{
    internal enum TaxiPriority : byte { TaxiIn = 0, Departing = 1, Landing = 2 }

    /// <summary>Who may be where on one field's taxi graph (spec M3 §2.1), shared by every Wing Command aircraft there.
    /// <list type="bullet">
    /// <item>Nodes (junctions, hold-shorts, thresholds) are exclusive: one owner at a time.</item>
    /// <item>Edges are directional: owners travelling the same way share an edge in the order they entered it (a
    /// convoy; each keeps its distance to <see cref="Ahead"/>), and nobody enters against that direction.</item>
    /// <item>An owner claims along its route item by item (edge, far node, next edge, ...) and stops at the first item
    /// it cannot have; what stopped it is remembered for <see cref="FindDeadlock"/>. It releases each node and edge
    /// once past it.</item>
    /// <item>A blocked edge (a wreck, native traffic) is never granted.</item>
    /// </list></summary>
    internal sealed class TaxiReservations
    {
        private readonly TaxiGraph graph;
        private readonly int[] nodeOwner;
        private readonly List<int>[] edgeUsers;
        private readonly int[] edgeDirection;   // +1 from→to, −1 to→from, 0 free
        private readonly bool[] blocked;
        private readonly Dictionary<int, int> waitingFor = new Dictionary<int, int>();
        private readonly Dictionary<int, TaxiPriority> priorities = new Dictionary<int, TaxiPriority>();
        private readonly List<int> seen = new List<int>();

        public TaxiReservations(TaxiGraph g)
        {
            graph = g;
            nodeOwner = new int[g.NodeCount];
            for (int i = 0; i < nodeOwner.Length; i++) nodeOwner[i] = -1;
            edgeUsers = new List<int>[g.EdgeCount];
            for (int i = 0; i < edgeUsers.Length; i++) edgeUsers[i] = new List<int>(4);
            edgeDirection = new int[g.EdgeCount];
            blocked = new bool[g.EdgeCount];
        }

        public int OwnerOfNode(int node) => nodeOwner[node];

        /// <summary>The first owner on the edge (−1: free).</summary>
        public int OwnerOfEdge(int edge) => edgeUsers[edge].Count > 0 ? edgeUsers[edge][0] : -1;

        /// <summary>The owner directly ahead of <paramref name="owner"/> on <paramref name="edge"/> (−1: none).</summary>
        public int Ahead(int owner, int edge)
        {
            int i = edgeUsers[edge].IndexOf(owner);
            return i > 0 ? edgeUsers[edge][i - 1] : -1;
        }

        public bool Blocked(int edge) => blocked[edge];

        public void Block(int edge, bool isBlocked) => blocked[edge] = isBlocked;

        /// <summary>Claims along the route from step <paramref name="from"/> for up to <paramref name="steps"/> steps (a
        /// step is edge k then node k+1; the route's first node is claimed too when <paramref name="from"/> is 0).
        /// Returns how many items it holds from there (0..2·steps); stops at the first item it cannot have.</summary>
        public int TryAdvance(int owner, TaxiPriority priority, IReadOnlyList<int> nodes, IReadOnlyList<int> edges, int from, int steps)
        {
            priorities[owner] = priority;
            waitingFor.Remove(owner);
            if (from == 0 && nodes.Count > 0 && !ClaimNode(owner, nodes[0])) return 0;
            int granted = 0;
            for (int k = from; k < from + steps && k < edges.Count; k++)
            {
                int e = edges[k];
                int direction = graph.EdgeFrom(e) == nodes[k] ? 1 : -1;
                if (!ClaimEdge(owner, e, direction)) return granted;
                granted++;
                if (!ClaimNode(owner, nodes[k + 1])) return granted;
                granted++;
            }
            return granted;
        }

        public void ReleaseNode(int owner, int node)
        {
            if (nodeOwner[node] == owner) nodeOwner[node] = -1;
        }

        public void ReleaseEdge(int owner, int edge)
        {
            if (edgeUsers[edge].Remove(owner) && edgeUsers[edge].Count == 0) edgeDirection[edge] = 0;
        }

        public void ReleaseAll(int owner)
        {
            for (int n = 0; n < nodeOwner.Length; n++)
                if (nodeOwner[n] == owner) nodeOwner[n] = -1;
            for (int e = 0; e < edgeUsers.Length; e++) ReleaseEdge(owner, e);
            waitingFor.Remove(owner);
            priorities.Remove(owner);
        }

        /// <summary>A cycle of owners each waiting for the next. The victim (the one to back off and reroute) has the
        /// lowest priority, then the highest id.</summary>
        public bool FindDeadlock(out int victim)
        {
            victim = -1;
            foreach (int start in waitingFor.Keys)
            {
                int at = start;
                seen.Clear();
                while (waitingFor.TryGetValue(at, out int next) && Holds(next))
                {
                    seen.Add(at);
                    if (next == start)
                    {
                        victim = Victim(seen);
                        return true;
                    }
                    if (seen.Contains(next)) break;
                    at = next;
                }
            }
            return false;
        }

        private int Victim(List<int> cycle)
        {
            int victim = cycle[0];
            foreach (int o in cycle)
            {
                TaxiPriority p = Priority(o), v = Priority(victim);
                if (p < v || (p == v && o > victim)) victim = o;
            }
            return victim;
        }

        private TaxiPriority Priority(int owner) => priorities.TryGetValue(owner, out TaxiPriority p) ? p : TaxiPriority.TaxiIn;

        private bool Holds(int owner)
        {
            for (int n = 0; n < nodeOwner.Length; n++)
                if (nodeOwner[n] == owner) return true;
            for (int e = 0; e < edgeUsers.Length; e++)
                if (edgeUsers[e].Contains(owner)) return true;
            return false;
        }

        private bool ClaimNode(int owner, int node)
        {
            if (nodeOwner[node] == -1 || nodeOwner[node] == owner)
            {
                nodeOwner[node] = owner;
                return true;
            }
            waitingFor[owner] = nodeOwner[node];
            return false;
        }

        private bool ClaimEdge(int owner, int edge, int direction)
        {
            if (edgeUsers[edge].Contains(owner)) return true;
            if (blocked[edge]) return false;
            if (edgeUsers[edge].Count > 0 && edgeDirection[edge] != direction)
            {
                waitingFor[owner] = edgeUsers[edge][edgeUsers[edge].Count - 1];
                return false;
            }
            edgeUsers[edge].Add(owner);
            edgeDirection[edge] = direction;
            return true;
        }
    }
}
