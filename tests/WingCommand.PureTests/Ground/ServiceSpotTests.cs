using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class ServiceSpotTests
    {
        private const float Dt = 1f / 30f;

        [Fact]
        public void AServicePointIsANodeJoinedToTheNearestRoad()
        {
            // Review M3a #8: service-point launches joined the graph at the nearest node, 200 m across the grass.
            TaxiGraph g = TaxiGraph.Build(TestFields.WithServicePoint());
            int node = g.ServiceNode(0);
            Assert.True(node >= 0);
            Assert.Equal(NodeKind.Service, g.Kind(node));
            Assert.True((g.NodePos(node) - new Vec3(-200f, 0f, 300f)).Length < 1f);
            Assert.Single(g.EdgesOf(node));
            int join = g.OtherEnd(g.EdgesOf(node)[0], node);
            Assert.True((g.NodePos(join) - new Vec3(-150f, 0f, 300f)).Length < 1f, $"joined at {g.NodePos(join)}");
        }

        [Fact]
        public void AMemberLaunchedAtAServicePointTaxisOutAlongTheRoadNearIt()
        {
            var field = new FieldTraffic(TestFields.WithServicePoint(), 0, false);
            Pose spawn = field.Field.ServicePoints[0];
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, -1, field.Graph.ServiceNode(0));
            AirframeProfile p = Jet();
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            float maxZ = 0f;
            for (int i = 0; i < 120 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), p, pipeline, i * Dt, Dt, null, 0), Dt);
                maxZ = Math.Max(maxZ, plant.Pos.Z);
            }
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
            Assert.True(maxZ < 340f, $"went north to z {maxZ:0} (the nearest junction) instead of joining the taxiway beside it");
        }

        private static AirframeProfile Jet() => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 216f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = 11f,
            TakeoffSpeed = 70f,
        });

        [Fact]
        public void LaunchesQueueOnTheServicePointsWayOutAheadOfIt()
        {
            // Review M3a #8: every call started again at the first point, so a second call spawned on top of the first.
            // Beside or behind the point, the parked aircraft turned out across the next one (seen on the real fields).
            var field = new FieldTraffic(TestFields.WithServicePoint(), 0, false);
            var taken = new List<Vec3>();
            Func<Vec3, bool> occupied = at => taken.Exists(t => (t - at).Horizontal.Length < ServiceSpots.ClearRadius);
            Assert.True(ServiceSpots.Pick(field, occupied, out ServiceSpot first));
            Assert.Equal(field.Field.ServicePoints[0].Pos, first.Pose.Pos);
            Assert.Equal(field.Graph.ServiceNode(0), first.StartNode);
            taken.Add(first.Pose.Pos);
            Assert.True(ServiceSpots.Pick(field, occupied, out ServiceSpot second));
            // 30 m along the connector east towards the taxiway at x = -150, facing along it.
            Assert.True((second.Pose.Pos - new Vec3(-170f, 0f, 300f)).Length < 1f, $"at {second.Pose.Pos}");
            Assert.True(Vec3.Dot(second.Pose.Fwd, new Vec3(1f, 0f, 0f)) > 0.99f);
            taken.Add(second.Pose.Pos);
            Assert.True(ServiceSpots.Pick(field, occupied, out ServiceSpot third));
            // 60 m: past the junction, 10 m down the taxiway (south, towards the hold-short).
            Assert.True((third.Pose.Pos - new Vec3(-150f, 0f, 290f)).Length < 1f, $"at {third.Pose.Pos}");
            Assert.True(Vec3.Dot(third.Pose.Fwd, new Vec3(0f, 0f, -1f)) > 0.99f);
            Assert.NotEqual(field.Graph.ServiceNode(0), third.StartNode);
        }

        [Fact]
        public void NoSpotIsPickedWhenEveryPlaceIsTaken()
        {
            var field = new FieldTraffic(TestFields.WithServicePoint(), 0, false);
            Assert.False(ServiceSpots.Pick(field, _ => true, out _));
        }
    }
}
