using Xunit;

namespace WingCommand.Tests
{
    public class ThrustModelTests
    {
        private const float MaxSpeed = 300f;

        // ------------------------------------------------------------------ the curve

        [Fact]
        public void FullThrottleSustainsMaxSpeedAndMaxSpeedNeedsFullThrottle()
        {
            Assert.Equal(MaxSpeed, ThrustModel.SpeedAtThrottle(1f, MaxSpeed), 3);
            Assert.Equal(1f, ThrustModel.ThrottleToHold(MaxSpeed, MaxSpeed), 3);
        }

        [Fact]
        public void ThrottleAndSpeedAreInverses()
        {
            for (float throttle = 0f; throttle <= 1f; throttle += 0.05f)
            {
                float speed = ThrustModel.SpeedAtThrottle(throttle, MaxSpeed);
                Assert.Equal(throttle, ThrustModel.ThrottleToHold(speed, MaxSpeed), 3);
            }
        }

        // The whole reason the model is not the identity: a lever and a speed fraction are
        // not the same number. Half throttle does not hold half speed, it holds about 71%.
        [Fact]
        public void HalfThrottleHoldsMoreThanHalfSpeed()
        {
            float speed = ThrustModel.SpeedAtThrottle(0.5f, MaxSpeed);
            Assert.True(speed > MaxSpeed * 0.5f);
            Assert.Equal(MaxSpeed * 0.7071f, speed, 1);
        }

        [Theory]
        [InlineData(-5f)]
        [InlineData(1.5f)]
        [InlineData(float.NaN)]
        public void ThrottleOutsideTheLeverIsClampedIntoIt(float throttle)
        {
            float speed = ThrustModel.SpeedAtThrottle(throttle, MaxSpeed);
            Assert.InRange(speed, 0f, MaxSpeed);
        }

        [Fact]
        public void ADegenerateAirframeNeverProducesANonFiniteAnswer()
        {
            Assert.Equal(0f, ThrustModel.SpeedAtThrottle(1f, 0f));
            Assert.Equal(0f, ThrustModel.ThrottleToHold(200f, 0f));
            Assert.Equal(0f, ThrustModel.ThrottleToHold(-50f, MaxSpeed));
        }

        // ----------------------------------------------------------- anticipation

        // The property the whole design rests on: a leader that is not changing anything
        // contributes nothing, so the term can be added to a wingman's throttle outright
        // without biasing steady formation flight.
        [Fact]
        public void ASettledLeaderAnticipatesNothing()
        {
            for (float throttle = 0.1f; throttle <= 1f; throttle += 0.1f)
            {
                float settled = ThrustModel.SpeedAtThrottle(throttle, MaxSpeed);
                Assert.Equal(
                    0f, ThrustModel.ThrottleAnticipation(throttle, settled, MaxSpeed), 3);
            }
        }

        [Fact]
        public void PushingTheLeverUpAnticipatesMorePower()
        {
            // Cruising at half throttle, player firewalls.
            float cruising = ThrustModel.SpeedAtThrottle(0.5f, MaxSpeed);
            float term = ThrustModel.ThrottleAnticipation(1f, cruising, MaxSpeed);

            Assert.True(term > 0f);
            Assert.Equal(0.5f, term, 2);
        }

        // The half the previous Mathf.Max anticipation could not express, and the direct
        // cause of a wingman sliding out in front of a decelerating player.
        [Fact]
        public void PullingTheLeverBackAnticipatesLessPower()
        {
            float term = ThrustModel.ThrottleAnticipation(0.3f, MaxSpeed, MaxSpeed);

            Assert.True(term < 0f);
            Assert.Equal(-0.7f, term, 2);
        }

        [Fact]
        public void AnticipationIsBoundedByLeverTravel()
        {
            Assert.InRange(ThrustModel.ThrottleAnticipation(1f, 0f, MaxSpeed), -1f, 1f);
            Assert.InRange(ThrustModel.ThrottleAnticipation(0f, MaxSpeed, MaxSpeed), -1f, 1f);
            Assert.InRange(ThrustModel.ThrottleAnticipation(0.5f, 4000f, MaxSpeed), -1f, 1f);
        }

        // A leader the wingman cannot keep up with must not read as a power cut: the term
        // is measured against the leader's own maximum precisely so that a faster airframe
        // holding a steady lever asks for no change at all.
        [Fact]
        public void AFasterLeaderHoldingSteadyDoesNotAskForLessPower()
        {
            const float fastLeaderMax = 600f;
            float settled = ThrustModel.SpeedAtThrottle(0.8f, fastLeaderMax);

            Assert.Equal(
                0f, ThrustModel.ThrottleAnticipation(0.8f, settled, fastLeaderMax), 3);
        }

        // ------------------------------------------------------------- prediction

        [Fact]
        public void AcceleratingLeaderIsPredictedFaster()
        {
            Assert.Equal(215f, ThrustModel.PredictSpeed(200f, 10f, 1.5f, 25f), 3);
        }

        [Fact]
        public void DeceleratingLeaderIsPredictedSlower()
        {
            Assert.Equal(185f, ThrustModel.PredictSpeed(200f, -10f, 1.5f, 25f), 3);
        }

        [Fact]
        public void SteadyLeaderIsPredictedAtItsCurrentSpeed()
        {
            Assert.Equal(200f, ThrustModel.PredictSpeed(200f, 0f, 1.5f, 25f), 3);
        }

        // The clamp is a credibility limit, not a tuning knob: the rate is differentiated
        // from a velocity, so a respawn or a dropped frame can present an arbitrary step.
        [Fact]
        public void AnIncredibleRateIsClampedNotTrusted()
        {
            Assert.Equal(200f + 25f * 1.5f,
                         ThrustModel.PredictSpeed(200f, 100000f, 1.5f, 25f), 3);
            Assert.Equal(200f - 25f * 1.5f,
                         ThrustModel.PredictSpeed(200f, -100000f, 1.5f, 25f), 3);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void ANonFiniteRateLeavesTheSpeedAlone(float rate)
        {
            Assert.Equal(200f, ThrustModel.PredictSpeed(200f, rate, 1.5f, 25f), 3);
        }

        [Fact]
        public void PredictionNeverGoesNegative()
        {
            Assert.Equal(0f, ThrustModel.PredictSpeed(5f, -25f, 1.5f, 25f), 3);
        }
    }
}
