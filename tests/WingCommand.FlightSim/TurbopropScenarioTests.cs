using System;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Scenarios on the CI-22-like turboprop, whose weak roll loop and low excess power broke the
    /// first in-game session (bank lagging 1–1.5 s with 20° overshoots; wingmen stuck near stall).</summary>
    public class TurbopropScenarioTests
    {
        private const float Dt = 1f / 60f;

        private static FlightIntent SlotIntent(RefState slot, AirframeProfile p, float spacing) =>
            new FlightIntent
            {
                Ref = slot,
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, false, true),
                Precision = 1f,
                Aggression = 0.5f,
                Spacing = spacing,
                TerrainClearance = 60f,
            };

        [Fact]
        public void TurbopropCloseSlotMatchesBankThroughGentleReversals()
        {
            AirframeProfile profile = SimProfiles.CoinTurboprop();
            var leader = new VirtualLeader(new Vec3(0f, 1500f, 0f), 100f, 0f);
            RefState start = leader.Slot(40f, 15f, 0f);
            var plant = new FixedWingPlant(PlantParams.CoinTurboprop, start.Pos, 100f, 0f);
            plant.SetThrottleState(plant.TrimThrottle());
            var pilot = new SimPilot(plant, profile);

            double sumErr2 = 0, sumBank2 = 0;
            int samples = 0;
            for (int i = 0; i < 70 * 60; i++)
            {
                float t = i * Dt;
                float bank = t < 5f ? 0f : ((int)((t - 5f) / 10f) % 2 == 0 ? 30f : -30f);
                leader.Step(bank, Dt);
                RefState slot = leader.Slot(40f, 15f, 0f);
                pilot.StepTracking(SlotIntent(slot, profile, 50f), Dt);
                if (t < 10f) continue;
                float err = (slot.Pos - plant.Position).Length;
                sumErr2 += err * err;
                float bankErr = Scalar.Wrap180(plant.BankDeg - leader.BankDeg);
                sumBank2 += bankErr * bankErr;
                samples++;
            }
            double rmsErr = Math.Sqrt(sumErr2 / samples), rmsBank = Math.Sqrt(sumBank2 / samples);
            Assert.True(rmsBank < 8.0, $"bank RMS {rmsBank:0.0} deg");
            Assert.True(rmsErr < 15.0, $"slot RMS {rmsErr:0.0} m");
        }

        [Fact]
        public void TurbopropChasingASlowClimbingLeaderKeepsFlyingSpeed()
        {
            // The in-game CI-22s were air-started behind a leader climbing slowly and sagged to 42–57 m/s at full
            // throttle while the guidance kept asking for climb and catch-up.
            AirframeProfile profile = SimProfiles.CoinTurboprop();
            var leader = new VirtualLeader(new Vec3(0f, 1500f, 0f), 62f, 0f);
            var plant = new FixedWingPlant(PlantParams.CoinTurboprop, new Vec3(150f, 1500f, -800f), 50f, 0f);
            plant.SetThrottleState(plant.TrimThrottle());
            var pilot = new SimPilot(plant, profile);

            float minTas = float.MaxValue;
            for (int i = 0; i < 120 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt, 62f, t < 5f ? 0f : 8f);
                RefState slot = leader.Slot(40f, 15f, 0f);
                pilot.StepTracking(SlotIntent(slot, profile, 50f), Dt);
                if (t > 2f) minTas = Math.Min(minTas, plant.Speed);
            }
            Assert.True(minTas >= profile.MinimumSpeed(1f), $"TAS fell to {minTas:0.0} m/s (minimum {profile.MinimumSpeed(1f):0.0})");
        }
    }
}
