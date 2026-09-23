using Xunit;

namespace WingCommand.PureTests
{
    public class FixedWingControllerTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Fighter = new AirframeProfile();

        private static AircraftState Level(float speed = 170f, float bank = 0f, float nz = 1f) => new AircraftState
        {
            Vel = new Vec3(0f, 0f, speed), Tas = speed, Qbar = Isa.DynamicPressure(2000f, speed),
            BankDeg = bank, Nz = nz, Fwd = Vec3.Forward, Dt = Dt, FbwActive = true,
        };

        private static AttitudeCommand Cmd(float bank = 0f, float nz = 1f, float energy = 0f, bool airbrake = true) =>
            new AttitudeCommand { BankDeg = bank, Nz = nz, EnergyRate = energy, AirbrakeAllowed = airbrake };

        [Fact]
        public void RightBankCommandGivesRightRollStick()
        {
            var c = new FixedWingController();
            ControlOutput o = c.Step(Cmd(bank: 30f), Level(), Fighter, Dt);
            Assert.True(o.Roll > 0f && o.Roll <= 1f);
        }

        [Fact]
        public void PitchFeedforwardInvertsTheFbwGCommand()
        {
            var c = new FixedWingController();
            ControlOutput o = c.Step(Cmd(nz: 2f), Level(), Fighter, Dt);
            // ff = (2 − 1)·max(170, 127.5)/(170·9) = 0.111, plus a small proportional term.
            Assert.InRange(o.Pitch, 0.11f, 0.3f);
        }

        [Fact]
        public void EnergyDeficitRaisesThrottleAboveTrim()
        {
            var c = new FixedWingController();
            AircraftState s = Level();
            ControlOutput o = c.Step(Cmd(energy: 20f), s, Fighter, Dt);
            Assert.True(o.Throttle > FixedWingController.TrimThrottle(s, Fighter, 1f));
        }

        [Fact]
        public void WithoutAfterburnerThrottleStaysAtOrBelowDryMaximum()
        {
            var c = new FixedWingController();
            ControlOutput o = default;
            for (int i = 0; i < 600; i++) o = c.Step(Cmd(energy: 500f), Level(), Fighter, Dt);
            Assert.True(o.Throttle <= FixedWingController.DryThrottleMax + 1e-5f);
        }

        [Fact]
        public void LargeEnergyExcessAtIdleLatchesAirbrakeWithZeroThrottleThenReleases()
        {
            var c = new FixedWingController();
            ControlOutput o = default;
            // −120 m/s of excess energy drives the energy PI to its floor at once; the latch needs 0.5 s more.
            for (int i = 0; i < 45; i++) o = c.Step(Cmd(energy: -120f), Level(300f), Fighter, Dt);
            Assert.True(o.Airbrake);
            Assert.Equal(0f, o.Throttle);
            // The brake stays open for its minimum on-time, then releases once idle alone meets the command.
            for (int i = 0; i < 30; i++) o = c.Step(Cmd(energy: 0f), Level(300f), Fighter, Dt);
            Assert.True(o.Airbrake);
            for (int i = 0; i < 40; i++) o = c.Step(Cmd(energy: 0f), Level(300f), Fighter, Dt);
            Assert.False(o.Airbrake);
        }

        [Fact]
        public void AirbrakeStaysLatchedWhileTheCommandNeedsMoreThanIdleDrag()
        {
            // Idle gives −60 m/s of energy rate and the open brake another −100. A −120 command needs the brake
            // throughout, even though the braked measurement (−160) overshoots the command.
            var c = new FixedWingController();
            bool open = false, engaged = false;
            int releases = 0;
            for (int i = 0; i < 300; i++)
            {
                AircraftState s = Level(200f);
                s.Acc = Vec3.Forward * ((open ? -160f : -60f) * Scalar.G / 200f);
                ControlOutput o = c.Step(Cmd(energy: -120f), s, Fighter, Dt);
                if (engaged && !o.Airbrake) releases++;
                engaged |= o.Airbrake;
                open = o.Airbrake;
            }
            Assert.True(engaged);
            Assert.Equal(0, releases);
        }

        [Fact]
        public void AirbrakeNeverLatchesWhenNotAllowed()
        {
            var c = new FixedWingController();
            ControlOutput o = default;
            for (int i = 0; i < 120; i++) o = c.Step(Cmd(energy: -120f, airbrake: false), Level(300f), Fighter, Dt);
            Assert.False(o.Airbrake);
            Assert.True(o.Throttle >= FixedWingController.ThrottleFloor);
        }

        [Fact]
        public void TrackMakesTheNextStepContinueFromTheAppliedRoll()
        {
            var c = new FixedWingController();
            AircraftState s = Level(bank: 20f);
            c.Track(s, new ControlOutput { Roll = 0.3f, Throttle = 0.6f }, Fighter);
            ControlOutput o = c.Step(Cmd(bank: 20f), s, Fighter, Dt);
            Assert.Equal(0.3f, o.Roll, 2);
        }

        [Fact]
        public void ZeroDynamicPressureKeepsGainsFinite()
        {
            AircraftState s = Level();
            s.Qbar = 0f;
            s.Tas = 0f;
            float schedule = FixedWingController.GainSchedule(s, Fighter);
            Assert.InRange(schedule, 0.3f, 3f);
            Assert.True(Scalar.IsFinite(FixedWingController.TrimThrottle(s, Fighter, 1f)));
        }

        [Fact]
        public void LargeDtStaysFiniteAndBounded()
        {
            var c = new FixedWingController();
            ControlOutput o = c.Step(Cmd(bank: 170f, nz: 8f, energy: 300f), Level(), Fighter, 0.1f);
            Assert.InRange(o.Roll, -1f, 1f);
            Assert.InRange(o.Pitch, -1f, 1f);
            Assert.InRange(o.Throttle, 0f, 1f);
        }
    }
}
