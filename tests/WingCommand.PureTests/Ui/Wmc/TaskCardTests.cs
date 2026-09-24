using Xunit;

namespace WingCommand.PureTests
{
    public class TaskCardTests
    {
        [Fact]
        public void TaskCardHandlesNoPointsAndNullTask()
        {
            Assert.Equal("FORM · on you", TaskCard.Text(null, -1, Vec3.Zero, 200f));
            Assert.Equal("FORM · on you", TaskCard.Text(WingTask.Form(), -1, Vec3.Zero, 200f));
            Assert.Equal("ROUTE · no points", TaskCard.Text(new WingTask { Kind = TaskKind.Route }, 0, Vec3.Zero, 200f));
        }

        [Fact]
        public void RouteShowsLegDistanceEtaAndFollowOn()
        {
            WingTask t = WingTask.Route(Waypoint.At(0f, 10000f), Waypoint.At(0f, 20000f));
            Assert.Equal("ROUTE · leg 2/2 · 20.0 km · ETA 1:40 · then ORBIT", TaskCard.Text(t, 1, Vec3.Zero, 200f));
            t.Then = FollowOn.Form;
            Assert.Equal("ROUTE · leg 1/2 · 10.0 km · ETA 0:50 · then FORM", TaskCard.Text(t, 0, Vec3.Zero, 200f));
        }

        [Fact]
        public void PatrolNamesItsCycleAndSlowLeadHasNoEta()
        {
            WingTask t = WingTask.Patrol(true, Waypoint.At(0f, 5000f), Waypoint.At(5000f, 5000f));
            Assert.Equal("PATROL · leg 1/2 · 5.0 km · ETA — · LOOP", TaskCard.Text(t, 0, Vec3.Zero, 0f));
            t = WingTask.Patrol(false, Waypoint.At(0f, 5000f), Waypoint.At(5000f, 5000f));
            Assert.EndsWith("PING-PONG", TaskCard.Text(t, 0, Vec3.Zero, 100f));
        }

        [Fact]
        public void OrbitSaysOnStationInsideTheArriveRadius()
        {
            WingTask t = WingTask.Orbit(Waypoint.At(0f, 500f), 120f);
            Assert.Equal("ORBIT · on station · for 2:00", TaskCard.Text(t, -1, Vec3.Zero, 200f));
            t = WingTask.Orbit(Waypoint.At(0f, 4000f));
            Assert.Equal("ORBIT · 4.0 km · ETA 0:20", TaskCard.Text(t, -1, Vec3.Zero, 200f));
        }

        [Fact]
        public void DistanceIsHorizontal()
        {
            WingTask t = WingTask.Move(Waypoint.At(3000f, 4000f));
            Assert.StartsWith("MOVE · leg 1/1 · 5.0 km", TaskCard.Text(t, 0, new Vec3(0f, 9000f, 0f), 250f));
        }
    }
}
