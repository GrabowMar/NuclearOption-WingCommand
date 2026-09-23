using Xunit;

namespace WingCommand.PureTests
{
    public class AutopilotSessionTests
    {
        private const float Dt = 1f / 60f;
        private const float LoadedMinimum = 70f;

        private static AircraftState At(float heading = 90f, float altitude = 3000f, float speed = 200f, float vy = 0f)
        {
            Vec3 h = Vec3.FromHeading(heading, speed);
            return TestStates.Flying(new Vec3(0f, altitude, 0f), new Vec3(h.X, vy, h.Z));
        }

        private static PilotInputs Hands(float pitch = 0f, float roll = 0f, float yaw = 0f, float throttle = 0.6f) =>
            new PilotInputs { Pitch = pitch, Roll = roll, Yaw = yaw, Throttle = throttle };

        [Fact]
        public void EngagingCapturesTheCurrentTrackAltitudeAndSpeed()
        {
            var ap = new AutopilotSession();
            AircraftState s = At(heading: 123f, altitude: 3210f, speed: 220f);
            ap.SetLateral(LateralHold.Heading, s);
            ap.SetVertical(VerticalHold.Altitude, s);
            ap.SetSpeed(true, s, 0.6f);
            Assert.True(ap.Engaged);
            Assert.Equal(123f, ap.Spec.HeadingDeg, 1);
            Assert.Equal(3210f, ap.Spec.AltitudeM);
            Assert.Equal(220f, ap.Spec.SpeedMps, 1);
        }

        [Fact]
        public void AutopilotWritesOnlyTheAxesOfItsModes()
        {
            var ap = new AutopilotSession();
            AircraftState s = At();
            ap.SetLateral(LateralHold.Heading, s);
            AutopilotTick t = ap.Step(s, Hands(), LoadedMinimum, Dt);
            Assert.True(t.WriteRoll && t.WriteYaw);
            Assert.False(t.WritePitch || t.WriteThrottle);

            ap.SetVertical(VerticalHold.Altitude, s);
            ap.SetSpeed(true, s, 0.6f);
            t = ap.Step(s, Hands(), LoadedMinimum, Dt);
            Assert.True(t.WritePitch && t.WriteThrottle && t.WriteRoll);
        }

        [Fact]
        public void StickDeflectionHandsThatAxisToThePilot()
        {
            var ap = new AutopilotSession();
            AircraftState s = At();
            ap.SetLateral(LateralHold.Heading, s);
            ap.SetVertical(VerticalHold.Altitude, s);
            AutopilotTick t = ap.Step(s, Hands(roll: 0.3f), LoadedMinimum, Dt);
            Assert.True(ap.LateralOverride);
            Assert.False(t.WriteRoll || t.WriteYaw);
            Assert.True(t.WritePitch);
        }

        [Fact]
        public void ReleasedAxisIsRecapturedAfterFourTenthsOfASecond()
        {
            var ap = new AutopilotSession();
            ap.SetLateral(LateralHold.Heading, At(heading: 90f));
            ap.Step(At(heading: 90f), Hands(roll: 0.5f), LoadedMinimum, Dt);
            AutopilotTick t = default;
            int ticks = 0;
            while (!t.Recaptured && ticks < 120)
            {
                t = ap.Step(At(heading: 135f), Hands(), LoadedMinimum, Dt);
                ticks++;
            }
            Assert.True(t.Recaptured);
            Assert.InRange(ticks * Dt, 0.39f, 0.45f);
            Assert.Equal(135f, ap.Spec.HeadingDeg, 1);
            Assert.True(t.WriteRoll);
            Assert.False(ap.LateralOverride);
        }

        [Fact]
        public void AltitudeRecaptureWaitsUntilTheClimbStops()
        {
            var ap = new AutopilotSession();
            ap.SetVertical(VerticalHold.Altitude, At(altitude: 3000f));
            ap.Step(At(), Hands(pitch: 0.4f), LoadedMinimum, Dt);
            AutopilotTick t = default;
            for (int i = 0; i < 60; i++) t = ap.Step(At(altitude: 3300f, vy: 10f), Hands(), LoadedMinimum, Dt);
            Assert.True(ap.VerticalOverride);
            Assert.False(t.WritePitch);
            t = ap.Step(At(altitude: 3400f, vy: 1f), Hands(), LoadedMinimum, Dt);
            Assert.True(t.Recaptured);
            Assert.Equal(3400f, ap.Spec.AltitudeM);
        }

