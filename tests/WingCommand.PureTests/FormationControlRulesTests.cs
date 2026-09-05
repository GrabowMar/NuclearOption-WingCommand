using System;
using Xunit;

namespace WingCommand.Tests
{
    public class FormationControlRulesTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(0.01f)]
        [InlineData(0.99f)]
        [InlineData(100f)]
        public void DirectAndNearDirectCollisionCoursesAreNotSkipped(float missSquared) =>
            Assert.True(FormationControlRules.CollisionThreat(missSquared, 40f));

        [Fact]
        public void OutsideProtectedRadiusIsNotAThreat() =>
            Assert.False(FormationControlRules.CollisionThreat(1601f, 40f));

        [Theory]
        [InlineData(0f, -400f)]
        [InlineData(200f, -200f)]
        [InlineData(0f, 0f)]
        public void ExactCrossingProducesOppositeFiniteLateralEscapes(float vx, float vz)
        {
            FormationControlRules.EscapeDirection(0, 0, 0, vx, vz, -1, out float x, out float y, out float z);
            FormationControlRules.EscapeDirection(0, 0, 0, -vx, -vz, 1, out float ox, out float oy, out float oz);
            Assert.InRange(Math.Abs(x * x + y * y + z * z - 1f), 0f, 0.00001f);
            Assert.Equal(0f, y);
            Assert.Equal(0f, oy);
            Assert.Equal(-x, ox);
            Assert.Equal(-z, oz);
        }

        [Fact]
        public void NonzeroMissSteersAwayFromPredictedPass()
        {
            FormationControlRules.EscapeDirection(10, 5, -20, 0, -400, -1, out float x, out float y, out float z);
            Assert.True(x < 0 && y < 0 && z > 0);
        }

        [Theory]
        [InlineData(10f)]
        [InlineData(500f)]
        [InlineData(1500f)]
        public void NativeBankAmplificationCannotExceedRequestedCeiling(float altitude)
        {
            float input = FormationControlRules.BankInput(58, altitude);
            float multiplier = Math.Max(0.6f, Math.Min(1.2f, altitude * 0.003f - 1f)) * 1.2f;
            Assert.True(input <= 58f);
            Assert.True(input * multiplier <= 58.001f);
        }

        [Fact]
        public void MatchingHorizontalTracksDoNotGrantExtraRollAuthority()
        {
            Assert.Equal(0f, FormationControlRules.HorizontalAngle(0, 200, 0, 650));
            Assert.Equal(90f, FormationControlRules.HorizontalAngle(0, 200, 650, 0));
            Assert.Equal(0f, FormationControlRules.HorizontalAngle(0, 0, 650, 0));
        }

        [Fact]
        public void TargetDirectlyBehindExecutesHorizontalTurnWithoutZoomClimb()
        {
            // Jet flying North (0, 0, 1). Slot is 500m behind and 2m above (0, 2, -500).
            FormationControlRules.SafeRejoinDirection(
                curDirX: 0f, curDirY: 0f, curDirZ: 1f,
                reqX: 0f, reqY: 2f, reqZ: -500f,
                allowedAngleDeg: 55f,
                maxPitchUpDeg: 18f,
                maxPitchDownDeg: 15f,
                radarAlt: 2000f,
                out float ox, out float oy, out float oz);

            // 1. Must be a unit vector
            float lenSq = ox * ox + oy * oy + oz * oz;
            Assert.InRange(lenSq, 0.999f, 1.001f);

            // 2. Pitch angle must be mild (matching 2m elevation over 500m), NEVER 55 deg nose-up!
            float pitchDeg = (float)(Math.Asin(oy) * 180.0 / Math.PI);
            Assert.InRange(pitchDeg, 0.1f, 1.0f);

            // 3. Horizontal heading must execute a 55-degree turn
            float horizAngle = FormationControlRules.HorizontalAngle(0f, 1f, ox, oz);
            Assert.InRange(horizAngle, 54.9f, 55.1f);
        }

        [Fact]
        public void ExtremeVerticalErrorClampsPitchUpToSafeAngle()
        {
            // Jet flying North. Slot is 1000m above and 200m ahead (0, 1000, 200) -> 78-deg raw pitch!
            FormationControlRules.SafeRejoinDirection(
                curDirX: 0f, curDirY: 0f, curDirZ: 1f,
                reqX: 0f, reqY: 1000f, reqZ: 200f,
                allowedAngleDeg: 55f,
                maxPitchUpDeg: 18f,
                maxPitchDownDeg: 15f,
                radarAlt: 2000f,
                out float ox, out float oy, out float oz);

            float lenSq = ox * ox + oy * oy + oz * oz;
            Assert.InRange(lenSq, 0.999f, 1.001f);

            float pitchDeg = (float)(Math.Asin(oy) * 180.0 / Math.PI);
            Assert.InRange(pitchDeg, 17.9f, 18.1f);
        }

        [Fact]
        public void LowAltitudeClampsDescentPitch()
        {
            // Jet low to terrain (40m radar alt). Slot is below (0, -100, 500).
            FormationControlRules.SafeRejoinDirection(
                curDirX: 0f, curDirY: 0f, curDirZ: 1f,
                reqX: 0f, reqY: -100f, reqZ: 500f,
                allowedAngleDeg: 55f,
                maxPitchUpDeg: 18f,
                maxPitchDownDeg: 15f,
                radarAlt: 40f,
                out float ox, out float oy, out float oz);

            // Must not command descent when below 60m radar altitude
            Assert.True(oy >= 0f);
        }

        [Theory]
        [InlineData(100f, 30f)]
        [InlineData(900f, 90f)]
        [InlineData(10000f, 300f)]
        public void RejoinClosureBindsToKinematicOverspeedCap(float gap, float expectedCap)
        {
            // gapGain = 0.45, closing = 0 -> raw demand would be gap * 0.45
            float closure = FormationControlRules.RejoinClosure(
                gap: gap, closing: 0f, maxDecel: 4.5f, aggression: 1f, damping: 1f,
                gapGain: 0.45f, closingDamp: 3.0f, maxStationClosure: 90f);

            Assert.InRange(closure, expectedCap - 0.1f, expectedCap + 0.1f);
        }

        [Fact]
        public void RejoinClosureForbidsPositiveClosureWhenAheadOfSlot()
        {
            // gap = -200m (ahead of slot)
            float closure = FormationControlRules.RejoinClosure(
                gap: -200f, closing: 50f, maxDecel: 4.5f, aggression: 1f, damping: 1f,
                gapGain: 0.45f, closingDamp: 3.0f, maxStationClosure: 90f);

            // Must be negative (deceleration demand), capped at -maxStationClosure
            Assert.True(closure <= 0f);
            Assert.InRange(closure, -90f, 0f);
        }
    }
}
