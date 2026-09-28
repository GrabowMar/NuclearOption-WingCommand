using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class TaxiReservationsTests
    {
        // A straight line A(0)-B(100)-C(200)-D(300) along +X, plus a spur B-E north.
        private static TaxiGraph Line() => TaxiGraph.Build(new AirbaseSample
        {
            Name = "line",
            Roads = new[]
            {
                new[] { new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f) },
                new[] { new Vec3(100f, 0f, 0f), new Vec3(200f, 0f, 0f) },
                new[] { new Vec3(200f, 0f, 0f), new Vec3(300f, 0f, 0f) },
                new[] { new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 100f) },
            },
        });

        private static (List<int> nodes, List<int> edges) Path(TaxiGraph g, float fromX, float fromZ, float toX, float toZ)
        {
            var nodes = new List<int>();
            var edges = new List<int>();
            Assert.True(TaxiRouter.Route(g, g.NearestNode(new Vec3(fromX, 0f, fromZ)), g.NearestNode(new Vec3(toX, 0f, toZ)), null, nodes, edges));
            return (nodes, edges);
        }

        [Fact]
        public void AnOwnerHoldsEveryEdgeAndNodeItClaims()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var (nodes, edges) = Path(g, 0f, 0f, 300f, 0f);
            Assert.Equal(6, r.TryAdvance(1, TaxiPriority.Departing, nodes, edges, 0, 3));
            foreach (int e in edges) Assert.Equal(1, r.OwnerOfEdge(e));
            foreach (int n in nodes) Assert.Equal(1, r.OwnerOfNode(n));
        }

        [Fact]
        public void ASecondOwnerMayUseItsEdgeButStopsBeforeANodeTheFirstHolds()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var first = Path(g, 0f, 0f, 300f, 0f);
            r.TryAdvance(1, TaxiPriority.Departing, first.nodes, first.edges, 0, 3);
            var second = Path(g, 100f, 100f, 200f, 0f);   // E → B → C: B is held by 1
            Assert.Equal(1, r.TryAdvance(2, TaxiPriority.Departing, second.nodes, second.edges, 0, 2));
        }

        [Fact]
        public void AFollowerSharesTheLeadersEdgeInTheSameDirectionButNotItsFarNode()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var leader = Path(g, 100f, 0f, 300f, 0f);
            Assert.Equal(4, r.TryAdvance(1, TaxiPriority.Departing, leader.nodes, leader.edges, 0, 2));
            r.ReleaseNode(1, leader.nodes[0]);   // the leader has passed B and is on B-C
            var follower = Path(g, 0f, 0f, 300f, 0f);
            // A-B, B, then B-C behind the leader; C is still the leader's.
            Assert.Equal(3, r.TryAdvance(2, TaxiPriority.Departing, follower.nodes, follower.edges, 0, 3));
            Assert.Equal(1, r.Ahead(2, follower.edges[1]));
            Assert.Equal(-1, r.Ahead(1, leader.edges[0]));
        }

        [Fact]
        public void OnlyTheHeadOfAConvoyMayTakeTheNodeAtTheEndOfItsEdge()
        {
            // Seen on a real field: a member joining a long edge behind others asked first for the node at its far end
            // and took it, so the convoy ahead could not reach the hold-short.
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var route = Path(g, 0f, 0f, 300f, 0f);   // A, A-B, B, ...
            int b = route.nodes[1];
            r.TryAdvance(3, TaxiPriority.Departing, new[] { b }, new int[0], 0, 0);   // someone holds B
            Assert.Equal(1, r.TryAdvance(1, TaxiPriority.Departing, route.nodes, route.edges, 0, 1));   // A, A-B
            r.ReleaseNode(1, route.nodes[0]);                                                       // 1 is past A
            Assert.Equal(1, r.TryAdvance(2, TaxiPriority.Departing, route.nodes, route.edges, 0, 1));   // A, A-B behind 1
            r.ReleaseAll(3);                                                                        // B comes free
            Assert.Equal(1, r.TryAdvance(2, TaxiPriority.Departing, route.nodes, route.edges, 0, 1));
            Assert.NotEqual(2, r.OwnerOfNode(b));
            // The leader, past A (which 2 now holds), claims on from its edge.
            Assert.Equal(2, r.TryAdvance(1, TaxiPriority.Departing, route.nodes, route.edges, 0, 1, claimStart: false));
            Assert.Equal(1, r.OwnerOfNode(b));
        }

        [Fact]
        public void OppositeDirectionsNeverShareAnEdge()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var east = Path(g, 100f, 0f, 200f, 0f);
            r.TryAdvance(1, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);   // B, B-C, C
            r.ReleaseNode(1, east.nodes[1]);                                        // C is free again, 1 is still on B-C
            var west = Path(g, 300f, 0f, 0f, 0f);
            // D-C and C granted; C-B is in use eastbound.
            Assert.Equal(2, r.TryAdvance(2, TaxiPriority.TaxiIn, west.nodes, west.edges, 0, 3));
        }

        [Fact]
        public void ReleasingFreesClaimsOneByOneOrAllAtOnce()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var (nodes, edges) = Path(g, 0f, 0f, 300f, 0f);
            r.TryAdvance(1, TaxiPriority.Departing, nodes, edges, 0, 3);
            r.ReleaseEdge(1, edges[0]);
            r.ReleaseNode(1, nodes[0]);
            Assert.Equal(-1, r.OwnerOfEdge(edges[0]));
            Assert.Equal(-1, r.OwnerOfNode(nodes[0]));
            Assert.Equal(1, r.OwnerOfEdge(edges[2]));
            r.ReleaseAll(1);
            Assert.Equal(-1, r.OwnerOfEdge(edges[2]));
            Assert.Equal(-1, r.OwnerOfNode(nodes[3]));
        }

        [Fact]
        public void ABlockedEdgeIsNeverGranted()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var (nodes, edges) = Path(g, 0f, 0f, 300f, 0f);
            r.Block(edges[1], true);
            Assert.True(r.Blocked(edges[1]));
            Assert.Equal(2, r.TryAdvance(1, TaxiPriority.Landing, nodes, edges, 0, 3));
        }

        [Fact]
        public void AWaitCycleIsADeadlockWhoseVictimHasTheLowerPriority()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var east = Path(g, 0f, 0f, 300f, 0f);
            var west = Path(g, 300f, 0f, 0f, 0f);
            r.TryAdvance(1, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);   // A, A-B, B
            r.TryAdvance(2, TaxiPriority.TaxiIn, west.nodes, west.edges, 0, 1);      // D, D-C, C
            r.TryAdvance(1, TaxiPriority.Departing, east.nodes, east.edges, 1, 1);   // B-C, then waits for C (2)
            r.TryAdvance(2, TaxiPriority.TaxiIn, west.nodes, west.edges, 1, 1);      // C-B is eastbound: waits for 1
            Assert.True(r.FindDeadlock(out int victim));
            Assert.Equal(2, victim);
            r.ReleaseAll(2);
            Assert.False(r.FindDeadlock(out _));
        }

        /// <summary>Head-on on B-C: 2 (eastbound, on A-B) waits for node C; 1 (westbound, on D-C) holds C and waits to
        /// enter C-B.</summary>
        private static TaxiReservations HeadOn(TaxiGraph g)
        {
            var r = new TaxiReservations(g);
            var east = Path(g, 0f, 0f, 300f, 0f);
            var west = Path(g, 300f, 0f, 0f, 0f);
            r.TryAdvance(2, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);
            r.TryAdvance(1, TaxiPriority.Departing, west.nodes, west.edges, 0, 1);
            r.TryAdvance(2, TaxiPriority.Departing, east.nodes, east.edges, 1, 1);
            r.TryAdvance(1, TaxiPriority.Departing, west.nodes, west.edges, 1, 1);
            return r;
        }

        [Fact]
        public void AMemberWaitingToEnterAnEdgeIsTheVictimBeforeOneWaitingForANode()
        {
            // Review M3a #4: the victim was picked by id alone, so the member facing a held node had to turn around
            // while the one at the junction could have taken another way.
            var r = HeadOn(Line());
            Assert.True(r.FindDeadlock(out int victim));
            Assert.Equal(1, victim);
        }

        [Fact]
        public void AVictimWithNoOtherWayIsPassedOver()
        {
            var r = HeadOn(Line());
            r.NoDetour(1);
            Assert.True(r.FindDeadlock(out int victim));
            Assert.Equal(2, victim);
        }

        [Fact]
        public void ASoleUserMayTurnBackOnItsEdgeButAConvoyMayNot()
        {
            TaxiGraph g = Line();
            var r = new TaxiReservations(g);
            var east = Path(g, 0f, 0f, 100f, 0f);   // A, A-B, B
            var back = Path(g, 100f, 0f, 0f, 0f);   // B, B-A, A
            r.TryAdvance(1, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);
            Assert.Equal(2, r.TryAdvance(1, TaxiPriority.Departing, back.nodes, back.edges, 0, 1));
            Assert.True(r.Against(east.edges[0], east.nodes[0]), "A-B is now westbound");
            r.ReleaseAll(1);
            r.ReleaseAll(2);
            r.TryAdvance(1, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);
            r.ReleaseNode(1, east.nodes[0]);
            r.TryAdvance(2, TaxiPriority.Departing, east.nodes, east.edges, 0, 1);   // A, then A-B behind 1
            r.ReleaseNode(1, east.nodes[1]);
            Assert.True(r.TryAdvance(1, TaxiPriority.Departing, back.nodes, back.edges, 0, 1) < 2, "1 has a follower on A-B");
        }
    }
}
