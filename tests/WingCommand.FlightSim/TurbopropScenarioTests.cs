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
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, true, true), // as FormationPilot: full power allowed
                Precision = 1f,
                Aggression = 0.5f,
                Spacing = spacing,
                TerrainClearance = 60f,
            };

        [Fact]
        public void TurbopropBankStepsAtSixtyMetresPerSecondSettleQuicklyWithoutOvershoot()
        {
            // In game the CI-22's bank lagged 1–1.5 s and overshot ~20° at 55–62 m/s: the controller scaled its roll
            // stick for 286°/s while full stick gave ≈ 57°/s. ±30° steps through the whole pipeline.
            AirframeProfile profile = SimProfiles.CoinTurboprop();
            var plant = new FixedWingPlant(PlantParams.CoinTurboprop, new Vec3(0f, 1500f, 0f), 60f, 0f);
            plant.SetThrottleState(plant.TrimThrottle());
            var pipeline = new FixedWingPipeline();
            pipeline.Track(SimSensor.Read(plant, Dt), new ControlOutput { Throttle = plant.ThrottleActual }, profile);

            float target = 0f, stepStart = 0f, overshoot = 0f;
            bool settled = true;
            double riseSum = 0;
            int steps = 0;
            for (int i = 0; i < 60 * 60; i++)
            {
                float t = i * Dt;
                float want = t < 4f ? 0f : ((int)((t - 4f) / 8f) % 2 == 0 ? 30f : -30f);
                if (want != target)
                {
                    if (!settled) riseSum += 8f;
                    target = want;
                    stepStart = t;
                    settled = false;
                    steps++;
                }
                AircraftState s = SimSensor.Read(plant, Dt);
                Vec3 right = Vec3.Cross(Vec3.Up, s.Vel.Normalized).Normalized;
                var g = new GuidanceCommand
                {
                    Accel = right * (Scalar.G * (float)Math.Tan(target * Scalar.Deg2Rad)) - Vec3.Up * (s.Vel.Y / profile.TauVel),
                    VelCmd = s.Vel.Horizontal.Normalized * 60f,
                    AfterburnerAllowed = true,
                };
                ControlOutput c = pipeline.Step(g, s, new LimitContext { FloorY = float.NaN, Aggression = 0.5f }, profile, Dt);
                plant.Step(new PlantInput(c.Pitch, c.Roll, c.Throttle), Dt);
                if (t < 4f) continue;
                float e = plant.BankDeg - target;
                if (!settled && Math.Abs(e) < 3f)
                {
                    settled = true;
                    riseSum += t - stepStart;
                }
                if (settled) overshoot = Math.Max(overshoot, Math.Sign(target) * e);
            }
            double rise = riseSum / steps;
            Assert.True(overshoot < 3f, $"overshoot {overshoot:0.0} deg");
            Assert.True(rise < 1.8, $"mean time to within 3 deg {rise:0.00} s");
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
