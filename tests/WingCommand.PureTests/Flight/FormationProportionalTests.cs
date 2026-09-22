using System;
using System.Numerics;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationProportionalTests
    {
        [Theory]
        [InlineData(-20f)]
        [InlineData(-1f)]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(20f)]
        public void StationCorrectionsScaleWithMovementAndDampClosure(float gap)
        {
            var velocity = new Vector2(0f, 200f);
            var command = FormationGuidance.Horizontal(new Vector2(gap, 0f), velocity,
                velocity, Vector2.UnitY, new Vector2(gap, 400f), velocity,
                Math.Abs(gap), 700f, 200f, 0f, 1f, 1f);
            Assert.Equal(1.35f * gap, command.Aim.X, 4);
            Assert.Equal(700f, command.Aim.Y);
            Assert.Equal(gap, FormationControlRules.KinematicVerticalCorrection(
                gap, 0f, 200f, 1f, 4f, 1f, 1f));

            var closing = FormationGuidance.Horizontal(new Vector2(gap, 0f),
                velocity + new Vector2(gap, 0f), velocity, Vector2.UnitY,
                new Vector2(gap, 400f), velocity, Math.Abs(gap), 700f, 200f, 0f, 1f, 1f);
            Assert.True(gap == 0f || closing.Correction.X * gap < 0f);
            float vertical = FormationControlRules.KinematicVerticalCorrection(
                gap, gap, 200f, 1f, 4f, 1f, 1f);
            Assert.True(gap == 0f || vertical * gap < 0f);
        }

        [Fact]
        public void LargeMovementsRemainBounded()
        {
            var velocity = new Vector2(0f, 200f);
            var command = FormationGuidance.Horizontal(new Vector2(10000f, 0f), velocity,
                velocity, Vector2.UnitY, new Vector2(10000f, 400f), velocity,
                10000f, 700f, 200f, 0f, 1f, 1f);
            Assert.Equal(command.MaxCorrection, command.Correction.Length(), 3);
            Assert.Equal(200f, FormationControlRules.KinematicVerticalCorrection(
                10000f, 0f, 200f, 1f, 4f, 1f, 1f));
        }

        [Fact]
        public void ClimbLeadRejectsNoiseAndOnlySlightlyAnticipatesSharpMoves()
        {
            foreach (float hold in new[] { 0f, 1f })
            {
                foreach (float noise in new[] { -2f, 0f, 2f })
                    Assert.Equal(10f, FormationControlRules.EffectiveClimb(10f, noise, hold));
                Assert.InRange(FormationControlRules.EffectiveClimb(10f, 2.001f, hold), 10f, 10.0001f);
                float previous = 10f;
                for (int accel = 3; accel <= 150; accel++)
                {
                    float pull = FormationControlRules.EffectiveClimb(10f, accel, hold);
                    Assert.InRange(pull, previous, 18f);
                    Assert.Equal(-pull, FormationControlRules.EffectiveClimb(-10f, -accel, hold));
                    previous = pull;
                }
                // A deliberate pull still responds immediately; a reversal changes lead direction.
                Assert.InRange(FormationControlRules.EffectiveClimb(10f, 25f, hold), 13f, 15f);
                Assert.True(FormationControlRules.EffectiveClimb(10f, -25f, hold) < 10f);
            }
        }

        [Fact]
        public void ClimbThrottleCapPreservesRejoinPowerWhenGapExceeds80M()
        {
            // On station (gap = 0, distance = 0): climbing above slot throttles back to let excess energy dissipate
            float stationThrottle = FormationControlRules.ClimbThrottleCap(
                rawThrottle: 1.0f, verticalSpeed: 10f, verticalError: -100f, gap: 0f, distance: 0f);
            Assert.True(stationThrottle <= 0.30f);

            // Rejoining (gap = 500m): never throttles back, preserving full pursuit power
            float rejoinThrottle = FormationControlRules.ClimbThrottleCap(
                rawThrottle: 1.0f, verticalSpeed: 10f, verticalError: -100f, gap: 500f, distance: 500f);
            Assert.Equal(1.0f, rejoinThrottle);

            // Rejoining from the side / front quarter (gap = 0m, but distance = 2500m): preserves full power
            float beamRejoinThrottle = FormationControlRules.ClimbThrottleCap(
                rawThrottle: 1.0f, verticalSpeed: 10f, verticalError: -100f, gap: -50f, distance: 2500f);
            Assert.Equal(1.0f, beamRejoinThrottle);
        }

        [Fact]
        public void RunawayZoomClimbAtRangeKeepsFullThrottle()
        {
            float throttle = FormationControlRules.ClimbThrottleCap(
                rawThrottle: 1.0f, verticalSpeed: 20f, verticalError: -500f, gap: 0f, distance: 5000f);
            Assert.Equal(1.0f, throttle);
        }

        [Fact]
        public void RunawayZoomClimbInsideArrivalEnvelopeCapsThrottle()
        {
            float throttle = FormationControlRules.ClimbThrottleCap(
                rawThrottle: 1.0f, verticalSpeed: 20f, verticalError: -500f, gap: 0f, distance: 500f);
            Assert.True(throttle <= 0.30f);
        }
    }
}
