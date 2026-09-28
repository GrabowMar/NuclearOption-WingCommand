using Xunit;

namespace WingCommand.PureTests
{
    public class RouteDraftTests
    {
        [Fact]
        public void EmptyDraftIsRefused()
        {
            var d = new RouteDraft();
            Assert.False(d.TryTask(out WingTask t, out string why));
            Assert.Null(t);
            Assert.Equal("Add points on the map first", why);
        }

        [Fact]
        public void OnePointIsAMoveAndMoreAreARoute()
        {
            var d = new RouteDraft();
            d.Add(1000f, 2000f);
            Assert.True(d.TryTask(out WingTask t, out _));
            Assert.Equal(TaskKind.Move, t.Kind);
            d.Add(3000f, 4000f);
            Assert.True(d.TryTask(out t, out _));
            Assert.Equal(TaskKind.Route, t.Kind);
            Assert.Equal(2, t.Points.Length);
            Assert.Equal(3000f, t.Points[1].X);
        }

        [Fact]
        public void APatrolNeedsTwoPoints()
        {
            var d = new RouteDraft();
            d.Add(1000f, 2000f);
            d.CycleLoop();
            Assert.Equal(RouteLoop.Loop, d.Loop);
            Assert.False(d.TryTask(out _, out string why));
            Assert.Equal("A patrol needs two points", why);
            d.Add(3000f, 4000f);
            Assert.True(d.TryTask(out WingTask t, out _));
            Assert.Equal(TaskKind.Patrol, t.Kind);
            Assert.True(t.Loop);
            d.CycleLoop();
            Assert.True(d.TryTask(out t, out _));
            Assert.False(t.Loop);   // ping-pong
            d.CycleLoop();
            Assert.Equal(RouteLoop.Once, d.Loop);
        }

        [Fact]
        public void TheDraftStopsAtSixteen()
        {
            var d = new RouteDraft();
            for (int i = 0; i < RouteDraft.MaxPoints; i++) Assert.True(d.Add(i * 100f, 0f));
            Assert.False(d.Add(9999f, 0f));
            Assert.Equal(RouteDraft.MaxPoints, d.Count);
        }

        [Fact]
        public void NewPointsTakeTheDefaultsAndBecomeSelected()
        {
            var d = new RouteDraft();
            d.StepAltitude(+1);
            d.StepSpeed(+1);
            d.Add(0f, 0f);
            Assert.Equal(0, d.Selected);
            Assert.Equal(RouteDraft.Altitudes[1], d[0].Altitude);
            Assert.Equal(RouteDraft.Speeds[1], d[0].Speed);
            Assert.False(float.IsNaN(d.Altitude));
        }

        [Fact]
        public void SteppersEditTheSelectedPointOnly()
        {
            var d = new RouteDraft();
            d.Add(0f, 0f);
            d.Add(10f, 0f);
            d.Select(0);
            d.StepAltitude(+1);
            Assert.Equal(RouteDraft.Altitudes[1], d[0].Altitude);
            Assert.True(float.IsNaN(d[1].Altitude));
            Assert.True(float.IsNaN(d.Altitude));   // defaults untouched while a point is selected
            d.Select(0);                             // a second click unselects
            Assert.Equal(-1, d.Selected);
        }

        [Fact]
        public void TheActionCyclesThroughOrbitLandAndCargo()
        {
            var d = new RouteDraft();
            d.Add(0f, 0f);
            d.CycleAction();
            Assert.Equal(ArrivalAction.Orbit, d[0].Action);
            Assert.Equal(RouteDraft.OrbitSeconds, d[0].Seconds);
            Assert.Equal("ORBIT 2 MIN", RouteDraft.ActionText(d[0]));
            d.CycleAction();
            Assert.Equal(ArrivalAction.Land, d[0].Action);
            Assert.Equal(0f, d[0].Seconds);
            d.CycleAction();
            Assert.Equal(ArrivalAction.Cargo, d[0].Action);
            d.CycleAction();
            Assert.Equal(ArrivalAction.None, d[0].Action);
            Assert.Equal("—", RouteDraft.ActionText(d[0]));
        }

        [Fact]
        public void UndoRemoveAndClear()
        {
            var d = new RouteDraft();
            d.Add(0f, 0f);
            d.Add(10f, 0f);
            d.Add(20f, 0f);
            d.Undo();
            Assert.Equal(2, d.Count);
            Assert.Equal(1, d.Selected);
            d.Select(0);
            d.RemoveSelected();
            Assert.Equal(1, d.Count);
            Assert.Equal(10f, d[0].X);
            Assert.Equal(-1, d.Selected);
            d.CycleLoop();
            d.Clear();
            Assert.Equal(0, d.Count);
            Assert.Equal(RouteLoop.Once, d.Loop);
        }

        [Fact]
        public void LadderSteps()
        {
            float[] l = RouteDraft.Altitudes;
            Assert.Equal(l[1], RouteDraft.Step(l, float.NaN, +1));          // AUTO → first value
            Assert.True(float.IsNaN(RouteDraft.Step(l, l[1], -1)));          // first value → AUTO
            Assert.True(float.IsNaN(RouteDraft.Step(l, float.NaN, -1)));     // AUTO stays
            Assert.Equal(l[l.Length - 1], RouteDraft.Step(l, l[l.Length - 1], +1));
            Assert.Equal(1000f, RouteDraft.Step(l, 700f, +1));               // off-ladder snaps in the press direction
            Assert.Equal(600f, RouteDraft.Step(l, 700f, -1));
        }

        [Fact]
        public void ReadoutsAreWords()
        {
            Assert.Equal("AUTO", RouteDraft.AltitudeText(float.NaN));
            Assert.Equal("1500 m", RouteDraft.AltitudeText(1500f));
            Assert.Equal("AUTO", RouteDraft.SpeedText(float.NaN));
            Assert.Equal("540 km/h", RouteDraft.SpeedText(150f));
            Assert.Equal("ONCE", RouteDraft.LoopText(RouteLoop.Once));
            Assert.Equal("PING-PONG", RouteDraft.LoopText(RouteLoop.PingPong));
        }

        [Fact]
        public void TheRowWindowFollowsTheFocus()
        {
            Assert.Equal(0, RouteDraft.Window(5, 8, 3));
            Assert.Equal(0, RouteDraft.Window(16, 8, 2));
            Assert.Equal(5, RouteDraft.Window(16, 8, 8));
            Assert.Equal(8, RouteDraft.Window(16, 8, 15));
            Assert.Equal(0, RouteDraft.Window(16, 8, -1));
        }

        [Fact]
        public void LoadCopiesAndCaps()
        {
            var pts = new Waypoint[20];
            for (int i = 0; i < pts.Length; i++) pts[i] = Waypoint.At(i, 0f);
            var d = new RouteDraft();
            d.Load(pts, RouteLoop.PingPong);
            Assert.Equal(RouteDraft.MaxPoints, d.Count);
            Assert.Equal(RouteLoop.PingPong, d.Loop);
            pts[0].X = 500f;
            Assert.Equal(0f, d[0].X);
        }
    }
}
