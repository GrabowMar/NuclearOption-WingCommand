using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace WingCommand.FlightSim
{
    public class RotaryScenarioTests
    {
        private const float Dt = RotarySimWing.Dt;

        private static FormationDefinition StaggeredTrail() => SimFormations.Get("staggered-trail");

        [Fact]
        public void HeloTrailHoldsItsSlotsThroughSTurns()
        {
            // H1: helo leader at 60 m/s, ±20° S-turns every 10 s; three helos in a staggered trail at 80 m.
            var leader = new VirtualLeader(new Vec3(0f, 300f, 0f), 60f, 0f) { CanHover = true };
            RotarySimWing wing = RotarySimWing.InSlots(leader, StaggeredTrail(), FormationCatalog.Standard, 3);
            var along2 = new double[3];
            int samples = 0;
            float minSeparation = float.MaxValue;
            for (int i = 0; i < 150 * 60; i++)
            {
                float t = i * Dt;
                leader.Step((int)(t / 10f) % 2 == 0 ? 20f : -20f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                if (t < 30f) continue;
                samples++;
                for (int k = 0; k < 3; k++) along2[k] += wing.AlongError(k) * wing.AlongError(k);
            }
            for (int k = 0; k < 3; k++)
            {
                double rms = Math.Sqrt(along2[k] / samples);
                Assert.True(rms < 10.0, $"member {k + 1}: along-track RMS {rms:0.0} m");
                Assert.Equal(BehaviourId.StationKeep, wing.Pilots[k].Mind.Current);
            }
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void HeloTrailStopsWithAHoveringLeader()
        {
            // H2: leader at 40 m/s decelerates at 2 m/s² to a hover and holds it.
            var leader = new VirtualLeader(new Vec3(0f, 300f, 0f), 40f, 0f) { CanHover = true, SpeedChangeRate = 2f };
            RotarySimWing wing = RotarySimWing.InSlots(leader, StaggeredTrail(), FormationCatalog.Standard, 3);
            var speeds = new List<float>[3];
            for (int k = 0; k < 3; k++) speeds[k] = new List<float>();
            for (int i = 0; i < 120 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt, t < 20f ? 40f : 0f, 0f);
                wing.Step();
                if (t >= 100f)
                    for (int k = 0; k < 3; k++) speeds[k].Add(wing.Plants[k].Velocity.Length);
            }
            for (int k = 0; k < 3; k++)
            {
                Assert.True(wing.SlotError(k) < 30f, $"member {k + 1} ended {wing.SlotError(k):0} m from its slot");
                double mean = 0, var = 0;
                foreach (float v in speeds[k]) mean += v;
                mean /= speeds[k].Count;
                foreach (float v in speeds[k]) var += (v - mean) * (v - mean);
                double sd = Math.Sqrt(var / speeds[k].Count);
                Assert.True(sd < 1.0, $"member {k + 1}: speed oscillation {sd:0.00} m/s over the last 20 s");
            }
            Assert.Equal(0, wing.Events.CountOf(WingEventKind.CollisionEmergency));
        }

        [Fact]
        public void RotaryFourShipTickAllocatesNothing()
        {
            var leader = new VirtualLeader(new Vec3(0f, 300f, 0f), 60f, 0f) { CanHover = true };
            RotarySimWing wing = RotarySimWing.InSlots(leader, StaggeredTrail(), FormationCatalog.Standard, 3);
            void Tick(int i)
            {
                leader.Step(i % 600 < 300 ? 20f : -20f, Dt);
                wing.Step();
            }
            for (int i = 0; i < 2000; i++) Tick(i);
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            const int n = 10000;
            for (int i = 0; i < n; i++) Tick(i);
            watch.Stop();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            double perAircraft = watch.Elapsed.TotalMilliseconds * 1000.0 / n / 3;
            Assert.True(perAircraft < 20.0, $"{perAircraft:0.00} µs per aircraft per tick");
        }
    }
}
