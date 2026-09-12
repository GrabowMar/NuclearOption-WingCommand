using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class SharpTurnPolicyTests
    {
        [Fact]
        public void RotaryAutopilotIsNotEligibleForSharpPlaneManeuvers()
        {
            bool eligible = SharpTurnPolicy.IsEligible(
                isPlaneAutopilot: false,
                isDisabled: false,
                hasPlayer: false,
                runwayAlign: false,
                gearDeployed: false,
                radarAlt: 500f,
                speed: 200f,
                landingSpeed: 60f);

            Assert.False(eligible);
        }

        [Theory]
        [InlineData(true, false, false, false, 500f, 200f)]   // Disabled
        [InlineData(false, true, false, false, 500f, 200f)]   // Player controlled
        [InlineData(false, false, true, false, 500f, 200f)]   // Runway align
        [InlineData(false, false, false, true, 500f, 200f)]   // Gear deployed
        [InlineData(false, false, false, false, 40f, 200f)]   // Below 60m AGL safe floor
        [InlineData(false, false, false, false, 500f, 65f)]   // Below 1.15x landing speed (60 * 1.15 = 69)
        public void SafetyConditionsInhibitSharpManeuvers(
            bool disabled, bool player, bool runway, bool gear, float radarAlt, float speed)
        {
            bool eligible = SharpTurnPolicy.IsEligible(
                isPlaneAutopilot: true,
                isDisabled: disabled,
                hasPlayer: player,
                runwayAlign: runway,
                gearDeployed: gear,
                radarAlt: radarAlt,
                speed: speed,
                landingSpeed: 60f);

            Assert.False(eligible);
        }

        [Fact]
        public void FastAirbornePlaneIsEligible()
        {
            bool eligible = SharpTurnPolicy.IsEligible(
                isPlaneAutopilot: true,
                isDisabled: false,
                hasPlayer: false,
                runwayAlign: false,
                gearDeployed: false,
                radarAlt: 300f,
                speed: 180f,
                landingSpeed: 60f);

            Assert.True(eligible);
        }

        [Fact]
        public void SafeBankCeilingScalesWithRadarAltitude()
        {
            // Below safe floor (60m): clamped to 60 deg max
            float lowCeiling = SharpTurnPolicy.SafeBankCeiling(88f, 30f);
            Assert.Equal(60f, lowCeiling);

            // High altitude (>= 200m): unlocks full combat bank authority up to 140 deg
            float highCeiling = SharpTurnPolicy.SafeBankCeiling(88f, 500f);
            Assert.Equal(140f, highCeiling);

            // Intermediate altitude (130m): between 70 and 140
            float midCeiling = SharpTurnPolicy.SafeBankCeiling(70f, 130f);
            Assert.InRange(midCeiling, 100f, 110f);
        }

        [Fact]
        public void CornerSpeedControlEmploysAirbrakeAboveCornerSpeed()
        {
            float cornerSpeed = 180f;

            // Flying well above corner speed (250 m/s) into a 60 deg break turn -> airbrake deployed, throttle cut
            var (fastThr, fastBrk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 250f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 60f,
                energyFighter: false);
            Assert.Equal(0f, fastThr);
            Assert.Equal(1f, fastBrk);

            // Near corner speed (190 m/s < 1.15 * 180 = 207 m/s) -> full power, brake off
            var (cornerThr, cornerBrk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 190f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 60f,
                energyFighter: false);
            Assert.Equal(1f, cornerThr);
            Assert.Equal(0f, cornerBrk);

            // Small turn angle (20 deg) -> normal flight power
            var (mildThr, mildBrk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 250f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 20f,
                energyFighter: false);
            Assert.Equal(1f, mildThr);
            Assert.Equal(0f, mildBrk);
        }

        [Fact]
        public void EnergyFighterPerkPreservesPowerBelowCornerSpeed()
        {
            float cornerSpeed = 180f;
            var (thr, brk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 150f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 70f,
                energyFighter: true);
            Assert.Equal(1f, thr);
            Assert.Equal(0f, brk);
        }

        [Fact]
        public void SpeedControlInhibitsBrakingNearGroundOrInDescent()
        {
            float cornerSpeed = 180f;

            // Low altitude (< 200m): never airbrake or cut power even above corner speed into a sharp turn
            var (lowAltThr, lowAltBrk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 250f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 60f,
                energyFighter: false,
                radarAlt: 150f,
                verticalSpeed: 0f);
            Assert.Equal(1f, lowAltThr);
            Assert.Equal(0f, lowAltBrk);

            // High sink rate (verticalSpeed < -10 m/s): never airbrake or cut power
            var (sinkThr, sinkBrk) = SharpTurnPolicy.ComputeSpeedControl(
                airspeed: 250f,
                cornerSpeed: cornerSpeed,
                turnAngleDeg: 60f,
                energyFighter: false,
                radarAlt: 800f,
                verticalSpeed: -20f);
            Assert.Equal(1f, sinkThr);
            Assert.Equal(0f, sinkBrk);
        }

        [Fact]
        public void PitchAuthorityBoostsAsRollAlignsInTurn()
        {
            // Small turn: no boost
            Assert.Equal(1f, SharpTurnPolicy.ComputePitchAuthority(rollErrorDeg: 10f, turnAngleDeg: 15f));

            // Bank not yet aligned (> 60 deg roll error): no boost
            Assert.Equal(1f, SharpTurnPolicy.ComputePitchAuthority(rollErrorDeg: 75f, turnAngleDeg: 60f));

            // Roll nearly complete (15 deg roll error) during sharp turn: boosts elevator authority into slice turn
            float boost = SharpTurnPolicy.ComputePitchAuthority(rollErrorDeg: 15f, turnAngleDeg: 60f);
            Assert.True(boost > 1.5f);
        }

        [Fact]
        public void CoordinatedRudderKickAssistsRollIn()
        {
            // Below 30m altitude: no rudder kick
            Assert.Equal(0f, SharpTurnPolicy.ComputeRudderKick(45f, 30f, 20f));

            // Small heading error: no rudder kick
            Assert.Equal(0f, SharpTurnPolicy.ComputeRudderKick(10f, 30f, 200f));

            // Right turn roll-in: positive rudder kick
            float rightKick = SharpTurnPolicy.ComputeRudderKick(45f, 30f, 200f);
            Assert.InRange(rightKick, 0.2f, 0.35f);

            // Left turn roll-in: negative rudder kick
            float leftKick = SharpTurnPolicy.ComputeRudderKick(-45f, 30f, 200f);
            Assert.InRange(leftKick, -0.35f, -0.2f);
        }

        [Fact]
        public void DefensiveBankLimitExpandsForMissileEvasion()
        {
            // Below safe floor: 60 deg
            Assert.Equal(60f, SharpTurnPolicy.DefensiveBankLimit(false, false, 40f));

            // Normal missile defence at altitude: 110 deg
            Assert.Equal(110f, SharpTurnPolicy.DefensiveBankLimit(false, false, 300f));

            // Terminal missile defence at altitude: 135 deg
            Assert.Equal(135f, SharpTurnPolicy.DefensiveBankLimit(true, false, 300f));

            // BreakTurn perk in terminal defence at altitude: 180 deg
            Assert.Equal(180f, SharpTurnPolicy.DefensiveBankLimit(true, true, 300f));
        }
    }
}
