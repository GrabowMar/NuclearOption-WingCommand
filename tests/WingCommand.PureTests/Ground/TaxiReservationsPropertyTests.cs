using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Random traffic on a grid of taxiways (spec M3 §9): agents move node to node only through what they hold,
    /// deadlock victims take another way. Nobody ever stands on a node it does not hold and no two agents share a node;
    /// at airfield densities every agent gets where it is going (at half the grid occupied it can gridlock, which the
    /// ground pilot's one relocation resolves).</summary>
    public class TaxiReservationsPropertyTests
    {
        private const int Size = 4, Seeds = 200, MaxTicks = 600;

        private static TaxiGraph Grid()
        {
            var roads = new List<Vec3[]>();
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                {
                    if (x + 1 < Size) roads.Add(new[] { new Vec3(x * 100f, 0f, z * 100f), new Vec3((x + 1) * 100f, 0f, z * 100f) });
                    if (z + 1 < Size) roads.Add(new[] { new Vec3(x * 100f, 0f, z * 100f), new Vec3(x * 100f, 0f, (z + 1) * 100f) });
                }
            return TaxiGraph.Build(new AirbaseSample { Name = "grid", Roads = roads.ToArray() });
        }

        private sealed class Agent
        {
            public int Id, At, Goal, Step;
            public readonly List<int> Nodes = new List<int>(), Edges = new List<int>();
            public bool Arrived => At == Goal;
        }

        [Theory]
        [InlineData(5, true)]
        [InlineData(8, false)]
        public void RandomTrafficNeverSharesANodeAndEveryoneArrives(int count, bool mustArrive)
        {
            TaxiGraph g = Grid();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var random = new Random(seed);
                var r = new TaxiReservations(g);
                var agents = new List<Agent>();
                var starts = new HashSet<int>();
                for (int a = 0; a < count; a++)
                {
                    int start;
                    do start = random.Next(g.NodeCount); while (!starts.Add(start));
                    int goal;
                    do goal = random.Next(g.NodeCount); while (goal == start);
                    var agent = new Agent { Id = a, At = start, Goal = goal };
                    Assert.True(TaxiRouter.Route(g, start, goal, null, agent.Nodes, agent.Edges));
                    r.TryAdvance(a, TaxiPriority.Departing, agent.Nodes, agent.Edges, 0, 0);
                    agents.Add(agent);
                }
                int tick = 0;
                for (; tick < MaxTicks && !agents.TrueForAll(x => x.Arrived); tick++)
                {
                    foreach (Agent agent in agents)
                    {
                        if (agent.Arrived) continue;
                        int k = agent.Step;
                        int items = r.TryAdvance(agent.Id, TaxiPriority.Departing, agent.Nodes, agent.Edges, k, Math.Min(2, agent.Edges.Count - k),
                            claimStart: false);
                        if (items < 2) continue;
                        // Across edge k to node k+1: what lies behind is let go.
                        r.ReleaseEdge(agent.Id, agent.Edges[k]);
                        r.ReleaseNode(agent.Id, agent.Nodes[k]);
                        agent.Step++;
                        agent.At = agent.Nodes[agent.Step];
                        if (agent.Arrived) r.ReleaseAll(agent.Id);   // off the taxiways (lined up, parked)
                    }
                    if (r.FindDeadlock(out int victim))
                    {
                        Agent v = agents[victim];
                        int k = v.Step;
                        int avoid = v.Edges[k];
                        // Take another way from the node it stands on (keeping only that), or tell it has none.
                        var nodes = new List<int>();
                        var edges = new List<int>();
                        if (TaxiRouter.Route(g, v.At, v.Goal, (e, from) => e == avoid || r.Against(e, from) ? 1e4f : 0f, nodes, edges) &&
                            !edges.Contains(avoid))
                        {
                            r.ReleaseAll(v.Id);
                            v.Nodes.Clear();
                            v.Nodes.AddRange(nodes);
                            v.Edges.Clear();
                            v.Edges.AddRange(edges);
                            v.Step = 0;
                            r.TryAdvance(v.Id, TaxiPriority.Departing, v.Nodes, v.Edges, 0, 0);
                        }
                        else r.NoDetour(v.Id);
                    }
                    var standing = new HashSet<int>();
                    foreach (Agent agent in agents)
                    {
                        if (agent.Arrived) continue;
                        Assert.True(standing.Add(agent.At), $"seed {seed} tick {tick}: two agents on node {agent.At}");
                        Assert.Equal(agent.Id, r.OwnerOfNode(agent.At));
                    }
                }
                if (mustArrive)
                    Assert.True(agents.TrueForAll(x => x.Arrived), $"seed {seed}: {agents.FindAll(x => !x.Arrived).Count} agents never arrived");
            }
        }
    }
}
