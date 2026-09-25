using System;
using System.Numerics;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationRendezvousTests
    {
        [Theory]
        [InlineData(4000f, 0f, 1f, 90f)]
        [InlineData(500f, 400f, 1f, 90f)]
        [InlineData(400f, 0f, -1f, 90f)]
        [InlineData(400f, 0f, 1f, 62f)]
        public void DistantCrossingAndUnflyablySlowJoinsDoNotYieldAhead(
            float distance, float crossTrack, float alignment, float leaderSpeed)
        {
            var recovery = new FormationRecovery();
            bool allow = FormationRecovery.CanYieldAhead(distance, crossTrack, alignment, leaderSpeed, 60f, 120f);
            for (int i = 0; i < 500; i++)
                recovery.UpdateMode(leaderSpeed, 60f, -distance, 120f, 0.02f, false, allow);
            Assert.Equal(FormationRecoveryMode.Station, recovery.Mode);
        }

        [Fact]
        public void OvershootReleasesItsLaneWhenTheLeaderTurnsAway()
        {
            var recovery = new FormationRecovery();
            recovery.UpdateMode(90f, 60f, -200f, 120f, 0.02f, false, true);
            Assert.Equal(FormationRecoveryMode.Overshoot, recovery.Mode);
            recovery.UpdateMode(90f, 60f, -200f, 120f, 0.02f, false,
                FormationRecovery.CanYieldAhead(400f, 80f, 0f, 90f, 60f, 120f));
            Assert.Equal(FormationRecoveryMode.Station, recovery.Mode);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(45f)]
        [InlineData(90f)]
        [InlineData(135f)]
        [InlineData(180f)]
        public void MovingHoldingCircuitPreservesFlyingSpeedAndRelativeCourse(float degrees)
        {
            float x = (float)Math.Sin(degrees * Math.PI / 180d);
            float z = (float)Math.Cos(degrees * Math.PI / 180d);
            float relativeSpeed = FormationRecovery.CirculationSpeed(z * 55f, 55f, 66f);
            float vx = x * relativeSpeed, vz = 55f + z * relativeSpeed;
            Assert.InRange(Math.Sqrt(vx * vx + vz * vz), 65.999d, 66.001d);
            Assert.True(relativeSpeed > 0f);
        }

        [Theory]
        [InlineData(50f, 60f)]
        [InlineData(55f, 68f)]
        [InlineData(70f, 84f)]
        public void SafeSlowFormationSpeedRetainsTurningAuthority(float landingSpeed, float speed)
        {
            Assert.InRange(FormationGuidance.AirborneBankLimit(600f, speed, landingSpeed), 30f, 60f);
            Assert.InRange(FormationGuidance.AirborneBankLimit(8f, speed, landingSpeed), 8f, WingTuning.DepartureTurnBank);
        }

        [Fact]
        public void VagrantCruisingAtPlayerSpeedDoesNotEnterAnUnnecessaryHoldingCircuit()
        {
            // Convert native published stall units: 180 km/h is 50 m/s, independent of the 100 m/s
            // approach target.
            float stall = FormationGuidance.StallAirspeed(180f, 100f);
            float minimum = FormationGuidance.MinimumAirspeed(180f, 100f);
            Assert.Equal(50f, stall);
            Assert.InRange(minimum, 59.999f, 60.001f);
            var recovery = new FormationRecovery();
            for (int step = 0; step < 1000; step++)
                recovery.UpdateMode(244f / 3.6f, minimum, 0f, 120f, 0.02f);
            Assert.Equal(FormationRecoveryMode.Station, recovery.Mode);
            Assert.True(FormationGuidance.AirborneBankLimit(596f, 244f / 3.6f, stall) > 40f);
        }

        [Fact]
        public void MissingPublishedStallFallsBackToTheExistingConservativeEnvelope()
        {
            Assert.InRange(FormationGuidance.MinimumAirspeed(0f, 100f), 119.999f, 120.001f);
            Assert.InRange(FormationGuidance.MinimumAirspeed(float.NaN, 100f), 119.999f, 120.001f);
        }

        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));

        [Fact]
        public void RapidRollCanAcquireBankAuthorityBeforeTheLeaderFinishesItsTurn()
        {
            Assert.Equal(WingTuning.FormationBankRiseRate, FormationGuidance.BankRiseRate(0.1f));
            Assert.InRange(FormationGuidance.BankRiseRate((float)Math.PI / 2f), 134.9f, 135.1f);
            Assert.Equal(FormationGuidance.BankRiseRate(2f), FormationGuidance.BankRiseRate(-2f));
        }

        [Fact]
        public void SharpInterceptUsesMoreBankButStillRespectsLowSpeedAndTerrain()
        {
            float requested = FormationGuidance.InterceptBank(60f);
            Assert.Equal(70f, requested);
            Assert.Equal(requested, FormationGuidance.InterceptBank(-60f));
            Assert.Equal(8f, FormationGuidance.InterceptBank(0f));
            Assert.True(FormationGuidance.AirborneBankLimit(600f, 200f, 50f) >= requested);
            Assert.True(FormationGuidance.AirborneBankLimit(600f, 65f, 50f) < requested);
            Assert.True(FormationGuidance.AirborneBankLimit(80f, 200f, 50f) < requested);
        }
    }
}
