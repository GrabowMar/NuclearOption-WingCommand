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
    /// once past it. The sole owner of an edge may turn back on it (its direction flips); a convoy may not. Only the
    /// head of a convoy (the first on the edge) may claim the node at its far end. The route's first node is claimed
    /// only when asked (<c>claimStart</c>: not by an owner already past it).</item>
    /// <item>A blocked edge (a wreck, native traffic) is never granted.</item>
    /// <item>A deadlock's victim is an owner waiting to enter an edge (it stands at a node it holds and can take another
    /// way there) before one waiting for a node (it would have to turn back), then the lowest priority, then the highest
    /// id; an owner that found no other way (<see cref="NoDetour"/>) is passed over until it next advances.</item>
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
        private readonly HashSet<int> waitingOnEdge = new HashSet<int>(), noDetour = new HashSet<int>();
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

        /// <summary>The owner <paramref name="owner"/> last found holding what it asked for (−1: none).</summary>
        public int WaitingFor(int owner) => waitingFor.TryGetValue(owner, out int holder) ? holder : -1;

        public void Block(int edge, bool isBlocked) => blocked[edge] = isBlocked;

        /// <summary>Someone is on <paramref name="edge"/> travelling towards <paramref name="fromNode"/>.</summary>
        public bool Against(int edge, int fromNode) =>
            edgeUsers[edge].Count > 0 && edgeDirection[edge] != (graph.EdgeFrom(edge) == fromNode ? 1 : -1);

        /// <summary>Nobody but <paramref name="owner"/> is on the edge.</summary>
        public bool SoleUser(int owner, int edge) =>
            edgeUsers[edge].Count == 0 || (edgeUsers[edge].Count == 1 && edgeUsers[edge][0] == owner);

        /// <summary>Claims one edge for travel from <paramref name="fromNode"/> (a turn back on it).</summary>
        public bool TryClaimEdge(int owner, int edge, int fromNode) =>
            ClaimEdge(owner, edge, graph.EdgeFrom(edge) == fromNode ? 1 : -1);

        /// <summary>The owner, a deadlock's victim, found no other way: pick another until it next advances.</summary>
        public void NoDetour(int owner) => noDetour.Add(owner);

        /// <summary>Claims along the route from step <paramref name="from"/> for up to <paramref name="steps"/> steps (a
        /// step is edge k then node k+1; the route's first node is claimed too when <paramref name="from"/> is 0 and
        /// <paramref name="claimStart"/>; the last step's far node only when <paramref name="lastNode"/>).
        /// Returns how many items it holds from there (0..2·steps); stops at the first item it cannot have.</summary>
        public int TryAdvance(int owner, TaxiPriority priority, IReadOnlyList<int> nodes, IReadOnlyList<int> edges, int from, int steps,
            bool claimStart = true, bool lastNode = true)
        {
            priorities[owner] = priority;
            waitingFor.Remove(owner);
            waitingOnEdge.Remove(owner);
            if (claimStart && from == 0 && nodes.Count > 0 && !ClaimNode(owner, nodes[0])) return 0;
            int granted = 0;
            for (int k = from; k < from + steps && k < edges.Count; k++)
            {
                int e = edges[k];
                int direction = graph.EdgeFrom(e) == nodes[k] ? 1 : -1;
                if (!ClaimEdge(owner, e, direction)) return granted;
                granted++;
                if (!lastNode && k == from + steps - 1) break;
                if (edgeUsers[e][0] != owner)
                {
                    // Behind others on the edge: its far node is theirs to take first.
                    waitingFor[owner] = edgeUsers[e][0];
                    waitingOnEdge.Remove(owner);
                    return granted;
                }
                if (!ClaimNode(owner, nodes[k + 1])) return granted;
                granted++;
            }
            noDetour.Remove(owner);
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
            waitingOnEdge.Remove(owner);
            noDetour.Remove(owner);
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
            bool anyDetour = false;
            foreach (int o in cycle)
                if (!noDetour.Contains(o)) anyDetour = true;
            int victim = -1;
            foreach (int o in cycle)
            {
                if (anyDetour && noDetour.Contains(o)) continue;
                if (victim < 0 || Before(o, victim)) victim = o;
            }
            return victim;
        }

        /// <summary><paramref name="a"/> backs off before <paramref name="b"/>.</summary>
        private bool Before(int a, int b)
        {
            bool edgeA = waitingOnEdge.Contains(a), edgeB = waitingOnEdge.Contains(b);
            if (edgeA != edgeB) return edgeA;
            TaxiPriority pa = Priority(a), pb = Priority(b);
            return pa != pb ? pa < pb : a > b;
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
            waitingOnEdge.Remove(owner);
            return false;
        }

        private bool ClaimEdge(int owner, int edge, int direction)
        {
            if (edgeUsers[edge].Contains(owner))
            {
                if (edgeDirection[edge] == direction) return true;
                if (edgeUsers[edge].Count == 1)
                {
                    edgeDirection[edge] = direction;
                    return true;
                }
            }
            else if (blocked[edge]) return false;
            if (edgeUsers[edge].Count > 0 && edgeDirection[edge] != direction)
            {
                int other = edgeUsers[edge][edgeUsers[edge].Count - 1];
                waitingFor[owner] = other != owner ? other : edgeUsers[edge][0];
                waitingOnEdge.Add(owner);
                return false;
            }
            edgeUsers[edge].Add(owner);
            edgeDirection[edge] = direction;
            return true;
        }
    }
}
