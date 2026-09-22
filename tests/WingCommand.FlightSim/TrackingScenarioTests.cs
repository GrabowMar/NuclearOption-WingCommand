using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class TrackingScenarioTests
    {
        private const float Dt = 1f / 60f;

        private static FlightIntent SlotIntent(RefState slot, AirframeProfile p, bool afterburner, float spacing) =>
            new FlightIntent
            {
                Ref = slot,
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, afterburner, true),
                Precision = 1f,
                Aggression = 1f,
                Spacing = spacing,
                TerrainClearance = 60f,
            };

        [Fact]
        public void ReversalsKeepACloseSlotWithinFifteenMetresAndMatchBank()
        {
            AirframeProfile profile = SimProfiles.GenericFighter();
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            RefState start = leader.Slot(60f, 20f, 0f);
            var plant = new FixedWingPlant(PlantParams.GenericFighter, start.Pos, 200f, 0f);
            var pilot = new SimPilot(plant, profile);

            double sumErr2 = 0, sumBank2 = 0;
            int samples = 0, saturated = 0, reversals = 0;
            float lastThrottle = plant.ThrottleActual, lastDelta = 0f, deltaWindow = 0f;
            for (int i = 0; i < 65 * 60; i++)
            {
                float t = i * Dt;
                float bank = t < 5f ? 0f : ((int)((t - 5f) / 8f) % 2 == 0 ? 60f : -60f);
                leader.Step(bank, Dt);
                RefState slot = leader.Slot(60f, 20f, 0f);
                pilot.StepTracking(SlotIntent(slot, profile, false, 80f), Dt);
                if (t < 5f) continue;
                float err = (slot.Pos - plant.Position).Length;
                sumErr2 += err * err;
                float bankErr = Scalar.Wrap180(plant.BankDeg - leader.BankDeg);
                sumBank2 += bankErr * bankErr;
                if (Math.Abs(pilot.Last.Roll) >= 0.99f) saturated++;
                deltaWindow += plant.ThrottleActual - lastThrottle;
                lastThrottle = plant.ThrottleActual;
                if (i % 30 == 0)
                {
                    if (Math.Abs(deltaWindow) > 0.05f && Math.Sign(deltaWindow) != Math.Sign(lastDelta) && lastDelta != 0f) reversals++;
                    if (Math.Abs(deltaWindow) > 0.05f) lastDelta = deltaWindow;
                    deltaWindow = 0f;
                }
                samples++;
            }
            double rmsErr = Math.Sqrt(sumErr2 / samples), rmsBank = Math.Sqrt(sumBank2 / samples);
            double minutes = samples * Dt / 60.0;
            Assert.True(rmsErr < 15.0, $"slot RMS {rmsErr:0.0} m");
            Assert.True(rmsBank < 8.0, $"bank RMS {rmsBank:0.0} deg");
            Assert.True(saturated < 0.05 * samples, $"roll saturated {100.0 * saturated / samples:0.0}%");
            Assert.True(reversals / minutes < 12.0, $"{reversals / minutes:0.0} throttle reversals/min");
        }

        [Fact]
        public void JoinFromFourKilometresAsternCapturesWithoutOvershoot()
        {
            AirframeProfile profile = SimProfiles.GenericFighter();
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 4000f), 200f, 0f);
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(80f, 2000f, 0f), 140f, 0f);
            var pilot = new SimPilot(plant, profile);
            const float spacing = 80f;

            float inside = 0f, captureTime = float.NaN, maxAhead = float.NegativeInfinity;
            for (int i = 0; i < 180 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt);
                RefState slot = leader.Slot(spacing, 0f, 0f);
                pilot.StepTracking(SlotIntent(slot, profile, true, spacing), Dt);
                Vec3 e = plant.Position - slot.Pos;
                maxAhead = Math.Max(maxAhead, e.Z);
                inside = e.Length < 0.25f * spacing ? inside + Dt : 0f;
                if (float.IsNaN(captureTime) && inside >= 5f) captureTime = t;
            }
            Assert.False(float.IsNaN(captureTime), "never captured");
            Assert.True(captureTime < 120f, $"captured at {captureTime:0} s");
            Assert.True(maxAhead < spacing, $"passed {maxAhead:0} m ahead of the slot");
        }

        [Fact]
        public void ValleyFlightNeverDescendsIntoTheFloor()
        {
            AirframeProfile profile = SimProfiles.GenericFighter();
            var leader = new VirtualLeader(new Vec3(0f, 300f, 0f), 180f, 0f);
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(60f, 300f, -20f), 180f, 0f);
            var pilot = new SimPilot(plant, profile);
            float minClearance = float.PositiveInfinity;
            for (int i = 0; i < 90 * 60; i++)
            {
                float t = i * Dt;
                float bank = (int)(t / 10f) % 2 == 0 ? 45f : -45f;
                leader.Step(bank, Dt);
                // Terrain rises and falls under the path; the slot sits 100 m above the leader's own floor.
                float floorY = 200f + 120f * (float)Math.Sin(plant.Position.Z / 1500f);
                RefState slot = leader.Slot(60f, 20f, 0f);
                var raised = new RefState(new Vec3(slot.Pos.X, Math.Max(slot.Pos.Y, floorY + 100f), slot.Pos.Z), slot.Vel, slot.Acc);
                pilot.StepTracking(SlotIntent(raised, profile, false, 80f), Dt, floorY);
                minClearance = Math.Min(minClearance, plant.Position.Y - floorY);
            }
            Assert.True(minClearance > 0f, $"minimum clearance {minClearance:0} m");
        }
    }
}
