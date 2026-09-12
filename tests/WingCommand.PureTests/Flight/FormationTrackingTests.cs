using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationTrackingTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(200f, 0f)]
        [InlineData(1000f, 60f)]
        [InlineData(1000f, -60f)]
        public void RejoinRetainsAccelerationLeadAndVerticalSpeed(float gap, float climb)
        {
            float baseline = FormationTracking.ApproachSpeed(0f, gap, 0f, 120f,
                0f, 120f, 2f, 1f, 1f, 0.75f);
            float expected = (float)Math.Sqrt(baseline * baseline + climb * climb);
            foreach (float acceleration in new[] { -8f, 0f, 8f })
            {
                float lead = ThrustModel.PredictSpeed(120f, acceleration, 0.75f, 25f) - 120f;
                float speed = FormationTracking.ApproachSpeed(0f, gap, 0f, 120f,
                    0f, 120f, 2f, 1f, 1f, 0.75f, climb, lead);
                Assert.InRange(Math.Abs(speed - expected - lead), 0f, 0.0001f);
            }
        }

        [Theory]
        [InlineData(-0.2f)]
        [InlineData(0.2f)]
        public void AcceleratingTurnKeepsSpeedLeadThroughPursuit(float turnRate)
        {
            var gap = new System.Numerics.Vector2(0f, 3000f);
            var velocity = new System.Numerics.Vector2(0f, 120f);
            float Pursuit(float acceleration)
            {
                float predicted = ThrustModel.PredictSpeed(120f, acceleration, 0.75f, 25f);
                var future = new System.Numerics.Vector2(0f, predicted);
                var plan = FormationIntercept.Solve(gap, future, System.Numerics.Vector2.Zero,
                    future, 120f, 320f, turnRate);
                Assert.InRange(plan.ArrivalVelocity.Length(), predicted - 0.001f, predicted + 0.001f);
                return FormationClosure.PursuitSpeed(gap, velocity, plan, predicted,
                    320f, 2f, 0.75f, 120f);
            }
            Assert.True(Pursuit(8f) > Pursuit(0f));
            Assert.True(Pursuit(-8f) < Pursuit(0f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.0000001f)]
        [InlineData(-0.0000001f)]
        public void ArcHasContinuousStraightFlightLimit(float turnRate)
        {
            var point = FormationTracking.Arc(40f, -3f, 100f, turnRate, 5f);
            Assert.InRange(point.x, 199.999f, 200.001f);
            Assert.Equal(-15f, point.y);
            Assert.InRange(point.z, 499.999f, 500.001f);
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(-1f)]
        public void TurnPredictionIntegratesArcInsteadOfRotatingTheWholeLead(float side)
        {
            var point = FormationTracking.Arc(0f, 2f, 100f, side * 0.1f, 5f);
            Assert.InRange(point.x * side, 122.41f, 122.43f);
            Assert.InRange(point.z, 479.42f, 479.44f);
            Assert.Equal(10f, point.y);
        }

        [Theory]
        [InlineData(-120f)]
        [InlineData(120f)]
        public void InsideAndOutsideSlotsStayOnTheirOwnTurnRadius(float lateral)
        {
            // Rotate the aft-offset slot around the leader's right-turn centre at x=1000 without
            // double-counting offset velocity.
            var point = FormationTracking.FutureSlotOffset(0f, 0f, 100f,
                lateral, 20f, -100f, 0.1f, 5f);
            double expectedRadius = Math.Sqrt(Math.Pow(1000f - lateral, 2) + 10000d);
            double radius = Math.Sqrt(Math.Pow(1000f - point.x, 2) + point.z * point.z);
            Assert.InRange(Math.Abs(radius - expectedRadius), 0d, 0.001d);
            Assert.Equal(20f, point.y);
        }

        [Fact]
        public void PredictionDoesNotJumpAtTheOldTurnActivationThreshold()
        {
            var below = FormationTracking.Arc(0f, 0f, 200f, 0.019999f, 6f);
            var above = FormationTracking.Arc(0f, 0f, 200f, 0.020001f, 6f);
            Assert.InRange(Math.Abs(above.x - below.x), 0f, 0.01f);
            Assert.InRange(Math.Abs(above.z - below.z), 0f, 0.01f);
        }

        [Fact]
        public void LongPredictionCannotWrapBehindTheLeader()
        {
            var point = FormationTracking.Arc(0f, 0f, 100f, 1.5f, 10f);
            Assert.True(point.x > 0f && point.z > 0f);
            Assert.InRange(FormationTracking.Sweep(1.5f, 10f), 0f, (float)Math.PI / 2f);
        }

        [Fact]
        public void CaptureCurveMeetsBothEndpointsAlongTheirVelocityTangents()
        {
            const float epsilon = 0.001f;
            var start = FormationTracking.Capture(1000, 1000, 0, 100, 100, 0, 10, 0);
            var departing = FormationTracking.Capture(1000, 1000, 0, 100, 100, 0, 10, epsilon);
            var arriving = FormationTracking.Capture(1000, 1000, 0, 100, 100, 0, 10, 10 - epsilon);
            var end = FormationTracking.Capture(1000, 1000, 0, 100, 100, 0, 10, 10);
            Assert.Equal((0f, 0f), start);
            Assert.Equal((1000f, 1000f), end);
            Assert.InRange(departing.x / epsilon, 0f, 0.1f);
            Assert.InRange(departing.z / epsilon, 99.9f, 100.1f);
            Assert.InRange((end.x - arriving.x) / epsilon, 99.8f, 100.2f);
            Assert.InRange((end.z - arriving.z) / epsilon, 0f, 0.2f);
        }

        [Fact]
        public void CaptureLimitsLongTangentsAndHasNoStraightApproachOvershoot()
        {
            float previous = 0f;
            for (int i = 0; i <= 100; i++)
            {
                var point = FormationTracking.Capture(0, 100, 0, 1000, 0, 500, 10, i * 0.1f);
                Assert.Equal(0f, point.x);
                Assert.InRange(point.z, previous, 100f);
                previous = point.z;
            }
        }

        [Theory]
        [InlineData(-3000f, 15f, 200f)]
        [InlineData(-3800f, 25f, 200f)]
        [InlineData(-10000f, 50f, 200f)]
        [InlineData(-6200f, 25f, -200f)]
        public void TargetBehindCommandsATurnInsteadOfChasingAForwardPreview(float targetZ, float travelTime, float slotVz)
        {
            var point = FormationTracking.Capture(0, targetZ, 0, 200, 0, slotVz, travelTime, 3.5f);
            Assert.True(point.z < 0f);
            FormationControlRules.SafeRejoinDirection(0, 0, 1,
                point.x, -1000, point.z, 55f, 18f, 15f, 50f,
                out float x, out float y, out float z);
            Assert.InRange(FormationControlRules.HorizontalAngle(0, 1, x, z), 54.99f, 55.01f);
            Assert.True(y >= 0f);
        }

        [Fact]
        public void DampedSlotRejectsOneTickAttitudeTwitch()
        {
            FormationTracking.DampedAxis(0, 0, 10, 0.5f, 100, 0.02f, out float p, out float v);
            Assert.InRange(p, 0f, 0.04f);
            float peak = p;
            for (int i = 0; i < 200; i++)
            {
                FormationTracking.DampedAxis(p, v, 0, 0.5f, 100, 0.02f, out p, out v);
                peak = Math.Max(peak, p);
            }
            Assert.InRange(peak, 0f, 0.4f);
            Assert.InRange(Math.Abs(p), 0f, 0.001f);
        }

        [Theory]
        [InlineData(0.02f, 150)]
        [InlineData(0.06f, 50)]
        public void SustainedChangeSettlesWithoutOvershootAtEveryGeometryStride(float dt, int ticks)
        {
            float p = 0f, v = 0f;
            for (int i = 0; i < ticks; i++)
            {
                float previous = p;
                FormationTracking.DampedAxis(p, v, 10, 0.5f, 100, dt, out p, out v);
                Assert.InRange(p, previous, 10f);
            }
            Assert.InRange(p, 9.999f, 10f);
        }

        [Fact]
        public void ShapeJumpKeepsSlotSpeedBounded()
        {
            float p = 0f, v = 0f;
            for (int i = 0; i < 1000; i++)
            {
                float previous = p;
                FormationTracking.DampedAxis(p, v, 1000, 0.5f, 50, 0.02f, out p, out v);
                Assert.InRange(v, 0f, 50.01f);
                Assert.InRange((p - previous) / 0.02f, 0f, 50.01f);
            }
        }

        [Fact]
        public void BankWrapStaysNearInvertedInsteadOfSwingingThroughLevel()
        {
            float bank = 179f;
            for (int i = 0; i < 100; i++)
            {
                bank = FormationTracking.SmoothBank(bank, -179f, 0.45f, 0.02f);
                Assert.InRange(Math.Abs(bank), 178.99f, 180f);
                Assert.InRange(bank, -180f, 180f);
            }
        }

        [Fact]
        public void TurnNoiseBandDoesNotStepTheCurveWhenItActivates()
        {
            Assert.Equal(0f, FormationTracking.QuietTurnRate(0.0059f, 0.006f));
            Assert.InRange(FormationTracking.QuietTurnRate(0.006001f, 0.006f), 0f, 0.000001f);
            Assert.Equal(0.02f, FormationTracking.QuietTurnRate(0.02f, 0.006f));
            Assert.Equal(-FormationTracking.QuietTurnRate(0.009f, 0.006f),
                FormationTracking.QuietTurnRate(-0.009f, 0.006f));
        }

        [Theory]
        [InlineData(-2f)]
        [InlineData(0f)]
        [InlineData(2f)]
        public void SmallTiltsKeepTheEstablishedQuietFlightResponse(float error)
        {
            Assert.Equal(0.35f, FormationTracking.TrackResponse(error, 0.35f));
            Assert.Equal(0.45f, FormationTracking.BankResponse(error, 0.45f));
        }

        [Theory]
        [InlineData(0.02f)]
        [InlineData(0.06f)]
        public void LargeBankReversalRespondsPromptlyWithoutOvershoot(float dt)
        {
            float bank = -60f, previous = bank, fixedBank = bank;
            for (int i = 0; i < (int)(0.3f / dt); i++)
            {
                bank = FormationTracking.SmoothBank(bank, 60f,
                    FormationTracking.BankResponse(60f - bank, 0.45f), dt);
                fixedBank = FormationTracking.SmoothBank(fixedBank, 60f, 0.45f, dt);
                Assert.InRange(bank, previous, 60f);
                previous = bank;
            }
            Assert.True(bank > 30f, $"Bank still lagging at {bank} degrees");
            Assert.True(bank > fixedBank + 25f);
        }

        [Fact]
        public void LargeTrackChangeCatchesUpWhileSmallCorrectionsRemainDamped()
        {
            float track = 0f, fixedTrack = 0f;
            for (int i = 0; i < 15; i++)
            {
                track = FormationTracking.SmoothBank(track, 25f,
                    FormationTracking.TrackResponse(25f - track, 0.35f), 0.02f);
                fixedTrack = FormationTracking.SmoothBank(fixedTrack, 25f, 0.35f, 0.02f);
            }
            Assert.InRange(track, 20f, 25f);
            Assert.True(track > fixedTrack + 5f);
            Assert.Equal(0.45f, FormationTracking.BankResponse(-358f, 0.45f));
        }

        [Theory]
        [InlineData(-1f)]
        [InlineData(1f)]
        public void HeadingRateUsesFlightTrackWithCorrectTurnSign(float side)
        {
            float angle = side * 0.02f;
            float rate = FormationTracking.TrackTurnRate(0f, 1f,
                (float)Math.Sin(angle), (float)Math.Cos(angle), 0.1f, 1.5f);
            Assert.InRange(rate * side, 0.1999f, 0.2001f);
        }

        [Fact]
        public void PitchChangeAndNearVerticalNoiseCannotInventAHorizontalTurn()
        {
            Assert.Equal(0f, FormationTracking.TrackTurnRate(0f, 1f, 0f, 0.3f, 0.02f, 1.5f));
            Assert.Equal(0f, FormationTracking.TrackTurnRate(0f, 0.01f, 0.01f, 0f, 0.02f, 1.5f));
            Assert.Equal(0f, FormationTracking.TrackTurnRate(0f, 0.01f, 0f, -0.01f, 0.02f, 1.5f));
            Assert.Equal(0f, FormationTracking.TrackTurnRate(0f, 1f, 1f, 0f, 0f, 1.5f));
        }

        [Fact]
        public void VerticalHeadingConfidenceReturnsContinuouslyAndBoundsDiscontinuities()
        {
            Assert.Equal(0f, FormationTracking.HorizontalTrackWeight(0f, 0.05f));
            Assert.InRange(FormationTracking.HorizontalTrackWeight(0f, 0.05001f), 0f, 0.00001f);
            Assert.Equal(1f, FormationTracking.HorizontalTrackWeight(0f, 0.2f));
            Assert.Equal(1.5f, FormationTracking.TrackTurnRate(0f, 1f, 1f, 0f, 0.02f, 1.5f));
        }

        [Fact]
        public void ManeuverIntensityRejectsNoiseAndDetectsActiveRotation()
        {
            Assert.Equal(0f, FormationTracking.ManeuverIntensity(0.01f, 0.01f, 0.01f));
            float active = FormationTracking.ManeuverIntensity(0.3f, 0.2f, 0.1f);
            Assert.InRange(active, 0.5f, 1.0f);
            Assert.Equal(1f, FormationTracking.ManeuverIntensity(0.5f, 0.5f, 0.5f));
        }

        [Fact]
        public void ResponsiveFiltersSharpenUnderManeuverAndHold()
        {
            float calmTrack = FormationTracking.ResponsiveTrackTime(1f, 0.35f, 0f, 0f);
            float holdTrack = FormationTracking.ResponsiveTrackTime(1f, 0.35f, 0f, 1f);
            float activeTrack = FormationTracking.ResponsiveTrackTime(1f, 0.35f, 1f, 0f);
            Assert.Equal(0.35f, calmTrack);
            Assert.True(holdTrack < calmTrack);
            Assert.True(activeTrack < calmTrack);
            Assert.InRange(activeTrack, 0.06f, 0.10f);

            float calmBank = FormationTracking.ResponsiveBankTime(2f, 0.45f, 0f, 0f);
            float holdBank = FormationTracking.ResponsiveBankTime(2f, 0.45f, 0f, 1f);
            float activeBank = FormationTracking.ResponsiveBankTime(2f, 0.45f, 1f, 0f);
            Assert.Equal(0.45f, calmBank);
            Assert.True(holdBank < calmBank);
            Assert.True(activeBank < calmBank);
            Assert.InRange(activeBank, 0.05f, 0.08f);

            float calmSlot = FormationTracking.ResponsiveSlotTime(0.5f, 0f, 0f);
            float holdSlot = FormationTracking.ResponsiveSlotTime(0.5f, 1f, 0f);
            Assert.Equal(0.5f, calmSlot);
            Assert.InRange(holdSlot, 0.11f, 0.13f);
        }
    }
}
