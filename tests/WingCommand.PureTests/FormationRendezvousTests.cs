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
        [InlineData(-1500f, -3500f, 0f, 68f, 1f, 1f)]
        [InlineData(2200f, 1800f, 0f, 68f, 1f, 1f)]
        [InlineData(0f, 3000f, 180f, 68f, 1f, 1f)]
        [InlineData(5000f, -1000f, -90f, 90f, 1f, 1f)]
        [InlineData(2200f, 1800f, 0f, 68f, 0.65f, 1.5f)]
        [InlineData(-1500f, -3500f, 0f, 68f, 1.35f, 1f)]
        [InlineData(-2500f, -9000f, 0f, 90f, 1f, 1f)]
        public void BankAndAccelerationLimitedAircraftActuallyConverges(
            float x, float z, float headingDegrees, float leaderSpeed, float aggression, float damping)
        {
            // Receding-horizon integration, not an endpoint/formula assertion. The
            // Target and follower advance every 50ms through the same production
            // horizontal command and airspeed/altitude bank envelope as live flight.
            // Roll/engine lag and finite braking model the remaining physical plant.
            const float dt = 0.05f, spacing = 120f;
            // Actual installed VT-7 metadata: stall180km/h, AI landing100m/s.
            float minimum = FormationGuidance.MinimumAirspeed(180f, 100f);
            float heading = headingDegrees * (float)Math.PI / 180f;
            float speed = 95f, bank = 0f, leaderZ = 0f, finalWorst = 0f;
            float firstCaptureSeconds = float.PositiveInfinity;
            var recovery = new FormationRecovery();
            for (int step = 0; step < 12000; step++)
            {
                float vx = (float)Math.Sin(heading) * speed, vz = (float)Math.Cos(heading) * speed;
                float gx = -x, gz = leaderZ - z;
                float distance = (float)Math.Sqrt(gx * gx + gz * gz);
                if (distance < 300f && float.IsPositiveInfinity(firstCaptureSeconds))
                    firstCaptureSeconds = step * dt;
                float blend = Clamp(distance / WingTuning.CaptureDistance, 0f, 1f);
                blend = blend * blend * (3f - 2f * blend);
                recovery.UpdateMode(leaderSpeed, minimum, gz, spacing, dt, false,
                    FormationRecovery.CanYieldAhead(distance, gx, (float)Math.Cos(heading), leaderSpeed, minimum, spacing,
                        recovery.Mode == FormationRecoveryMode.Overshoot));

                float closure = FormationControlRules.RejoinClosure(gz, vz - leaderSpeed, 2f, aggression, damping, 0.45f, 3f, 90f, 0.75f);
                float stationSpeed = leaderSpeed + closure;
                float approachSpeed = FormationTracking.ApproachSpeed(gx, gz, vx, vz, 0f, leaderSpeed, 2f, aggression, damping, 0.75f);
                float desiredSpeed = stationSpeed + (approachSpeed - stationSpeed) * blend;
                var intercept = FormationIntercept.Solve(new Vector2(gx, gz), new Vector2(0f, leaderSpeed),
                    Vector2.Zero, new Vector2(0f, leaderSpeed), speed, 340f, 0f);
                if (recovery.Blend < 0.01f)
                    desiredSpeed = FormationClosure.PursuitSpeed(new Vector2(gx, gz), new Vector2(vx, vz),
                        intercept, desiredSpeed, 340f, 2f, 0.75f, spacing);
                desiredSpeed += (Math.Max(minimum, leaderSpeed - 10f) - desiredSpeed) * recovery.Blend;
                desiredSpeed = Clamp(desiredSpeed, minimum, 340f);
                speed += Clamp((desiredSpeed - speed) / 1.5f, -2f, 3f) * dt;

                float baseline = Math.Max(650f, speed * 3.5f);
                var guidance = FormationGuidance.Horizontal(new Vector2(gx, gz), new Vector2(vx, vz),
                    new Vector2(0f, leaderSpeed), Vector2.UnitY,
                    intercept.Gap, intercept.ArrivalVelocity,
                    distance, baseline, speed, blend, aggression, damping);
                float ax = guidance.Aim.X, az = guidance.Aim.Y;
                if (recovery.Blend > 0f)
                {
                    float laneX = FormationRecovery.LaneCorrection(x, x < 0f ? -spacing * 2f : spacing * 2f, baseline) * baseline;
                    ax += (laneX - ax) * recovery.Blend;
                    az += (baseline - az) * recovery.Blend;
                }
                FormationControlRules.SafeRejoinDirection(vx, 0f, vz, ax, 0f, az,
                    55f, 18f, 15f, 600f, out float sx, out _, out float sz);
                float error = FormationTracking.WrapDegrees(((float)Math.Atan2(sx, sz) - heading) * 180f / (float)Math.PI);
                float airframeBank = FormationGuidance.AirborneBankLimit(600f, speed, minimum / 1.2f);
                float bankCeiling = Math.Min(distance > 1500f ? 45f : 40f + 18f * blend, airframeBank);
                bankCeiling += (Math.Min(bankCeiling, 25f) - bankCeiling) * recovery.Blend;
                float bankDemand = Clamp(Math.Abs(error) * (distance > 1500f ? 1.5f : 3f), 8f, bankCeiling);
                // Decompiled AutoAim projects a level lateral waypoint to a 90deg
                // roll demand, then clamps to bankAllowed and applies these factors.
                float demandedBank = Math.Sign(error) * FormationControlRules.BankInput(bankDemand, 600f) * 0.8f * 1.2f;
                bank += (demandedBank - bank) * (1f - (float)Math.Exp(-dt / 0.8f));
                heading += 9.81f * (float)Math.Tan(bank * Math.PI / 180d) / Math.Max(speed, 1f) * dt;
                x += (float)Math.Sin(heading) * speed * dt;
                z += (float)Math.Cos(heading) * speed * dt;
                leaderZ += leaderSpeed * dt;
                if (step >= 11400) finalWorst = Math.Max(finalWorst, distance);
                Assert.True(float.IsFinite(x) && float.IsFinite(z));
            }
            Assert.InRange(finalWorst, 0f, 100f);
            Assert.InRange(firstCaptureSeconds, 0f, 180f);
            Assert.InRange(Math.Abs(speed - leaderSpeed), 0f, 3f);
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
            // EncyclopediaBrowser divides AircraftInfo.stallSpeed by3.6; the
            // installed VT-7's native landing target100 is unrelated to stall50.
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
            Assert.InRange(FormationGuidance.BankRiseRate((float)Math.PI / 2f), 89.9f, 90.1f);
            Assert.Equal(FormationGuidance.BankRiseRate(2f), FormationGuidance.BankRiseRate(-2f));
        }
    }
}
