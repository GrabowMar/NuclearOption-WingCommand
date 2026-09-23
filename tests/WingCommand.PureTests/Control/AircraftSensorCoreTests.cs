using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class AircraftSensorCoreTests
    {
        private const float Dt = 1f / 60f;

        private static RawAircraftSample Level(Vec3 vel, float bankDeg = 0f)
        {
            float b = bankDeg * Scalar.Deg2Rad;
            Vec3 fwd = vel.Normalized;
            Vec3 horizon = Vec3.Cross(Vec3.Up, fwd).Normalized;
            // Rotate the wing about the nose: + bank puts the right wing down.
            Vec3 right = horizon * (float)Math.Cos(b) - Vec3.Up * (float)Math.Sin(b);
            Vec3 up = Vec3.Cross(fwd, right);
            return new RawAircraftSample
            {
                Pos = new Vec3(1000f, 2000f, -3000f), Vel = vel, Fwd = fwd, Right = right, Up = up,
                AirDensity = 1f, RadarAlt = 2000f, GroundSpeed = vel.Length, Throttle = 0.6f,
            };
        }

        [Fact]
        public void FixtureUpVectorPointsUpInLevelFlight() =>
            Assert.Equal(1f, Level(new Vec3(0f, 0f, 200f)).Up.Y, 4);

        [Theory]
        [InlineData(30f)]
        [InlineData(-45f)]
        [InlineData(120f)]
        public void BankIsPositiveRightWingDown(float bank) =>
            Assert.Equal(bank, new AircraftSensorCore().Read(Level(new Vec3(0f, 0f, 200f), bank), Dt).BankDeg, 2);

        [Fact]
        public void RotorSpeedPassesThrough()
        {
            RawAircraftSample r = Level(new Vec3(0f, 0f, 60f));
            r.RotorRpm = 0.96f;
            Assert.Equal(0.96f, new AircraftSensorCore().Read(r, 1f / 60f).RotorRpm);
        }

        [Fact]
        public void BankIsZeroWithTheNoseVertical()
        {
            RawAircraftSample r = Level(new Vec3(0f, 0f, 200f));
            Assert.Equal(0f, AircraftSensorCore.BankOf(Vec3.Up, r.Right));
        }

        [Fact]
        public void BodyRatesUsePureSigns()
        {
            RawAircraftSample r = Level(new Vec3(0f, 0f, 200f));
            // Unity local: +x pitches the nose down, +y yaws right, +z rolls left.
            r.AngularVelocity = new Vec3(-0.1f, 0.2f, -1f);
            AircraftState s = new AircraftSensorCore().Read(r, Dt);
            Assert.Equal(5.73f, s.Q, 2);    // nose up
            Assert.Equal(11.46f, s.R, 2);   // nose right
            Assert.Equal(57.3f, s.P, 1);    // right roll
        }

        [Fact]
        public void LoadFactorIsOneInSteadyLevelFlight()
        {
            var core = new AircraftSensorCore();
            AircraftState s = default;
            for (int i = 0; i < 30; i++) s = core.Read(Level(new Vec3(0f, 0f, 200f)), Dt);
            Assert.Equal(1f, s.Nz, 3);
        }

        [Fact]
        public void LoadFactorIsTwoInASixtyDegreeLevelTurn()
        {
            // Heading north, right turn: centripetal acceleration g·tan60 toward +x, wings banked 60° right.
            var core = new AircraftSensorCore();
            float a = Scalar.G * (float)Math.Tan(60f * Scalar.Deg2Rad);
            AircraftState s = default;
            for (int i = 0; i < 60; i++)
            {
                RawAircraftSample r = Level(new Vec3(0f, 0f, 200f), 60f);
                r.Vel = new Vec3(a * i * Dt, 0f, 200f);
                s = core.Read(r, Dt);
            }
            Assert.Equal(2f, s.Nz, 1);
            Assert.Equal(a, s.Acc.X, 0);
        }

        [Fact]
        public void WindReducesAirspeedAndDynamicPressure()
        {
            RawAircraftSample r = Level(new Vec3(0f, 0f, 200f));
            r.Wind = new Vec3(0f, 0f, 20f);
            AircraftState s = new AircraftSensorCore().Read(r, Dt);
            Assert.Equal(180f, s.Tas, 3);
            Assert.Equal(0.5f * 180f * 180f, s.Qbar, 0);
        }

        [Fact]
        public void SideslipIsPositiveWhenMovingTowardTheRightWing()
        {
            RawAircraftSample r = Level(new Vec3(0f, 0f, 200f));
            r.Vel = new Vec3(20f, 0f, 200f);
            Assert.True(new AircraftSensorCore().Read(r, Dt).SideslipDeg > 5f);
        }

        [Fact]
        public void FlyByWireGateUsesTheAirframesOwnMinimums()
        {
            // The CI-22's ControlsFilter filters down to 10 m/s and 0 m AGL; a fixed 25 m/s gate released a wingman
            // whose fly-by-wire was still working.
            RawAircraftSample slow = Level(new Vec3(0f, 0f, 15f));
            slow.HasFbwGate = true;
            slow.FbwGateMinSpeed = 10f;
            slow.FbwGateMinRadarAlt = 0f;
            slow.RadarAlt = 0.5f;
            Assert.True(new AircraftSensorCore().Read(slow, Dt).FbwActive);
            slow.FbwGateMinSpeed = 20f;
            Assert.False(new AircraftSensorCore().Read(slow, Dt).FbwActive);
        }

        [Fact]
        public void AHelicoptersFlyByWireHasNoSpeedGate()
        {
            // The helo filter filters at any speed (native C4); its base-class gate fields must not release a hover.
            RawAircraftSample hover = Level(new Vec3(0f, 0f, 0f));
            hover.HasFbwGate = true;
            hover.FbwGateMinSpeed = 25f;
            hover.FbwAlwaysOn = true;
            Assert.True(new AircraftSensorCore().Read(hover, Dt).FbwActive);
        }

        [Fact]
        public void FlyByWireIsInactiveWhenSlowOrOnTheGround()
        {
            Assert.False(new AircraftSensorCore().Read(Level(new Vec3(0f, 0f, 20f)), Dt).FbwActive);
            RawAircraftSample ground = Level(new Vec3(0f, 0f, 80f));
            ground.RadarAlt = 0.5f;
            Assert.False(new AircraftSensorCore().Read(ground, Dt).FbwActive);
            Assert.True(new AircraftSensorCore().Read(Level(new Vec3(0f, 0f, 200f)), Dt).FbwActive);
        }

        [Fact]
        public void AZeroDtReadPeeksWithoutAdvancingTheFilter()
        {
            var a = new AircraftSensorCore();
            var b = new AircraftSensorCore();
            RawAircraftSample s0 = Level(new Vec3(0f, 0f, 200f)), s1 = Level(new Vec3(0f, 0f, 201f));
            a.Read(s0, Dt);
            b.Read(s0, Dt);
            b.Read(Level(new Vec3(50f, 0f, 150f)), 0f);
            Assert.Equal(a.Read(s1, Dt).Acc, b.Read(s1, Dt).Acc);
        }

        [Fact]
        public void PositionPassesThroughUnchanged()
        {
            // The engine feeds global (floating-origin safe) positions; the core must not re-base them.
            AircraftState s = new AircraftSensorCore().Read(Level(new Vec3(0f, 0f, 200f)), Dt);
            Assert.Equal(new Vec3(1000f, 2000f, -3000f), s.Pos);
        }
    }
}
