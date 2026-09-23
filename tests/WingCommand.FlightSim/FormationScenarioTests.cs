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
        public void CloseFingerFourJoinsFromAnAirStart()
        {
            // The in-game air-start (2026-09-23 harness run): 2 km behind, 150 m low, one lateral step per slot, a
            // straight leader at 200 m/s, Close spacing. #4's lane (3 × 34 m) was deeper than two spacings, and it
            // never left the lane under its pre-slot.
            var leader = new VirtualLeader(new Vec3(0f, 2500f, 2000f), 200f, 0f);
            FormationDefinition shape = SimFormations.Get("finger-four-right");
            var wing = new SimWing(leader, shape, FormationCatalog.Close,
                new[] { new Vec3(-120f, 2350f, 0f), new Vec3(120f, 2350f, 0f), new Vec3(240f, 2350f, 0f) }, 200f, 0f);
            float[] captured = RunUntilCaptured(wing, t => (0f, 200f), 150f, out float minSeparation);
            for (int k = 0; k < 3; k++) Assert.True(captured[k] < 120f, $"member {k + 1} captured at {captured[k]:0} s");
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

        private static int HoldEntries(WingEventRing events, int member)
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++)
            {
                WingEvent e = events[i];
                if (e.Member == member && e.Kind == WingEventKind.BehaviourChanged && e.To == BehaviourId.HoldOverhead) n++;
            }
            return n;
        }

        [Fact]
        public void LeaderDashCausesOneFallingBehindPerMemberThenARejoin()
        {
            // A1: the leader dashes at 300 m/s for a minute; wingmen may not use afterburner (255 m/s usable).
            var leader = new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);
            foreach (FormationPilot pilot in wing.Pilots) pilot.AfterburnerAllowed = false;
            for (int i = 0; i < 300 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt, t > 5f && t < 65f ? 300f : 200f, 0f);
                wing.Step();
            }
            for (int k = 0; k < 3; k++)
            {
                Assert.Equal(1, wing.Events.CountOf(WingEventKind.FallingBehind, k));
                Assert.Equal(1, wing.Events.CountOf(WingEventKind.FallingBehindCleared, k));
                Assert.Equal(0, HoldEntries(wing.Events, k));
                Assert.Equal(BehaviourId.StationKeep, wing.Pilots[k].Mind.Current);
            }
        }

        [Fact]
        public void LowLevelValleyWithTurnsKeepsClearanceAndAtMostOneGcasPerMember()
        {
            // L1: the leader flies 100 m above rolling terrain with ±30° turns every 15 s; clearance 60 m.
            Func<float, float, float> terrain = (x, z) => 200f + 100f * (float)Math.Sin(z / 2000f) * (float)Math.Cos(x / 3000f);
            var leader = new VirtualLeader(new Vec3(0f, terrain(0f, 0f) + 100f, 0f), 180f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);
            wing.Terrain = terrain;
            float minClearance = float.MaxValue;
            for (int i = 0; i < 120 * 60; i++)
            {
                float t = i * Dt;
                Vec3 ahead = leader.Position + leader.Velocity * 3f;
                float target = Math.Max(terrain(leader.Position.X, leader.Position.Z), terrain(ahead.X, ahead.Z)) + 100f;
                float gamma = Scalar.Clamp((float)Math.Atan2(target - leader.Position.Y, 3f * leader.Speed) * Scalar.Rad2Deg, -10f, 10f);
                leader.Step((int)(t / 15f) % 2 == 0 ? 30f : -30f, Dt, 180f, gamma);
                wing.Step();
                for (int k = 0; k < 3; k++) minClearance = Math.Min(minClearance, wing.ClearanceOf(k));
            }
            Assert.True(minClearance > 0f, $"minimum clearance {minClearance:0} m");
            for (int k = 0; k < 3; k++)
            {
                int events = wing.Events.CountOf(WingEventKind.GcasActivated, k);
                Assert.True(events <= 1, $"member {k + 1}: {events} GCAS events");
            }
        }

        [Fact]
        public void CrossingRejoinsKeepSeparationWithABriefBias()
        {
            // C1: #2 (left slot) starts on the right and #3 (right slot) on the left, so their rejoins cross.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 3000f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(300f, 1850f, 0f), new Vec3(-300f, 1850f, 0f), new Vec3(600f, 1850f, -300f) }, 180f, 0f);
            var biasTime = new float[3];
            var lastError = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var captured = new bool[3];
            float minSeparation = float.MaxValue;
            for (int i = 0; i < 180 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                {
                    if (wing.Wing.Frame.Bias[k].Length > 0.05f * Scalar.G) biasTime[k] += Dt;
                    if (wing.Pilots[k].Mind.Current == BehaviourId.StationKeep) captured[k] = true;
                    if (i % (5 * 60) != 0 || captured[k]) continue;
                    float error = wing.SlotError(k);
                    Assert.True(error <= lastError[k] * 1.05f + 5f, $"member {k + 1}: slot error grew to {error:0} m at {wing.Time:0} s");
                    lastError[k] = error;
                }
            }
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
            for (int k = 0; k < 3; k++)
            {
                Assert.True(biasTime[k] < 5f, $"member {k + 1}: bias active {biasTime[k]:0.0} s");
                Assert.True(captured[k], $"member {k + 1} never captured");
            }
        }

        /// <summary>Runs the wing until every member has held its slot within 0.25 spacing for 5 s; returns the
        /// capture times (NaN = never) and the smallest separation seen.</summary>
        private static float[] RunUntilCaptured(SimWing wing, Func<float, (float bank, float speed)> schedule, float seconds,
            out float minSeparation)
        {
            int n = wing.Plants.Length;
            var captured = new float[n];
            var inside = new float[n];
            for (int k = 0; k < n; k++) captured[k] = float.NaN;
            minSeparation = float.MaxValue;
            float spacing = wing.Wing.Frame.Spacing;
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                (float bank, float speed) = schedule(i * Dt);
                wing.Leader.Step(bank, Dt, speed, 0f);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < n; k++)
                {
                    inside[k] = wing.SlotError(k) < 0.25f * spacing ? inside[k] + Dt : 0f;
                    if (float.IsNaN(captured[k]) && inside[k] >= 5f) captured[k] = wing.Time;
                }
            }
            return captured;
        }

        [Fact]
        public void FourShipJoinsBehindALeaderInASteadyTurn()
        {
            // J1 with the leader in a steady 60° turn: the rejoin must cut inside the turn, not chase it round.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 4000f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-150f, 1850f, 0f), new Vec3(150f, 1850f, 0f), new Vec3(300f, 1850f, -150f) }, 140f, 0f);
            float[] captured = RunUntilCaptured(wing, t => (60f, 200f), 200f, out float minSeparation);
            for (int k = 0; k < 3; k++) Assert.True(captured[k] < 150f, $"member {k + 1} captured at {captured[k]:0} s");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void OppositeHeadingJoinBehindAnSTurningLeader()
        {
            // J2 with the leader weaving ±30° every 8 s.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(-200f, 2000f, 6000f), new Vec3(200f, 2000f, 6000f), new Vec3(400f, 2000f, 6200f) }, 200f, 180f);
            float[] captured = RunUntilCaptured(wing, t => ((int)(t / 8f) % 2 == 0 ? 30f : -30f, 200f), 300f, out float minSeparation);
            for (int k = 0; k < 3; k++) Assert.False(float.IsNaN(captured[k]), $"member {k + 1} never captured");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void HoldExitRejoinsBehindATurningLeader()
        {
            // The leader slows below the members' hold threshold (they hold overhead), then accelerates away in a
            // 60° turn: the rejoin from the hold must converge.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 3);
            float[] captured = RunUntilCaptured(wing, t => t < 60f ? (0f, 80f) : (60f, 200f), 60f, out _);
            for (int k = 0; k < 3; k++) Assert.Equal(BehaviourId.HoldOverhead, wing.Pilots[k].Mind.Current);
            captured = RunUntilCaptured(wing, t => (60f, 200f), 200f, out float minSeparation);
            for (int k = 0; k < 3; k++) Assert.False(float.IsNaN(captured[k]), $"member {k + 1} never captured after the hold");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }

        [Fact]
        public void AbeamJoinConvergesOnLanesWithoutConflicts()
        {
            // Members start 2 km abeam on the right at the leader's altitude, stacked outward: #2 sits inboard of
            // #3 and #4, so their paths to the finger four cross laterally and must do so on separate lanes.
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            var wing = new SimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard,
                new[] { new Vec3(2000f, 2000f, 0f), new Vec3(2200f, 2000f, 0f), new Vec3(2400f, 2000f, 0f) }, 200f, 0f);
            float[] captured = RunUntilCaptured(wing, t => (0f, 200f), 200f, out float minSeparation);
            for (int k = 0; k < 3; k++) Assert.False(float.IsNaN(captured[k]), $"member {k + 1} never captured");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
            for (int k = 0; k < 3; k++) Assert.Equal(0, wing.Events.CountOf(WingEventKind.CollisionEmergency, k));
        }

        [Fact]
        public void CombatSpreadCrossesOverCleanlyInASustainedTurn()
        {
            // 40 s of 45° bank turns the leader ~110° toward the spread's wide side: the shape mirrors once, then
            // 40 s level. No two aircraft may meet, and no member may fight the collision bias for long.
            var leader = new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("combat-spread"), FormationCatalog.Spread, 3);
            var biasTime = new float[3];
            float minSeparation = float.MaxValue;
            for (int i = 0; i < 80 * 60; i++)
            {
                leader.Step(i * Dt < 40f ? 45f : 0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                for (int k = 0; k < 3; k++)
                    if (wing.Wing.Frame.Bias[k].Length > 0.05f * Scalar.G) biasTime[k] += Dt;
            }
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
            for (int k = 0; k < 3; k++)
            {
                Assert.True(biasTime[k] < 5f, $"member {k + 1}: bias active {biasTime[k]:0.0} s");
                Assert.True(wing.SlotError(k) < 0.5f * FormationCatalog.Spread, $"member {k + 1} ended {wing.SlotError(k):0} m off");
            }
            Assert.True(wing.Wing.Frame.Slots[0].Lateral < 0f, "the shape did not mirror");
        }
    }
}
