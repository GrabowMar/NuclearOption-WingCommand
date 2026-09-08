using System;
using System.Numerics;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationInterceptTests
    {
        [Theory]
        [InlineData(1000f, 100f, 200f, 10f)]
        [InlineData(1200f, -100f, 200f, 4f)]
        [InlineData(1000f, -100f, 100f, 5f)]
        [InlineData(1000f, -200f, 100f, 3.333333f)]
        public void StraightInterceptSolvesActualMeetingTime(float gap, float targetSpeed,
            float ownSpeed, float expectedSeconds)
        {
            float seconds = FormationIntercept.InterceptSeconds(new Vector2(0f, gap),
                new Vector2(0f, targetSpeed), ownSpeed, 45f);
            Assert.InRange(Math.Abs(seconds - expectedSeconds), 0f, 0.00001f);
            Assert.InRange(Math.Abs(Math.Abs(gap + targetSpeed * seconds) - ownSpeed * seconds),
                0f, 0.001f);
        }

        [Fact]
        public void CrossingInterceptLeadsTheTargetByItsTravelUntilArrival()
        {
            var gap = new Vector2(1000f, 0f);
            var velocity = new Vector2(0f, 100f);
            float seconds = FormationIntercept.InterceptSeconds(gap, velocity, 200f, 45f);
            Assert.InRange(seconds, 5.7734f, 5.7736f);
            Vector2 future = gap + velocity * seconds;
            Assert.InRange(future.Y, 577.34f, 577.36f);
            Assert.InRange(Math.Abs(future.Length() - 200f * seconds), 0f, 0.001f);
        }

        [Theory]
        [InlineData(100f)]
        [InlineData(101f)]
        [InlineData(300f)]
        public void EqualOrFasterRecedingTargetHasFiniteBoundedPrediction(float targetSpeed)
        {
            float seconds = FormationIntercept.InterceptSeconds(new Vector2(0f, 5000f),
                new Vector2(0f, targetSpeed), 100f, 45f);
            Assert.Equal(45f, seconds);
        }

        [Fact]
        public void NearEqualSpeedsDoNotLoseTheSmallPositiveRoot()
        {
            float seconds = FormationIntercept.InterceptSeconds(new Vector2(0f, 1000f),
                new Vector2(0f, -100f), 100.00001f, 45f);
            Assert.InRange(seconds, 4.9999f, 5.0001f);
        }

        [Fact]
        public void LongRangePlanExtendsLeadAndKeepsAStationarySlotAtLeaderVelocity()
        {
            var velocity = new Vector2(0f, 200f);
            var plan = FormationIntercept.Solve(new Vector2(0f, 3000f), velocity,
                new Vector2(120f, -100f), velocity, 300f, 320f, 0f);
            Assert.InRange(plan.Seconds, 29.999f, 30.001f);
            Assert.InRange(plan.Gap.Y, 8999.9f, 9000.1f);
            Assert.Equal(velocity, plan.ArrivalVelocity);
        }

        [Theory]
        [InlineData(-0.2f)]
        [InlineData(0.2f)]
        public void SustainedTurnBoundsPredictionAndPreservesSlotVelocityMagnitude(float turnRate)
        {
            var velocity = new Vector2(0f, 200f);
            var plan = FormationIntercept.Solve(new Vector2(0f, 8000f), velocity,
                new Vector2(120f, -100f), velocity, 250f, 300f, turnRate);
            Assert.InRange(Math.Abs(turnRate * plan.Seconds), 0f, 0.785399f);
            Assert.InRange(plan.ArrivalVelocity.Length(), 199.999f, 200.001f);
            Assert.True(plan.Gap.X * turnRate > 0f);
            Assert.True(plan.Gap.Y > 8000f);
        }

        [Fact]
        public void CloseFormationKeepsItsShortPrediction()
        {
            var velocity = new Vector2(0f, 200f);
            var plan = FormationIntercept.Solve(new Vector2(0f, 400f), velocity,
                Vector2.Zero, velocity, 200f, 300f, 0f);
            Assert.Equal(2f, plan.Seconds);
            Assert.Equal(new Vector2(0f, 800f), plan.Gap);
        }

        [Theory]
        [InlineData(600f)]
        [InlineData(2400f)]
        public void HorizonBlendHasNoJumpAtCaptureBoundaries(float distance)
        {
            var velocity = new Vector2(100f, 100f);
            var before = FormationIntercept.Solve(new Vector2(0f, distance - 0.001f), velocity,
                Vector2.Zero, velocity, 200f, 300f, 0f);
            var after = FormationIntercept.Solve(new Vector2(0f, distance + 0.001f), velocity,
                Vector2.Zero, velocity, 200f, 300f, 0f);
            Assert.InRange(Vector2.Distance(before.Gap, after.Gap), 0f, 0.02f);
        }

        [Theory]
        [InlineData(50f, 0.5f, 2f)]
        [InlineData(500f, 2f, 1f)]
        [InlineData(5000f, 6f, 0.5f)]
        public void SafeClosureReservesBothEngineLagAndStoppingDistance(float distance, float braking, float lag)
        {
            float closure = FormationClosure.SafeClosure(distance, braking, lag);
            float required = closure * lag + closure * closure / (2f * braking);
            Assert.InRange(Math.Abs(required - distance), 0f, 0.002f);
            Assert.True(closure < Math.Sqrt(2f * braking * distance));
        }

        [Fact]
        public void NoRemainingDistanceAllowsNoClosingSpeed()
        {
            Assert.Equal(0f, FormationClosure.SafeClosure(0f, 2f, 1f));
            Assert.Equal(0f, FormationClosure.SafeClosure(-50f, 2f, 1f));
        }

        [Fact]
        public void LongerPredictedRangeDoesNotIncreaseArrivalEnergy()
        {
            var actual = new Vector2(0f, 2500f);
            var velocity = new Vector2(0f, 100f);
            var near = new FormationIntercept.Plan(actual, velocity, 10f);
            var far = new FormationIntercept.Plan(actual * 10f, velocity, 45f);
            float speed = FormationClosure.PursuitSpeed(actual, velocity, near, 150f, 400f, 2f, 1f, 100f);
            float farSpeed = FormationClosure.PursuitSpeed(actual, velocity, far, 150f, 400f, 2f, 1f, 100f);
            Assert.Equal(speed, farSpeed);
            Assert.InRange(speed, 196f, 198f);
        }

        [Fact]
        public void PursuitSpeedRespectsCapabilityAndLeavesCloseStationDemandAlone()
        {
            var plan = new FormationIntercept.Plan(new Vector2(0f, 10000f), new Vector2(0f, 250f), 40f);
            Assert.Equal(280f, FormationClosure.PursuitSpeed(new Vector2(0f, 5000f), new Vector2(0f, 200f), plan,
                200f, 280f, 2f, 1f, 100f));
            Assert.Equal(150f, FormationClosure.PursuitSpeed(new Vector2(0f, 500f), new Vector2(0f, 200f), plan,
                150f, 280f, 2f, 1f, 100f));
            Assert.Equal(150f, FormationClosure.PursuitSpeed(new Vector2(0f, 5000f), new Vector2(0f, -200f), plan,
                150f, 280f, 2f, 1f, 100f));
        }

        [Fact]
        public void LongAlignedJoinUsesFullPowerButNotAfterBrakingEnvelopeIsReached()
        {
            var accelerating = Resolve(distance: 5000f, closing: 50f, speedError: 20f, throttle: 0.4f);
            Assert.Equal(1f, accelerating.Throttle);
            Assert.False(accelerating.Airbrake);
            Assert.Equal(0.4f, Resolve(distance: 5000f, closing: 160f, speedError: 20f, throttle: 0.4f).Throttle);
            Assert.Equal(0.4f, Resolve(distance: 5000f, closing: 50f, speedError: 20f,
                throttle: 0.4f, allowPursuit: false).Throttle);
            Assert.Equal(0.4f, Resolve(distance: 5000f, closing: 50f, speedError: 20f,
                throttle: 0.4f, alignment: 0f).Throttle);
        }

        [Fact]
        public void ExcessiveClosureUsesIdleAndSpeedBrake()
        {
            var controls = Resolve();
            Assert.Equal(0f, controls.Throttle);
            Assert.True(controls.Airbrake);
        }

        [Theory]
        [InlineData(400f, 30f)]
        [InlineData(1000f, 50f)]
        [InlineData(5000f, 100f)]
        public void CaptureUsesFullPowerEvenAboveTheDampedSpeedTarget(float distance, float closing)
        {
            var controls = Resolve(distance: distance, closing: closing, speedError: -2f, throttle: 0.4f);
            Assert.Equal(1f, controls.Throttle);
            Assert.False(controls.Airbrake);
            Assert.Equal(0.4f, Resolve(distance: distance, closing: closing, speedError: -2f,
                throttle: 0.4f, terrainWarning: true).Throttle);
            Assert.Equal(0.4f, Resolve(distance: 200f, closing: 0f, speedError: -2f,
                throttle: 0.4f).Throttle);
        }

        [Fact]
        public void BrakingHysteresisAvoidsChatterAndReleasesBeforeCoSpeed()
        {
            Assert.False(Resolve(closing: 15f, wasBraking: false).Airbrake);
            Assert.True(Resolve(closing: 15f, wasBraking: true).Airbrake);
            var released = Resolve(closing: 6f, wasBraking: true);
            Assert.False(released.Airbrake);
            Assert.Equal(0.8f, released.Throttle);
        }

        [Fact]
        public void BrakeStallMarginAccountsForBankAndHasSeparateReleaseThreshold()
        {
            Assert.False(Resolve(airspeed: 125f, bank: 40f).Airbrake);
            Assert.True(Resolve(airspeed: 125f, bank: 40f, wasBraking: true).Airbrake);
            Assert.False(Resolve(airspeed: 115f, bank: 40f, wasBraking: true).Airbrake);
        }

        [Fact]
        public void StallRecoveryOverridesArrivalAndRetractsBrake()
        {
            var controls = Resolve(airspeed: 99f, wasBraking: true);
            Assert.Equal(1f, controls.Throttle);
            Assert.False(controls.Airbrake);
        }

        [Fact]
        public void ClimbOvershootCannotCancelBankLoadedStallRecovery()
        {
            var controls = Resolve(airspeed: 130f, bank: 60f, verticalSpeed: 10f);
            float finalThrottle = FormationControlRules.ClimbThrottleCap(controls.Throttle,
                10f, -100f, airspeed: 130f,
                minimumSpeed: FormationClosure.LoadedMinimum(100f, 60f));
            Assert.Equal(1f, finalThrottle);
            Assert.False(controls.Airbrake);
        }

        [Fact]
        public void ReleasedOrDeniedBrakeUsesPositiveNativeIdle()
        {
            var denied = Resolve(throttle: 0f, allowBraking: false);
            Assert.False(denied.Airbrake);
            Assert.Equal(0.01f, denied.Throttle);
            var released = Resolve(throttle: 0f, closing: 6f, wasBraking: true);
            Assert.False(released.Airbrake);
            Assert.Equal(0.01f, released.Throttle);
        }

        [Fact]
        public void DisabledBrakingCannotTakeTheThrottleOrBrake()
        {
            var controls = Resolve(allowBraking: false, wasBraking: true);
            Assert.Equal(0.8f, controls.Throttle);
            Assert.False(controls.Airbrake);
        }

        [Theory]
        [InlineData(true, 1000f, 0f, 0f)]
        [InlineData(false, 100f, 0f, 0f)]
        [InlineData(false, 1000f, 60f, 0f)]
        [InlineData(false, 1000f, 0f, 10f)]
        public void UnsafeEnergySheddingCannotCutRequestedRecoveryPower(bool terrainWarning,
            float altitude, float bank, float climb)
        {
            var controls = Resolve(terrainWarning: terrainWarning, radarAltitude: altitude,
                bank: bank, verticalSpeed: climb, wasBraking: true);
            Assert.False(controls.Airbrake);
            Assert.True(controls.Throttle >= 0.8f);
        }

        private static FormationClosure.Controls Resolve(float throttle = 0.8f, float speedError = -20f,
            float airspeed = 200f, float distance = 100f, float closing = 30f, float alignment = 1f,
            float bank = 0f, float radarAltitude = 1000f, float verticalSpeed = 0f,
            bool terrainWarning = false, bool allowPursuit = true, bool allowBraking = true, bool wasBraking = false) =>
            FormationClosure.Resolve(throttle, speedError, airspeed, 100f, distance, closing,
                100f, 2f, 1f, alignment, bank, radarAltitude, verticalSpeed,
                terrainWarning, allowPursuit, allowBraking, wasBraking);
    }
}
