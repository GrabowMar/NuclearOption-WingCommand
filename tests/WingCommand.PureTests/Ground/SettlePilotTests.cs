using Xunit;

namespace WingCommand.PureTests
{
    public class SettlePilotTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
        private static readonly Vec3 Point = new Vec3(100f, 0f, 200f);

        private static AircraftState At(float height, float vy = 0f, float dx = 0f) =>
            TestStates.Flying(new Vec3(Point.X + dx, height, Point.Z), new Vec3(0f, vy, 0.01f));

        private static (SettlePilot, IFlightPipeline, WingEventRing) New() =>
            (new SettlePilot(Point, 0f, 0f), FlightStack.NewPipeline(AirframeClass.Rotary), new WingEventRing());

        [Fact]
        public void OverThePointAtApproachHeightItDescends()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(40f, dx: 30f), Helo, pipe, 0f, Dt, e, 0);
            Assert.Equal(SettlePhase.Approach, s.Phase);
            s.Step(At(SettlePilot.ApproachHeight, dx: 1f), Helo, pipe, 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.Descend, s.Phase);
        }

        [Fact]
        public void OnlyALowSlowContactIsATouchdown()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0);
            s.Step(At(0.3f, vy: -3f), Helo, pipe, 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.Descend, s.Phase);
            s.Step(At(0.3f, vy: -0.5f), Helo, pipe, 2f, Dt, e, 0);
            Assert.Equal(SettlePhase.Down, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.Landed));
            ControlOutput o = s.Step(At(0.1f), Helo, pipe, 3f, Dt, e, 0);
            Assert.Equal(0f, o.Throttle);
            Assert.Equal(1f, o.Brake);
        }

        [Fact]
        public void ABounceWhileDownDescendsAgain()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0);
            s.Step(At(0.3f, vy: -0.5f), Helo, pipe, 1f, Dt, e, 0);
            s.Step(At(3f), Helo, pipe, 2f, Dt, e, 0);
            Assert.Equal(SettlePhase.Descend, s.Phase);
        }

        [Fact]
        public void TakeOffFromDownOrDescendLiftsOffToDone()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0);
            s.TakeOff();
            Assert.Equal(SettlePhase.LiftOff, s.Phase);
            s.Step(At(10f), Helo, pipe, 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.LiftOff, s.Phase);
            s.Step(At(SettlePilot.LiftOffHeight - 2f), Helo, pipe, 2f, Dt, e, 0);
            Assert.Equal(SettlePhase.Done, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.Airborne));
        }

        [Fact]
        public void ALongApproachFromHighIsAllowedButADescentThatNeverTouchesDownGivesUp()
        {
            // An order given at 600 m: the approach alone takes over a minute; the timeouts are per phase.
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(600f, dx: 30f), Helo, pipe, 0f, Dt, e, 0);
            s.Step(At(300f, dx: 30f), Helo, pipe, SettlePilot.SettleSeconds + 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.Approach, s.Phase);
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 100f, Dt, e, 0);
            Assert.Equal(SettlePhase.Descend, s.Phase);
            s.Step(At(8f), Helo, pipe, 100f + SettlePilot.SettleSeconds + 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.Done, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.LandingFailed));
        }

        [Fact]
        public void AnApproachThatNeverArrivesGivesUp()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(40f, dx: 30f), Helo, pipe, 0f, Dt, e, 0);
            s.Step(At(40f, dx: 30f), Helo, pipe, SettlePilot.ApproachSeconds + 1f, Dt, e, 0);
            Assert.Equal(SettlePhase.Done, s.Phase);
        }
    }
}
