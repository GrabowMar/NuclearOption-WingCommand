using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class SettlePilotTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
        private static readonly Vec3 Point = new Vec3(100f, 0f, 200f);
        private static readonly LimitContext Far = new LimitContext { FloorY = 60f, Aggression = 0.3f };

        private static AircraftState At(float height, float vy = 0f, float dx = 0f, float slide = 0.01f, float tiltDeg = 0f)
        {
            AircraftState s = TestStates.Flying(new Vec3(Point.X + dx, height, Point.Z), new Vec3(0f, vy, slide));
            float t = tiltDeg * (float)Math.PI / 180f;
            s.Up = new Vec3((float)Math.Sin(t), (float)Math.Cos(t), 0f);
            return s;
        }

        private static (SettlePilot, IFlightPipeline, WingEventRing) New() =>
            (new SettlePilot(Point, 0f, 0f), FlightStack.NewPipeline(AirframeClass.Rotary), new WingEventRing());

        private static void Run(SettlePilot s, IFlightPipeline pipe, WingEventRing e, AircraftState state, float from, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) s.Step(state, Helo, pipe, from + t, Dt, e, 0, Far);
        }

        [Fact]
        public void OverThePointAtApproachHeightItDescends()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(40f, dx: 30f), Helo, pipe, 0f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Approach, s.Phase);
            s.Step(At(SettlePilot.ApproachHeight, dx: 1f), Helo, pipe, 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Descend, s.Phase);
        }

        [Fact]
        public void FarFromThePointTheApproachHoldsItsHeight()
        {
            // Review M4c I4: from far away it flew level at 15 m over whatever lay between.
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(200f, dx: 2000f), Helo, pipe, 0f, Dt, e, 0, Far);
            Assert.Equal(200f, s.Target.Y, 1);
            s.Step(At(200f, dx: 20f), Helo, pipe, 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePilot.ApproachHeight, s.Target.Y, 1);
        }

        [Fact]
        public void OnlyARealStillLevelContactIsATouchdown()
        {
            // Review M4c I1: "down" was declared 0.4 m up; the helicopter then fell the rest.
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0, Far);
            Run(s, pipe, e, At(0.3f, vy: -0.4f), 1f, 1f);
            Assert.Equal(SettlePhase.Descend, s.Phase);              // not yet on the ground
            Run(s, pipe, e, At(0.05f, vy: -0.2f, slide: 3f), 2f, 1f);
            Assert.Equal(SettlePhase.Descend, s.Phase);              // sliding
            Run(s, pipe, e, At(0.05f, vy: -0.2f, tiltDeg: 25f), 3f, 1f);
            Assert.Equal(SettlePhase.Descend, s.Phase);              // on a slope
            s.Step(At(0.05f, vy: -0.2f), Helo, pipe, 4f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Descend, s.Phase);              // the dwell
            Run(s, pipe, e, At(0.05f, vy: -0.2f), 4f, SettlePilot.ContactDwell + 0.1f);
            Assert.Equal(SettlePhase.Down, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.Landed));
        }

        [Fact]
        public void DownRampsTheCollectiveToNothingAndBrakes()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0, Far);
            Run(s, pipe, e, At(0.05f, vy: -0.2f), 1f, SettlePilot.ContactDwell + 0.1f);
            ControlOutput first = s.Step(At(0.05f), Helo, pipe, 2f, Dt, e, 0, Far);
            Assert.True(first.Throttle > 0f, "no cut: the collective ramps down");
            ControlOutput o = default;
            for (float t = 0f; t < SettlePilot.CollectiveRampSeconds + 0.1f; t += Dt) o = s.Step(At(0.05f), Helo, pipe, 2f + t, Dt, e, 0, Far);
            Assert.Equal(0f, o.Throttle);
            Assert.Equal(1f, o.Brake);
        }

        [Fact]
        public void ABounceWhileDownDescendsAgainEvenAfterAMinute()
        {
            // Review M4c I2: the descent timeout counted from the first descent; a bounce after 60 s ended the landing.
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0, Far);
            Run(s, pipe, e, At(0.05f, vy: -0.2f), 1f, SettlePilot.ContactDwell + 0.1f);
            Assert.Equal(SettlePhase.Down, s.Phase);
            s.Step(At(3f), Helo, pipe, 100f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Descend, s.Phase);
            s.Step(At(2.5f), Helo, pipe, 101f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Descend, s.Phase);
            Assert.Equal(0, e.CountOf(WingEventKind.LandingFailed));
        }

        [Fact]
        public void TakeOffFromDownOrDescendLiftsOffToDone()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 0f, Dt, e, 0, Far);
            s.TakeOff();
            Assert.Equal(SettlePhase.LiftOff, s.Phase);
            s.Step(At(10f), Helo, pipe, 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.LiftOff, s.Phase);
            s.Step(At(SettlePilot.LiftOffHeight - 2f), Helo, pipe, 2f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Done, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.Airborne));
        }

        [Fact]
        public void ALongApproachFromHighIsAllowedButADescentThatNeverTouchesDownGivesUp()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(600f, dx: 30f), Helo, pipe, 0f, Dt, e, 0, Far);
            s.Step(At(300f, dx: 30f), Helo, pipe, SettlePilot.SettleSeconds + 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Approach, s.Phase);
            s.Step(At(SettlePilot.ApproachHeight), Helo, pipe, 100f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Descend, s.Phase);
            s.Step(At(8f), Helo, pipe, 100f + SettlePilot.SettleSeconds + 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Done, s.Phase);
            Assert.Equal(1, e.CountOf(WingEventKind.LandingFailed));
        }

        [Fact]
        public void AnApproachThatNeverArrivesGivesUp()
        {
            (SettlePilot s, IFlightPipeline pipe, WingEventRing e) = New();
            s.Step(At(40f, dx: 30f), Helo, pipe, 0f, Dt, e, 0, Far);
            s.Step(At(40f, dx: 30f), Helo, pipe, SettlePilot.ApproachSeconds + 1f, Dt, e, 0, Far);
            Assert.Equal(SettlePhase.Done, s.Phase);
        }

        [Theory]
        [InlineData(true, 1f, true)]
        [InlineData(true, 0.9f, false)]    // about 26°: too steep
        [InlineData(false, 1f, false)]     // water (or nothing hit)
        public void OnlyDryLevelGroundIsLandable(bool land, float normalY, bool ok) =>
            Assert.Equal(ok, SettlePilot.Landable(land, normalY));
    }
}