        [Fact]
        public void MovingTheThrottleDisengagesSpeedHoldOnly()
        {
            var ap = new AutopilotSession();
            AircraftState s = At();
            ap.SetLateral(LateralHold.Heading, s);
            ap.SetSpeed(true, s, 0.6f);
            ap.Step(s, Hands(throttle: 0.62f), LoadedMinimum, Dt);
            Assert.True(ap.Spec.Speed);
            AutopilotTick t = ap.Step(s, Hands(throttle: 0.7f), LoadedMinimum, Dt);
            Assert.False(ap.Spec.Speed);
            Assert.Equal(ApDisengage.Throttle, t.Disengaged);
            Assert.False(t.WriteThrottle);
            Assert.Equal(LateralHold.Heading, ap.Spec.Lateral);
        }

        [Theory]
        [InlineData((int)ApDisengage.Gloc)]
        [InlineData((int)ApDisengage.GearLow)]
        [InlineData((int)ApDisengage.Slow)]
        public void SafetyConditionsDisengageEverything(int causeValue)
        {
            var cause = (ApDisengage)causeValue;
            var ap = new AutopilotSession();
            AircraftState s = At();
            ap.SetLateral(LateralHold.Level, s);
            ap.SetVertical(VerticalHold.VerticalSpeed, s);
            ap.SetSpeed(true, s, 0.6f);
            PilotInputs hands = Hands();
            if (cause == ApDisengage.Gloc) hands.Gloc = true;
            if (cause == ApDisengage.GearLow)
            {
                hands.GearDown = true;
                s.RadarAlt = 15f;
            }
            if (cause == ApDisengage.Slow) s = At(speed: 70f);
            AutopilotTick t = ap.Step(s, hands, LoadedMinimum, Dt);
            Assert.Equal(cause, t.Disengaged);
            Assert.False(ap.Engaged);
            Assert.False(t.WritePitch || t.WriteRoll || t.WriteThrottle);
        }

        [Fact]
        public void AddingAModeWhileEngagedAsksForAReseed()
        {
            // Unwritten axes keep stepping against a plane the pilot flies; the axis a new mode takes over must be
            // seeded from the applied inputs first, or it starts from a wound-up loop.
            var ap = new AutopilotSession();
            ap.SetLateral(LateralHold.Heading, At());
            ap.Step(At(), Hands(), LoadedMinimum, Dt);
            Assert.False(ap.Step(At(), Hands(), LoadedMinimum, Dt).Recaptured);
            ap.SetVertical(VerticalHold.Altitude, At());
            Assert.True(ap.Step(At(), Hands(), LoadedMinimum, Dt).Recaptured);
            Assert.False(ap.Step(At(), Hands(), LoadedMinimum, Dt).Recaptured);
            ap.SetSpeed(true, At(), 0.6f);
            Assert.True(ap.Step(At(), Hands(), LoadedMinimum, Dt).Recaptured);
        }

        [Fact]
        public void TooSlowIsJudgedOnEquivalentAirspeedAtAltitude()
        {
            // 100 m/s true at 8 km is about 65 m/s equivalent: below 1.1 × the 70 m/s loaded minimum.
            var ap = new AutopilotSession();
            AircraftState s = At(altitude: 8000f, speed: 100f);
            ap.SetLateral(LateralHold.Level, s);
            Assert.Equal(ApDisengage.Slow, ap.Step(s, Hands(), LoadedMinimum, Dt).Disengaged);
        }

        [Fact]
        public void GearDownHighUpDoesNotDisengage()
        {
            var ap = new AutopilotSession();
            ap.SetLateral(LateralHold.Level, At());
            AutopilotTick t = ap.Step(At(), new PilotInputs { GearDown = true, Throttle = 0.6f }, LoadedMinimum, Dt);
            Assert.Equal(ApDisengage.None, t.Disengaged);
            Assert.True(ap.Engaged);
        }
    }
}
