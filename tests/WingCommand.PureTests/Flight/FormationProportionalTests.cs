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
        public void EffectiveClimbAnticipatesVerticalAccelAndPitchPull()
        {
            // Level flight baseline
            float levelClimb = FormationControlRules.EffectiveClimb(0f, 0f, 0f, 150f, 0f);
            Assert.Equal(0f, levelClimb);

            // Pull-up with 25 m/s² vertical accel and 0.25 rad/s pitch rate
            float pullClimbCalm = FormationControlRules.EffectiveClimb(10f, 25f, 0.25f, 150f, 0f);
            float pullClimbHold = FormationControlRules.EffectiveClimb(10f, 25f, 0.25f, 150f, 1f);

            Assert.True(pullClimbCalm > 20f);
            Assert.True(pullClimbHold > pullClimbCalm); // Stronger lead in HOLD
        }

        [Fact]
        public void TargetBankMatchesLeaderInHoldAndBlendsTurnNavigation()
        {
            float turnBank = 10f;
            float leaderBank = 45f;
            float leaderRollRate = 0.2f;

            // Near slot in HOLD: matches leader bank + roll rate lead closely
            float onStationHold = FormationControlRules.TargetBank(turnBank, leaderBank, leaderRollRate, 1f, 0f, 60f);
            Assert.True(onStationHold > 40f);

            // Far out: follows navigation turnBank
            float farRejoin = FormationControlRules.TargetBank(turnBank, leaderBank, leaderRollRate, 1f, 1f, 60f);
            Assert.Equal(turnBank, farRejoin);
        }

        [Fact]
        public void RollFeedforwardCommandsAileronForBankMismatchAndDampsRate()
        {
            // 30 deg bank deficit with zero relative roll rate -> strong roll demand
            float rollDemand = FormationControlRules.RollFeedforward(30f, 0f, 0f, 1f, 0f);
            Assert.InRange(rollDemand, 0.7f, 1.0f);

            // Leader rolling fast right -> positive feedforward even at zero error
            float rateDemand = FormationControlRules.RollFeedforward(0f, 1.5f, 0f, 1f, 0f);
            Assert.InRange(rateDemand, 0.1f, 0.3f);

            // Far out of position -> fades to zero
            float distant = FormationControlRules.RollFeedforward(30f, 1.5f, 0f, 1f, 1f);
            Assert.Equal(0f, distant);
        }

        [Fact]
        public void PitchFeedforwardProvidesImmediateElevatorAssist()
        {
            // Leader pulling pitch up faster than wingman with positive vertical accel
            float pitchDemand = FormationControlRules.PitchFeedforward(0.4f, 20f, 1f, 0f);
            Assert.InRange(pitchDemand, 0.3f, 0.8f);

            // Far out -> fades to zero
            float distant = FormationControlRules.PitchFeedforward(0.4f, 20f, 1f, 1f);
            Assert.Equal(0f, distant);
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
    }
}
