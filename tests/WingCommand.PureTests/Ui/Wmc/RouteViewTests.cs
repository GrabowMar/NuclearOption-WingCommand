using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class RouteViewTests
    {
        private readonly List<RouteLeg> legs = new List<RouteLeg>();
        private readonly List<RouteRing> rings = new List<RouteRing>();

        [Fact]
        public void NoTaskDrawsNothing()
        {
            // Review focus 5: a merged, completed or emptied element has nothing left on the map.
            RouteView.Task(null, -1, Vec3.Zero, 200f, legs, rings);
            RouteView.Task(WingTask.Form(), 0, Vec3.Zero, 200f, legs, rings);
            Assert.Empty(legs);
            Assert.Empty(rings);
        }

        [Fact]
        public void ARouteDrawsTheLegsLeftWithCumulativeDistanceAndEta()
        {
            WingTask t = WingTask.Route(Waypoint.At(0f, 10000f), Waypoint.At(0f, 20000f), Waypoint.At(0f, 30000f));
            RouteView.Task(t, 1, new Vec3(0f, 1000f, 12000f), 200f, legs, rings);
            Assert.Equal(2, legs.Count);                  // lead → 2, 2 → 3
            Assert.Equal(2, legs[0].Number);
            Assert.Equal(8f, legs[0].Km, 3);
            Assert.Equal(40f, legs[0].Eta, 3);
            Assert.Equal(3, legs[1].Number);
            Assert.Equal(18f, legs[1].Km, 3);
            Assert.Equal(90f, legs[1].Eta, 3);
            Assert.Equal("3 · 18.0 km · 1:30", RouteView.Label(legs[1]));
        }

        [Fact]
        public void AnOrbitArrivalAndAnOrbitTaskDrawRings()
        {
            Waypoint p = Waypoint.At(0f, 10000f);
            p.Action = ArrivalAction.Orbit;
            RouteView.Task(WingTask.Move(p), 0, Vec3.Zero, 200f, legs, rings);
            Assert.Single(rings);
            Assert.Equal(TaskLead.OrbitRadius(200f), rings[0].Radius, 3);
            legs.Clear();
            rings.Clear();
            // Beyond the orbit radius (~11 km at 200 m/s): the lead's way in is drawn and labelled.
            RouteView.Task(WingTask.Orbit(Waypoint.At(30000f, 0f)), 0, Vec3.Zero, 200f, legs, rings);
            Assert.Single(rings);
            Assert.Single(legs);
            Assert.Equal(1, legs[0].Number);
        }

        [Fact]
        public void ALoopPatrolClosesAndAPingPongDoesNot()
        {
            WingTask loop = WingTask.Patrol(true, Waypoint.At(0f, 0f), Waypoint.At(0f, 10000f), Waypoint.At(10000f, 10000f));
            RouteView.Task(loop, 0, new Vec3(0f, 0f, -5000f), 200f, legs, rings);
            Assert.Equal(4, legs.Count);                   // lead → 1, 1 → 2, 2 → 3, 3 → 1 (closing)
            Assert.True(legs[3].Closing);
            Assert.True(float.IsNaN(legs[2].Eta));         // one lap is not a schedule: ETA only to the next point
            legs.Clear();
            WingTask pp = WingTask.Patrol(false, Waypoint.At(0f, 0f), Waypoint.At(0f, 10000f));
            RouteView.Task(pp, 0, new Vec3(0f, 0f, -5000f), 200f, legs, rings);
            Assert.Equal(2, legs.Count);
            Assert.False(legs[1].Closing);
        }

        [Fact]
        public void TheDraftDrawsFromTheScopeWithoutAnEtaWhenTheSpeedIsUnknown()
        {
            var d = new RouteDraft();
            d.Add(0f, 3000f);
            d.Add(4000f, 3000f);
            RouteView.Draft(d, Vec3.Zero, 0f, legs);
            Assert.Equal(2, legs.Count);
            Assert.Equal(7f, legs[1].Km, 3);
            Assert.True(float.IsNaN(legs[1].Eta));
            Assert.Equal("2 · 7.0 km", RouteView.Label(legs[1]));
            d.Add(4000f, 0f);
            d.CycleLoop();
            legs.Clear();
            RouteView.Draft(d, Vec3.Zero, 0f, legs);
            Assert.Equal(4, legs.Count);
            Assert.True(legs[3].Closing);
        }

        [Fact]
        public void APatrolLabelsEachPointOnce()
        {
            // Review P4 I2: past its first point, the lead's leg and the circuit leg end at the same point; one label.
            WingTask loop = WingTask.Patrol(true, Waypoint.At(0f, 0f), Waypoint.At(0f, 10000f), Waypoint.At(10000f, 10000f));
            RouteView.Task(loop, 1, new Vec3(0f, 0f, 4000f), 200f, legs, rings);
            int twos = 0;
            foreach (RouteLeg l in legs)
                if (l.Number == 2) twos++;
            Assert.Equal(1, twos);
            Assert.Equal(2, legs[0].Number);               // the lead's leg keeps it, with the ETA
            Assert.False(float.IsNaN(legs[0].Eta));
        }
    }
}
