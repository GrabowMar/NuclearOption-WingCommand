using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RecoveryPilotTests
    {
        private const float Dt = 1f / 30f;

        private static AirframeProfile Jet() => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 216f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = 11f,
            TakeoffSpeed = 70f,
        });

        private static AirframeProfile Helo() => AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 134f });

        private static AircraftState Flying(Vec3 pos, Vec3 fwd) => new AircraftState
        {
            Pos = pos, Vel = fwd * 150f, Fwd = fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, fwd), Tas = 150f, RadarAlt = pos.Y,
        };

        [Fact]
        public void AJetApproachesFiveKilometresOutOnTheApproachSideAndHandsOverThere()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.FixedWing, RecoveryIntent.Rtb);
            // Runway 0 runs north from z = 0: landing northwards, the approach point is 5 km south of the threshold.
            Vec3 point = recovery.ApproachPoint(Flying(new Vec3(0f, 800f, 9000f), Vec3.Forward));
            Assert.True((point - new Vec3(0f, RecoveryPilot.ApproachHeight, -RecoveryPilot.ApproachDistance)).Length < 1f, $"{point}");
            recovery.ApproachIntent(Flying(new Vec3(3000f, 800f, 3000f), Vec3.Forward), Jet(), out bool far);
            Assert.False(far);
            FlightIntent intent = recovery.ApproachIntent(Flying(new Vec3(300f, 500f, -5200f), Vec3.Forward), Jet(), out bool near);
            Assert.True(near);
            Assert.True((intent.Ref.Pos - point).Length < 1f);
        }

        [Fact]
        public void AHelicopterApproachesTheFieldLowAndClose()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.Rotary, RecoveryIntent.Rtb);
            Vec3 point = recovery.ApproachPoint(Flying(new Vec3(-150f, 300f, 8000f), Vec3.Forward));
            Assert.Equal(field.Field.Center.Y + RecoveryPilot.HeloApproachHeight, point.Y, 1);
            Assert.Equal(RecoveryPilot.HeloApproachDistance, (point - field.Field.Center).Horizontal.Length, 1);
            Assert.True(point.Z > field.Field.Center.Z, "on the side it comes from");
        }

        [Fact]
        public void EachFailedLandingGoesBackToTheApproachUntilTheThirdReleasesIt()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.FixedWing, RecoveryIntent.Rtb);
            var events = new WingEventRing();
            for (int i = 1; i <= RecoveryPilot.MaxLandingTries; i++)
            {
                recovery.LandingBegun(i * 100f);
                Assert.Equal(RecoveryPhase.Landing, recovery.Phase);
                recovery.LandingFailed(i * 100f + 50f, events, 0);
            }
            Assert.Equal(RecoveryPhase.Released, recovery.Phase);
            Assert.Equal(RecoveryPilot.MaxLandingTries, events.CountOf(WingEventKind.LandingFailed));
        }

        [Fact]
        public void ALandingThatNeverEndsIsOverdue()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.FixedWing, RecoveryIntent.Rtb);
            recovery.LandingBegun(10f);
            Assert.False(recovery.LandingOverdue(10f + RecoveryPilot.LandingSeconds - 1f));
            Assert.True(recovery.LandingOverdue(10f + RecoveryPilot.LandingSeconds + 1f));
        }

        /// <summary>Lands the jet at the runway exit and runs it on the ground until <paramref name="until"/> holds.</summary>
        private static float RunGround(RecoveryPilot recovery, FieldTraffic field, TestGroundPlant plant, IFlightPipeline pipeline,
            WingEventRing events, float t, float fuel, float ammo, Func<RecoveryAction, bool> until, out RecoveryAction last)
        {
            last = RecoveryAction.None;
            for (int i = 0; i < 400 * 30; i++, t += Dt)
            {
                field.Step(Dt);
                plant.Step(recovery.Ground.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0), Dt);
                last = recovery.Update(t, fuel, ammo, events, 0);
                if (until(last)) return t;
            }
            return t;
        }

        [Fact]
        public void AfterTouchdownAnRtbMemberTaxisToAStandAndGoesBackToTheReserve()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.FixedWing, RecoveryIntent.Rtb);
            var events = new WingEventRing();
            recovery.LandingBegun(0f);
            var landed = new Pose(new Vec3(0f, 0f, 930f), Vec3.Forward);
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            recovery.Landed(field, plant.Read(Dt), 5f, events, 0);
            Assert.Equal(RecoveryPhase.Ground, recovery.Phase);
            Assert.Equal(GroundPhase.TaxiIn, recovery.Ground.Phase);
            RunGround(recovery, field, plant, FlightStack.NewPipeline(AirframeClass.FixedWing), events, 5f, 0.3f, 0.5f,
                a => a != RecoveryAction.None, out RecoveryAction action);
            Assert.Equal(RecoveryAction.Reserve, action);
            Assert.Equal(RecoveryPhase.Reserve, recovery.Phase);
            Assert.True((plant.Pos - new Vec3(-200f, 0f, 300f)).Horizontal.Length < 5f);
            Assert.Equal(RecoveryAction.None, recovery.Update(1000f, 0.3f, 0.5f, events, 0));
        }

        [Fact]
        public void ARefitMemberIsServicedForTheRefitTimeThenDepartsAgain()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.FixedWing, RecoveryIntent.Refit);
            var events = new WingEventRing();
            recovery.LandingBegun(0f);
            var landed = new Pose(new Vec3(0f, 0f, 930f), Vec3.Forward);
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            recovery.Landed(field, plant.Read(Dt), 5f, events, 0);
            float standing = RunGround(recovery, field, plant, pipeline, events, 5f, 0.5f, 1f,
                _ => recovery.Phase == RecoveryPhase.Servicing, out _);
            float serviced = RunGround(recovery, field, plant, pipeline, events, standing, 0.5f, 1f,
                a => a == RecoveryAction.Service, out _);
            Assert.Equal(RefitTimer.Seconds(0.5f, 1f), serviced - standing, 0);
            recovery.Serviced(serviced, 1, events, 0);
            Assert.Equal(RecoveryPhase.Departing, recovery.Phase);
            Assert.Equal(GroundPhase.Parked, recovery.Ground.Phase);
            RunGround(recovery, field, plant, pipeline, events, serviced, 1f, 1f,
                _ => recovery.Ground.Phase == GroundPhase.ClimbOut, out _);
            Assert.Equal(GroundPhase.ClimbOut, recovery.Ground.Phase);
            Assert.Equal(1, events.CountOf(WingEventKind.Serviced));
        }

        [Fact]
        public void ALandedHelicopterStandsWhereItLandedAndLiftsOffAfterItsRefit()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.Rotary, RecoveryIntent.Refit);
            var events = new WingEventRing();
            recovery.LandingBegun(0f);
            var pad = new AircraftState { Pos = new Vec3(-400f, 0f, 600f), Fwd = Vec3.Forward, Up = Vec3.Up, Right = Vec3.Right, RotorRpm = 1f };
            recovery.Landed(field, pad, 5f, events, 0);
            Assert.Equal(GroundPhase.Stand, recovery.Ground.Phase);
            Assert.Equal(RecoveryAction.None, recovery.Update(5f, 0.2f, 1f, events, 0));
            Assert.Equal(RecoveryPhase.Servicing, recovery.Phase);
            Assert.Equal(RecoveryAction.Service, recovery.Update(5f + RefitTimer.Seconds(0.2f, 1f) + 0.1f, 0.2f, 1f, events, 0));
            recovery.Serviced(200f, 1, events, 0);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.Rotary);
            for (int i = 0; i < 5 * 30 && recovery.Ground.Phase != GroundPhase.LiftOff; i++)
                recovery.Ground.Step(pad, Helo(), pipeline, 200f + i * Dt, Dt, events, 0);
            Assert.Equal(GroundPhase.LiftOff, recovery.Ground.Phase);
        }

        [Fact]
        public void AMemberRecalledWhileTaxiingOutIsRecoveredFromWhereItIs()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var plant = new TestGroundPlant(spawn);
            var ground = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            float t = 0f;
            for (int i = 0; i < 20 * 30; i++, t += Dt)
            {
                field.Step(Dt);
                plant.Step(ground.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0), Dt);
            }
            Assert.True(ground.TaxiIn(plant.Read(Dt), t, events, 0));
            RecoveryPilot recovery = RecoveryPilot.FromGround(1, ground, RecoveryIntent.Rtb);
            Assert.Equal(RecoveryPhase.Ground, recovery.Phase);
            RunGround(recovery, field, plant, pipeline, events, t, 0.9f, 1f, a => a != RecoveryAction.None, out RecoveryAction action);
            Assert.Equal(RecoveryAction.Reserve, action);
        }

        [Fact]
        public void ALandedHelicopterOnRtbGoesStraightBackToTheReserve()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var recovery = new RecoveryPilot(1, field, AirframeClass.Rotary, RecoveryIntent.Rtb);
            var events = new WingEventRing();
            recovery.LandingBegun(0f);
            var pad = new AircraftState { Pos = new Vec3(-400f, 0f, 600f), Fwd = Vec3.Forward, Up = Vec3.Up, Right = Vec3.Right, RotorRpm = 1f };
            recovery.Landed(field, pad, 5f, events, 0);
            Assert.Equal(RecoveryAction.Reserve, recovery.Update(5f, 0.5f, 1f, events, 0));
        }
    }
}
