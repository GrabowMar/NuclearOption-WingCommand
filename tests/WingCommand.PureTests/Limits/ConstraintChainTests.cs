using Xunit;

namespace WingCommand.PureTests
{
    public class ConstraintChainTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Fighter = new AirframeProfile();

        private static AircraftState At(float altitude, float speed = 200f, float vy = 0f, float bank = 0f) => new AircraftState
        {
            Pos = new Vec3(0f, altitude, 0f), Vel = new Vec3(0f, vy, speed), Tas = speed, BankDeg = bank, Nz = 1f,
            Qbar = Isa.DynamicPressure(altitude, speed),
        };

        private static LimitContext Floor(float floorY, float clearance = 60f, float aggression = 0f) =>
            new LimitContext { FloorY = floorY, Clearance = clearance, Aggression = aggression };

        [Fact]
        public void TerrainFloorRaisesADescentCommandAndSaysSo()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var g = new GuidanceCommand { VelCmd = new Vec3(0f, -40f, 200f) };
            // 50 m above the floor with 60 m clearance: 10 m inside the margin, so a climb is required.
            chain.ApplyAccel(ref g, At(50f), Floor(0f), Fighter, ref report);
            Assert.True(g.VelCmd.Y > -40f);
            Assert.True(g.Accel.Y > 0f);
            Assert.Equal(ConstraintId.Terrain, report.VerticalBy);
        }

        [Theory]
        [InlineData(10f)]
        [InlineData(60f)]
        [InlineData(200f)]
        [InlineData(500f)]
        public void TerrainFloorNeverAllowsASinkThatTriggersGcas(float margin)
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var g = new GuidanceCommand { VelCmd = new Vec3(0f, -300f, 250f) };
            chain.ApplyAccel(ref g, At(60f + margin, speed: 250f), Floor(0f), Fighter, ref report);
            float sink = -g.VelCmd.Y;
            var a = new AttitudeCommand { Nz = 1f };
            chain.ApplyAttitude(ref a, At(60f + margin, speed: 250f, vy: -sink), Floor(0f), Fighter, Dt, ref report);
            Assert.False(chain.GcasActive, $"allowed sink {sink:0.0} m/s at margin {margin} m triggers GCAS");
            Assert.True(sink > 0f);
        }

        [Fact]
        public void UnknownFloorLeavesTheCommandAlone()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var g = new GuidanceCommand { VelCmd = new Vec3(0f, -40f, 200f) };
            chain.ApplyAccel(ref g, At(100f), Floor(float.NaN), Fighter, ref report);
            Assert.Equal(-40f, g.VelCmd.Y);
            Assert.Equal(ConstraintId.None, report.VerticalBy);
        }

        [Fact]
        public void BankCeilingFollowsAggressionAndIsAttributed()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var a = new AttitudeCommand { BankDeg = 85f, Nz = 1f };
            chain.ApplyAttitude(ref a, At(3000f, bank: 0f), Floor(float.NaN, aggression: 0f), Fighter, Dt, ref report);
            Assert.Equal(ConstraintId.Envelope, report.BankBy);
            Assert.Equal(85f, report.BankRequested);
            Assert.Equal(60f, report.BankAllowed);
            Assert.StartsWith("BANK Envelope 60/85", report.Describe());
        }

        [Fact]
        public void BankCeilingKeepsTheVerticalLiftInsteadOfTheLoadFactor()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            // 80° at 4 g: vertical lift 4·cos 80° = 0.695 g. At the 60° ceiling the same vertical lift needs 1.39 g,
            // not 4 g (which would be a 2 g vertical surplus: a zoom).
            var a = new AttitudeCommand { BankDeg = 80f, Nz = 4f };
            chain.ApplyAttitude(ref a, At(3000f, speed: 250f, bank: 60f), Floor(float.NaN, aggression: 0f), Fighter, Dt, ref report);
            Assert.Equal(60f, a.BankDeg, 3);
            Assert.Equal(4f * (float)System.Math.Cos(80.0 * System.Math.PI / 180.0) / 0.5f, a.Nz, 2);
        }

        [Fact]
        public void LoadFactorIsLimitedByLiftAtLowSpeed()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var a = new AttitudeCommand { Nz = 6f };
            chain.ApplyAttitude(ref a, At(3000f, speed: 80f), Floor(float.NaN), Fighter, Dt, ref report);
            Assert.True(a.Nz <= Fighter.LiftLimitedG(80f) + 1e-3f);
            Assert.Equal(ConstraintId.Envelope, report.NzBy);
        }

        [Fact]
        public void LoadFactorLimitUsesEquivalentAirspeedAtAltitude()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            // 150 m/s true at 9 km is about 93 m/s equivalent: lift allows (93/55)² ≈ 2.9 g, not the
            // (150/55)² ≈ 7.4 g that true airspeed suggests.
            var a = new AttitudeCommand { Nz = 6f };
            chain.ApplyAttitude(ref a, At(9000f, speed: 150f), Floor(float.NaN), Fighter, Dt, ref report);
            Assert.InRange(a.Nz, 1f, 3f);
            Assert.Equal(ConstraintId.Envelope, report.NzBy);
        }

        [Fact]
        public void GcasTriggersInASteepDescentNearTheFloorAndReleasesWhenClimbing()
        {
            var chain = new ConstraintChain();
            var report = new BindingReport();
            var a = new AttitudeCommand { BankDeg = 70f, Nz = 1f };
            // 200 m up, sinking 80 m/s at 90° bank: roll-out + pull loses ~83 m, leaving (200−83−30)/80 ≈ 1.1 s.
            chain.ApplyAttitude(ref a, At(200f, vy: -80f, bank: 90f), Floor(0f), Fighter, Dt, ref report);
            Assert.True(a.Gcas);
            Assert.True(report.GcasActive);
            Assert.True(chain.GcasActive);
            var b = new AttitudeCommand { BankDeg = 70f, Nz = 1f };
            var r2 = new BindingReport();
            chain.ApplyAttitude(ref b, At(400f, vy: 20f), Floor(0f), Fighter, Dt, ref r2);
            Assert.False(chain.GcasActive);
        }

        [Fact]
        public void AuthoritySlewsAStepInBankCommand()
        {
            var chain = new ConstraintChain();
            chain.Track(At(3000f));
            var report = new BindingReport();
            var a = new AttitudeCommand { BankDeg = 60f, Nz = 1f };
            chain.ApplyAttitude(ref a, At(3000f), Floor(float.NaN, aggression: 1f), Fighter, Dt, ref report);
            Assert.Equal(Fighter.RollRateMaxDps * Dt, a.BankDeg, 3);
            Assert.Equal(ConstraintId.Authority, report.BankBy);
        }

        [Fact]
        public void AuthoritySlewTakesShortestWayAcross180()
        {
            // From 179° toward −170° is +11° across ±180, so a 2° step lands on −179°, not 177°.
            Assert.Equal(-179f, ConstraintChain.SlewBank(179f, -170f, 2f), 3);
            Assert.Equal(179f, ConstraintChain.SlewBank(-179f, 170f, 2f), 3);
            Assert.Equal(12f, ConstraintChain.SlewBank(10f, 60f, 2f), 3);
        }
    }
}
