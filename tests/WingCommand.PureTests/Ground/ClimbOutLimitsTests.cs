using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Night-1 sim runs (2026-09-28): a jet leaves the runway below the loaded minimum speed its profile reckons; the speed
    /// priority (a slow aircraft sinks to win its speed back, made for formation flight at altitude) then pushed it back onto the
    /// runway at 105 m/s, because the climb-out flew without a terrain floor. The climb-out flies over the ground under it, a
    /// climb-out clearance above, and the wing's collision bias.</summary>
    public class ClimbOutLimitsTests
    {
        private static readonly AirframeProfile Jet = new AirframeProfile();

        private static AircraftState JustOff(float radarAlt, float eas) => new AircraftState
        {
            Pos = new Vec3(0f, 300f + radarAlt, 0f), Vel = new Vec3(0f, 1f, eas), Tas = eas, RadarAlt = radarAlt, Nz = 1f,
            Qbar = 0.5f * Isa.SeaLevelDensity * eas * eas,
        };

        [Theory]
        [InlineData(1.5f)]
        [InlineData(5f)]
        [InlineData(20f)]
        public void AClimbOutBelowItsMinimumSpeedNearTheGroundIsNeverToldToDescend(float radarAlt)
        {
            AircraftState s = JustOff(radarAlt, 0.8f * Jet.MinimumSpeed(1f));
            LimitContext ctx = GroundPilot.ClimbOutLimits(s, default, false);
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var g = new GuidanceCommand { VelCmd = new Vec3(0f, 5f, s.Tas) };
            chain.ApplyAccel(ref g, s, ctx, Jet, ref report);
            Assert.True(g.VelCmd.Y > 0f, $"told to go {g.VelCmd.Y:0.0} m/s at {radarAlt} m");
        }

        [Fact]
        public void TheFloorIsTheGroundUnderTheJetOrTheWingsFloorWhicheverIsHigher()
        {
            AircraftState s = JustOff(10f, 80f);
            Assert.Equal(300f, GroundPilot.ClimbOutLimits(s, default, false).FloorY, 3);
            var air = new LimitContext { FloorY = 340f, CollisionBias = new Vec3(1f, 0f, 0f) };
            LimitContext ctx = GroundPilot.ClimbOutLimits(s, air, true);
            Assert.Equal(340f, ctx.FloorY, 3);
            Assert.Equal(1f, ctx.CollisionBias.X, 3);
            Assert.Equal(GroundPilot.ClimbOutClearance, ctx.Clearance, 3);
            // No GCAS right off the runway: its recovery model would trip on the ground it just left.
            Assert.False(ctx.HasNearFloor);
        }
    }
}
