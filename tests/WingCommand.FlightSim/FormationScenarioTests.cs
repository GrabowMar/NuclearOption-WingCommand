using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class FormationScenarioTests
    {
        private const float Dt = SimWing.Dt;

        [Fact]
        public void FourShipJoinsFromFourKilometresAsternInTurnWithoutConflicts()
        {
            // J1: leader 4 km ahead; wingmen 60 m/s slower, 150 m low, spread by slot side; afterburner allowed.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 4000f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-150f, 1850f, 0f), new Vec3(150f, 1850f, 0f), new Vec3(300f, 1850f, -150f) }, 140f, 0f);
            var captured = new[] { float.NaN, float.NaN, float.NaN };
            var inside = new float[3];
            float maxAhead = float.NegativeInfinity, minSeparation = float.MaxValue;
            for (int i = 0; i < 200 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                {
                    Vec3 e = wing.Plants[k].Position - wing.Wing.Frame.Slots[k].Ref.Pos;
                    maxAhead = Math.Max(maxAhead, e.Z);
                    inside[k] = e.Length < 0.25f * FormationCatalog.Standard ? inside[k] + Dt : 0f;
                    if (float.IsNaN(captured[k]) && inside[k] >= 5f) captured[k] = wing.Time;
                }
            }
            for (int k = 0; k < 3; k++)
                Assert.True(captured[k] < 120f + 15f * k, $"member {k + 1} captured at {captured[k]:0} s");
            Assert.True(maxAhead < FormationCatalog.Standard, $"passed {maxAhead:0} m ahead of a slot");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void OppositeHeadingJoinConvergesWithoutConflicts()
        {
            // J2: the wing starts 6 km ahead of the leader, flying toward it.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-200f, 2000f, 6000f), new Vec3(200f, 2000f, 6000f), new Vec3(400f, 2000f, 6200f) }, 200f, 180f);
            var captured = new[] { float.NaN, float.NaN, float.NaN };
            var inside = new float[3];
            float minSeparation = float.MaxValue;
            for (int i = 0; i < 300 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                {
                    inside[k] = wing.SlotError(k) < 0.25f * FormationCatalog.Standard ? inside[k] + Dt : 0f;
                    if (float.IsNaN(captured[k]) && inside[k] >= 5f) captured[k] = wing.Time;
                }
            }
            for (int k = 0; k < 3; k++) Assert.False(float.IsNaN(captured[k]), $"member {k + 1} never captured");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        /// <summary>Counts sign changes of 0.5 s throttle trends larger than 0.05 (the M1a R1 metric).</summary>
        private sealed class ReversalCounter
        {
            private float last, lastDelta, window;
            private int ticks;
            public int Count;

            public ReversalCounter(float start) => last = start;

            public void Add(float throttle)
            {
                window += throttle - last;
                last = throttle;
                if (++ticks % 30 != 0) return;
                if (Math.Abs(window) > 0.05f && lastDelta != 0f && Math.Sign(window) != Math.Sign(lastDelta)) Count++;
                if (Math.Abs(window) > 0.05f) lastDelta = window;
                window = 0f;
            }
        }

        private static float Reversal(float t) => t < 5f ? 0f : ((int)((t - 5f) / 8f) % 2 == 0 ? 60f : -60f);

        [Fact]
        public void CloseFingerFourHoldsThroughReversals()
        {
            // R1: ±60° reversals every 8 s at 200 m/s, Close (40 m) finger four started in its slots.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Close, 3);
            var err2 = new double[3];
            var bank2 = new double[3];
            var bankSamples = new int[3];
            var saturated = new int[3];
            var reversals = new ReversalCounter[3];
            for (int k = 0; k < 3; k++) reversals[k] = new ReversalCounter(wing.Plants[k].ThrottleActual);
            int samples = 0;
            for (int i = 0; i < 65 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(Reversal(t), Dt);
                wing.Step();
                if (t < 5f) continue;
                samples++;
                for (int k = 0; k < 3; k++)
                {
                    float e = wing.SlotError(k);
                    err2[k] += e * e;
                    // Close slots: one spacing out from the leader (#2 and #3 of a finger four).
                    if (Math.Abs(wing.Wing.Frame.Slots[k].Lateral) <= 1.01f * FormationCatalog.Close)
                    {
                        float b = Scalar.Wrap180(wing.Plants[k].BankDeg - leader.BankDeg);
                        bank2[k] += b * b;
                        bankSamples[k]++;
                    }
                    if (Math.Abs(wing.Pilots[k].LastOutput.Roll) >= 0.99f) saturated[k]++;
                    reversals[k].Add(wing.Plants[k].ThrottleActual);
                }
            }
            double minutes = samples * Dt / 60.0;
            for (int k = 0; k < 3; k++)
            {
                double rms = Math.Sqrt(err2[k] / samples);
                Assert.True(rms < 15.0, $"member {k + 1}: slot RMS {rms:0.0} m");
                Assert.True(bankSamples[k] > 0 == k < 2, $"member {k + 1}: close-slot classification");
                if (bankSamples[k] > 0)
                {
                    double bankRms = Math.Sqrt(bank2[k] / bankSamples[k]);
                    Assert.True(bankRms < 8.0, $"member {k + 1}: bank RMS {bankRms:0.0} deg");
                }
                Assert.True(saturated[k] < 0.05 * samples, $"member {k + 1}: roll saturated {100.0 * saturated[k] / samples:0.0}%");
                Assert.True(reversals[k].Count / minutes < 12.0, $"member {k + 1}: {reversals[k].Count / minutes:0.0} throttle reversals/min");
            }
        }

        [Fact]
        public void CombatSpreadStaysLevelThroughReversals()
        {
            // R2: R1's leader with the wing in combat spread (350 m): wide slots stay level.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            FormationDefinition spread = SimFormations.Get("combat-spread");
            SimWing wing = SimWing.InSlots(leader, spread, FormationCatalog.Spread, 3);
            float maxExcursion = 0f, maxSlotTilt = 0f, maxError = 0f;
            for (int i = 0; i < 65 * 60; i++)
            {
                leader.Step(Reversal(i * Dt), Dt);
                wing.Step();
                for (int k = 0; k < 3; k++)
                {
                    float levelY = leader.Position.Y + spread.Slots[k].Up * FormationCatalog.StackMetres;
                    maxSlotTilt = Math.Max(maxSlotTilt, Math.Abs(wing.Wing.Frame.Slots[k].Ref.Pos.Y - levelY));
                    maxExcursion = Math.Max(maxExcursion, Math.Abs(wing.Plants[k].Position.Y - levelY));
                    maxError = Math.Max(maxError, wing.SlotError(k));
                }
            }
            Assert.True(maxSlotTilt < 5f, $"a wide slot moved {maxSlotTilt:0.0} m vertically");
            Assert.True(maxExcursion < 50f, $"vertical excursion {maxExcursion:0} m");
            Assert.True(maxError < FormationCatalog.Spread, $"slot error reached {maxError:0} m");
        }

        [Fact]
        public void ThirtyDegreeZoomAndDiveNeverStallsOrOverspeeds()
        {
            // Z1: 30° zoom through +3 km, then a 30° dive back down; finger four at standard spacing.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 220f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);
            AirframeProfile p = wing.Profile;
            float minSpeed = float.MaxValue, maxSpeed = 0f;
            int phase = 0;   // 0 level, 1 zoom, 2 dive, 3 level
            for (int i = 0; i < 180 * 60; i++)
            {
                if (phase == 0 && i * Dt > 5f) phase = 1;
                if (phase == 1 && leader.Position.Y > 5000f) phase = 2;
                if (phase == 2 && leader.Position.Y < 2500f) phase = 3;
                float gamma = phase == 1 ? 30f : phase == 2 ? -30f : 0f;
                float speed = phase == 1 ? 170f : phase == 2 ? 260f : 220f;
                leader.Step(0f, Dt, speed, gamma);
                wing.Step();
                for (int k = 0; k < 3; k++)
                {
                    minSpeed = Math.Min(minSpeed, wing.Plants[k].Speed);
                    maxSpeed = Math.Max(maxSpeed, wing.Plants[k].Speed);
                }
            }
            Assert.Equal(3, phase);
            Assert.True(minSpeed > 1.1f * p.MinimumSpeed(1f), $"min speed {minSpeed:0} m/s");
            Assert.True(maxSpeed < p.MaxSpeed, $"max speed {maxSpeed:0} m/s");
            for (int k = 0; k < 3; k++)
                Assert.True(wing.SlotError(k) < FormationCatalog.Standard, $"member {k + 1} ended {wing.SlotError(k):0} m off");
        }
    }
}
