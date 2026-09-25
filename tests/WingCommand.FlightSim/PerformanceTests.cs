using System;
using System.Diagnostics;
using Xunit;

namespace WingCommand.FlightSim
{
    public class PerformanceTests
    {
        [Fact]
        public void GuidanceAndPipelineStepAllocateNothingAndTakeUnderTwentyMicroseconds()
        {
            const float dt = 1f / 60f;
            AirframeProfile profile = SimProfiles.GenericFighter();
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(60f, 2000f, -20f), 200f, 0f);
            var pipeline = new FixedWingPipeline();
            var ctx = new LimitContext { FloorY = 0f, Clearance = 60f, Aggression = 1f };
            var intent = new FlightIntent
            {
                Limits = new SpeedLimits(profile.MinimumSpeed(1f), profile.MaxSpeed, false, true),
                Precision = 1f, Aggression = 1f, Spacing = 80f, TerrainClearance = 60f,
            };

            void Tick(int i)
            {
                leader.Step(i % 480 < 240 ? 45f : -45f, dt);
                intent.Ref = leader.Slot(60f, 20f, 0f);
                AircraftState s = SimSensor.Read(plant, dt);
                GuidanceCommand g = TrackingGuidance.Evaluate(intent, s, profile);
                ControlOutput o = pipeline.Step(g, s, ctx, profile, dt);
                plant.Step(new PlantInput(o.Pitch, o.Roll, o.Throttle), dt);
            }

            for (int i = 0; i < 2000; i++) Tick(i);   // warm-up (JIT)

            double microseconds = Bench.BestMicroseconds(Tick, 5, 4000, out long allocated);
            Assert.Equal(0L, allocated);
            Assert.True(microseconds < 20.0, $"{microseconds:0.00} µs per tick (sensor + guidance + pipeline + plant)");
        }

        [Fact]
        public void FormationTickForAFourShipAllocatesNothingAndStaysUnderTwentyMicrosecondsPerAircraft()
        {
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);

            void Tick(int i)
            {
                leader.Step(i % 480 < 240 ? 45f : -45f, SimWing.Dt);
                wing.Step();
            }

            for (int i = 0; i < 2000; i++) Tick(i);   // warm-up (JIT)

            double perAircraft = Bench.BestMicroseconds(Tick, 5, 4000, out long allocated) / wing.Plants.Length;
            Assert.Equal(0L, allocated);
            Assert.True(perAircraft < 20.0, $"{perAircraft:0.00} µs per aircraft per tick (wing share + pilot + plant)");
        }

        [Fact]
        public void TheBenchReportsARealCostNotHidesIt()
        {
            // The fastest batch is still the tick's own cost: a tick that takes 30 µs never reads under the 20 µs budget.
            var spin = new Stopwatch();
            void Slow(int i)
            {
                spin.Restart();
                while (spin.Elapsed.TotalMilliseconds < 0.03) { }
            }
            Assert.True(Bench.BestMicroseconds(Slow, 3, 20, out _) >= 30.0);
        }
    }
}
