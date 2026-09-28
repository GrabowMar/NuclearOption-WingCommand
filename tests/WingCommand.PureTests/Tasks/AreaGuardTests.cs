using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>CAP and SWEEP (program R8 / bezel v2 A1): an orbit or a loop that guards an area — hostile aircraft entering it are
    /// engaged, and the element's combat is leashed to the area instead of the wing's anchor.</summary>
    public class AreaGuardTests
    {
        private static readonly Waypoint At = new Waypoint { X = 1000f, Z = -2000f, Altitude = 3000f, Speed = float.NaN };

        [Fact]
        public void ACapIsAnOrbitThatGuardsItsPoint()
        {
            WingTask t = WingTask.Cap(At, 8000f);
            Assert.Equal(TaskKind.Orbit, t.Kind);
            Assert.Equal(8000f, t.GuardRadius);
            Assert.Equal(1000f, t.Center.X);
            Assert.Equal(At.X, t.Points[0].X);
            Assert.Equal("CAP", AreaGuard.Word(t));
        }

        [Fact]
        public void ASweepIsALoopRoundTheAreaAtItsHeight()
        {
            WingTask t = WingTask.Sweep(At, 12000f);
            Assert.Equal(TaskKind.Patrol, t.Kind);
            Assert.True(t.Loop);
            Assert.Equal(AreaGuard.SweepPoints, t.Points.Length);
            foreach (Waypoint p in t.Points)
            {
                float d = new Vec3(p.X - At.X, 0f, p.Z - At.Z).Length;
                Assert.Equal(AreaGuard.SweepCircuit * 12000f, d, 1);
                Assert.Equal(3000f, p.Altitude);
            }
            Assert.Equal("SWEEP", AreaGuard.Word(t));
        }

        [Fact]
        public void TheRadiusStaysBetweenTwoAndFortyKilometres()
        {
            Assert.Equal(AreaGuard.MinRadius, WingTask.Cap(At, 10f).GuardRadius);
            Assert.Equal(AreaGuard.MaxRadius, WingTask.Sweep(At, 1e6f).GuardRadius);
        }

        [Fact]
        public void AnAircraftInsideTheRadiusIsTheAreasWhateverItsHeight()
        {
            WingTask t = WingTask.Cap(At, 8000f);
            Assert.True(AreaGuard.Inside(t, new Vec3(1000f + 7000f, 9000f, -2000f)));
            Assert.False(AreaGuard.Inside(t, new Vec3(1000f + 9000f, 3000f, -2000f)));
            Assert.False(AreaGuard.Inside(WingTask.Orbit(At), new Vec3(1000f, 3000f, -2000f)));
            Assert.False(AreaGuard.Inside(null, Vec3.Zero));
        }

        [Fact]
        public void TheCombatLeashIsTheAreaAndAHalfNeverUnderTenKilometres()
        {
            Assert.Equal(12000f, AreaGuard.Leash(WingTask.Cap(At, 8000f)));
            Assert.Equal(AreaGuard.MinLeash, AreaGuard.Leash(WingTask.Cap(At, 2000f)));
            Assert.Equal(0f, AreaGuard.Leash(WingTask.Orbit(At)));
            Assert.Equal(0f, AreaGuard.Leash(null));
        }

        [Fact]
        public void TheSupervisorTakesAMemberBackAtItsOwnLeash()
        {
            var s = new CombatSituation { AnchorPresent = true, AnchorDistance = 15000f, Ammo = 1f, LeashMetres = 12000f };
            Assert.True(CombatSupervisor.TakeBack(s, out TransitionReason reason));
            Assert.Equal(TransitionReason.Leash, reason);
            s.LeashMetres = 0f;
            Assert.False(CombatSupervisor.TakeBack(s, out _));
        }

        [Fact]
        public void TheTaskWordsSayCapAndSweep()
        {
            Assert.StartsWith("CAP", TaskCard.Short(WingTask.Cap(At, 8000f), -1, Vec3.Zero, 100f));
            Assert.StartsWith("SWEEP", TaskCard.Short(WingTask.Sweep(At, 12000f), 0, Vec3.Zero, 100f));
            Assert.StartsWith("ORBIT", TaskCard.Short(WingTask.Orbit(At), -1, Vec3.Zero, 100f));
        }

        [Fact]
        public void TheMapDrawsTheGuardedArea()
        {
            var legs = new List<RouteLeg>();
            var rings = new List<RouteRing>();
            RouteView.Task(WingTask.Sweep(At, 12000f), 0, Vec3.Zero, 100f, legs, rings);
            Assert.Contains(rings, r => System.Math.Abs(r.Radius - 12000f) < 1f && System.Math.Abs(r.X - At.X) < 1f);
            rings.Clear();
            RouteView.Task(WingTask.Cap(At, 8000f), -1, Vec3.Zero, 100f, legs, rings);
            Assert.Contains(rings, r => System.Math.Abs(r.Radius - 8000f) < 1f);
        }

        [Fact]
        public void CapAndSweepArmTheMapAndPlaceAtAClick()
        {
            Assert.Equal(MapClick.Cap, MapOrders.Resolve(MapMode.Cap, false, MapPointer.Empty, false));
            Assert.Equal(MapClick.Sweep, MapOrders.Resolve(MapMode.Sweep, true, MapPointer.Enemy, false));
            Assert.Equal("CAP", MapOrders.Label(MapMode.Cap));
            Assert.Equal("SWEEP", MapOrders.Label(MapMode.Sweep));
        }

        [Fact]
        public void TheGridsCapAndSweepCellsAreLive()
        {
            GridCell sweep = OrderGrid.At(0, 3, false), cap = OrderGrid.At(1, 2, false);
            Assert.True(sweep.Built);
            Assert.True(cap.Built);
            Assert.Equal(MapMode.Sweep, sweep.Map);
            Assert.Equal(MapMode.Cap, cap.Map);
            Assert.Null(OrderGrid.Why(cap, true, 2, false));
        }

        [Fact]
        public void TheAreaDoesNotReEngageAMemberBeyondItsLeashOrJustTakenBack()
        {
            // Review (combat): a member taken back beyond the leash was re-engaged 0.5 s later, and again, so its missile defence
            // never started; an outnumbered fall-back lasted half a second.
            WingTask cap = WingTask.Cap(Waypoint.At(0f, 0f), 8000f);
            Assert.True(AreaGuard.MayEngage(cap, 5000f, float.PositiveInfinity));
            Assert.False(AreaGuard.MayEngage(cap, 12500f, float.PositiveInfinity));    // beyond 1.5 x 8 km
            Assert.False(AreaGuard.MayEngage(cap, 5000f, AreaGuard.ReEngageSeconds - 1f));
            Assert.True(AreaGuard.MayEngage(cap, 5000f, AreaGuard.ReEngageSeconds + 1f));
        }
    }
}
