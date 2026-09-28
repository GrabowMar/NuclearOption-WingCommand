using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Taxi and departure scenarios (spec M3 §9): T1 a 4-ship departure, T3 a blocked taxiway.</summary>
    public class GroundScenarioTests
    {
        private const float Dt = 1f / 30f;

        /// <summary>Runway 0 along +Z (0..2400 m, 60 m wide) with its entry points; a parallel taxiway at x = −150 joined
        /// to the runway ends; four hangars at x = −300 (z = 500..800, facing +X) on apron roads to the taxiway.
        /// <paramref name="innerTaxiway"/> adds a longer second way from the apron to the south connector.</summary>
        internal static AirbaseSample Field(bool innerTaxiway = false)
        {
            var roads = new List<Vec3[]>
            {
                new[] { new Vec3(-150f, 0f, 0f), new Vec3(-150f, 0f, 500f) },
                new[] { new Vec3(-150f, 0f, 500f), new Vec3(-150f, 0f, 600f) },
                new[] { new Vec3(-150f, 0f, 600f), new Vec3(-150f, 0f, 700f) },
                new[] { new Vec3(-150f, 0f, 700f), new Vec3(-150f, 0f, 800f) },
                new[] { new Vec3(-150f, 0f, 800f), new Vec3(-150f, 0f, 2400f) },
                new[] { new Vec3(-150f, 0f, 2400f), new Vec3(-40f, 0f, 2400f) },
            };
            if (innerTaxiway)
            {
                roads.Add(new[] { new Vec3(-150f, 0f, 0f), new Vec3(-100f, 0f, 0f) });
                roads.Add(new[] { new Vec3(-100f, 0f, 0f), new Vec3(-40f, 0f, 0f) });
                roads.Add(new[] { new Vec3(-150f, 0f, 800f), new Vec3(-100f, 0f, 900f), new Vec3(-100f, 0f, 0f) });
            }
            else roads.Add(new[] { new Vec3(-150f, 0f, 0f), new Vec3(-40f, 0f, 0f) });
            var hangars = new HangarSample[4];
            for (int i = 0; i < 4; i++)
            {
                float z = 500f + 100f * i;
                roads.Add(new[] { new Vec3(-260f, 0f, z), new Vec3(-150f, 0f, z) });
                hangars[i] = new HangarSample { Index = i, Spawn = new Pose(new Vec3(-300f, 0f, z), new Vec3(1f, 0f, 0f)), Available = true };
            }
            return new AirbaseSample
            {
                Name = "sim_field", Center = new Vec3(-150f, 0f, 1200f), Radius = 3000f,
                Runways = new[]
                {
                    new RunwaySample
                    {
                        Index = 0, Start = Vec3.Zero, End = new Vec3(0f, 0f, 2400f), Width = 60f, Length = 2400f, Reversable = true,
                        Takeoff = true, Landing = true,
                        Entries = new[]
                        {
                            new Pose(new Vec3(-40f, 0f, 0f), new Vec3(1f, 0f, 0f)),
                            new Pose(new Vec3(-40f, 0f, 2400f), new Vec3(1f, 0f, 0f)),
                        },
                    },
                },
                Roads = roads.ToArray(),
                Hangars = hangars,
            };
        }

        internal static AirframeProfile Jet()
        {
            AirframeProfile p = SimProfiles.GenericFighter();
            p.TakeoffSpeed = 1.3f * p.StallSpeed;
            p.WheelbaseM = 6.6f;
            p.SteerLockDeg = 45f;
            p.SpanM = 11f;
            return p;
        }

        private sealed class Member
        {
            public GroundPilot Pilot;
            public DepartingPlant Plant;
            public IFlightPipeline Pipeline;
            public Vec3 LinedUpAt;
            public float DoneAt = float.NaN;
        }

        private static List<Member> Launch(FieldTraffic field, AirframeProfile p, int count) => Launch(field, p, count, p.TakeoffSpeed);

        private static List<Member> Launch(FieldTraffic field, AirframeProfile p, int count, float liftSpeed)
        {
            var members = new List<Member>();
            for (int i = 0; i < count; i++)
            {
                Pose spawn = field.Field.Hangars[i].Spawn;
                field.Departures.Expect(i, LineupPlanner.Abreast(field.Runway.Width, p.SpanM));
                members.Add(new Member
                {
                    Pilot = new GroundPilot(i, field, AirframeClass.FixedWing, spawn, i),
                    Plant = new DepartingPlant(PlantParams.GenericFighter, spawn, liftSpeed, p.WheelbaseM, p.SteerLockDeg),
                    Pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing),
                });
            }
            return members;
        }

        private static void Step(FieldTraffic field, List<Member> members, AirframeProfile p, float t, WingEventRing events)
        {
            field.Step(Dt);
            for (int k = 0; k < members.Count; k++)
            {
                Member m = members[k];
                if (m.Pilot.Done) continue;
                AircraftState s = m.Plant.Read(Dt);
                GroundPhase before = m.Pilot.Phase;
                ControlOutput o = m.Pilot.Step(s, p, m.Pipeline, t, Dt, events, k);
                m.Plant.Step(o, Dt);
                if (m.Pilot.TakeRelocation(out Pose to)) m.Plant.Teleport(to);
                if (before == GroundPhase.LineUp && m.Pilot.Phase == GroundPhase.Roll) m.LinedUpAt = m.Plant.Position;
                if (m.Pilot.Done) m.DoneAt = t;
            }
        }

        [Fact]
        public void FourShipDepartsTwoAbreastWithoutConflictsOrRelocations()
        {
            // T1.
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = Jet();
            List<Member> members = Launch(field, p, 4);
            var events = new WingEventRing();
            float minGroundSeparation = float.MaxValue;
            for (int i = 0; i < 400 * 30; i++)
            {
                Step(field, members, p, i * Dt, events);
                for (int a = 0; a < members.Count; a++)
                    for (int b = a + 1; b < members.Count; b++)
                        if (!members[a].Plant.Airborne && !members[b].Plant.Airborne)
                            minGroundSeparation = Math.Min(minGroundSeparation,
                                (members[a].Plant.Position - members[b].Plant.Position).Horizontal.Length);
            }
            for (int k = 0; k < members.Count; k++) Assert.False(float.IsNaN(members[k].DoneAt), $"member {k} still {members[k].Pilot.Phase}");
            Assert.True(minGroundSeparation > 20f, $"members came within {minGroundSeparation:0} m of each other on the ground");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
            // Two abreast: the first two share a row, 30 m apart across the runway.
            Vec3 a0 = members[0].LinedUpAt, a1 = members[1].LinedUpAt;
            Assert.True(Math.Abs(a0.Z - a1.Z) < 5f && Math.Abs(Math.Abs(a0.X - a1.X) - 30f) < 5f, $"row 1 at {a0} and {a1}");
            float last = 0f;
            foreach (Member m in members) last = Math.Max(last, m.DoneAt);
            // Spec M3 §9 T1: the 4-ship is off the field within 180 s.
            Assert.True(last < 180f, $"the last member finished its climb-out at {last:0} s");
        }

        /// <summary>Night-1 sim runs (2026-09-28): the game's jets leave the runway below the loaded minimum speed their profile
        /// reckons (they rotate at 0.7 × take-off speed and fly, the profile's stall estimate is conservative); the climb-out's speed
        /// priority then pushed the nose down and #3 touched the runway again at 105 m/s, and the members, all aiming at one point over
        /// the centreline, passed 13 m apart. Here the controller's profile believes the stall 35% higher than the plant's own, as in
        /// the game: the four-ship must keep climbing and keep its distance.</summary>
        [Fact]
        public void AFourShipLiftingOffBelowItsMinimumSpeedClimbsOutWithoutSinkingBackOrConverging()
        {
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = Jet();
            float plantLift = p.TakeoffSpeed;
            p.StallSpeed *= 1.35f;
            List<Member> members = Launch(field, p, 4, plantLift);
            var events = new WingEventRing();
            var airborneAt = new float[members.Count];
            for (int k = 0; k < airborneAt.Length; k++) airborneAt[k] = float.NaN;
            float lowest = float.MaxValue, closest = float.MaxValue;
            string lowWho = null, closeWho = null;
            for (int i = 0; i < 400 * 30 && members.Exists(m => !m.Pilot.Done); i++)
            {
                float t = i * Dt;
                Step(field, members, p, t, events);
                for (int k = 0; k < members.Count; k++)
                {
                    Member m = members[k];
                    if (!m.Plant.Airborne) continue;
                    if (float.IsNaN(airborneAt[k])) airborneAt[k] = t;
                    if (!m.Pilot.Done && t - airborneAt[k] > 2f && m.Plant.Position.Y < lowest)
                    {
                        lowest = m.Plant.Position.Y;
                        lowWho = $"member {k} at {t - airborneAt[k]:0.0} s after lift-off";
                    }
                    for (int j = k + 1; j < members.Count; j++)
                    {
                        if (!members[j].Plant.Airborne || m.Pilot.Done || members[j].Pilot.Done) continue;
                        float d = (m.Plant.Position - members[j].Plant.Position).Length;
                        if (d < closest)
                        {
                            closest = d;
                            closeWho = $"members {k} and {j} at t {t:0.0}";
                        }
                    }
                }
            }
            for (int k = 0; k < members.Count; k++) Assert.True(members[k].Pilot.Done, $"member {k} still {members[k].Pilot.Phase}");
            Assert.True(lowest >= 3f, $"sank back to {lowest:0.0} m ({lowWho})");
            Assert.True(closest >= 25f, $"came within {closest:0.0} m in the climb-out ({closeWho})");
        }

        /// <summary>Night-1 sim runs (2026-09-28): a UH-90 spawned in a Boscali North hangar whose roof the upward ray missed climbed
        /// straight into it and hovered there, pinned, for five minutes. A helicopter launched from a hangar hovers out of it first,
        /// whatever the ray says.</summary>
        [Fact]
        public void AHelicopterLaunchedFromAHangarLeavesItBeforeClimbing()
        {
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = SimProfiles.Utility();
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(0, field, AirframeClass.Rotary, spawn, 0) { RoofOverhead = false };
            var plant = new RotaryPlant(RotaryParams.Utility, spawn.Pos, Vec3.Zero, Vec3.HeadingDeg(spawn.Fwd)) { GroundY = spawn.Pos.Y };
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            pipe.Track(plant.Read(Dt), new ControlOutput { Throttle = plant.Collective }, p);
            var events = new WingEventRing();
            float highestInside = 0f;
            for (float t = 0f; t < 90f && !pilot.Done; t += Dt)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), p, pipe, t, Dt, events, 0), Dt);
                float out_ = Vec3.Dot(plant.Position - spawn.Pos, spawn.Fwd.Horizontal.Normalized);
                if (out_ < TaxiGraph.HangarExitDistance - 2f) highestInside = Math.Max(highestInside, plant.Position.Y - spawn.Pos.Y);
            }
            Assert.True(pilot.Done, $"still {pilot.Phase}");
            Assert.True(highestInside <= GroundPilot.HoverExitHeight + 2f, $"climbed to {highestInside:0.0} m before leaving the hangar");
        }

        /// <summary>A helicopter that cannot get up (a roof, a wall) is moved out of the hangar once rather than hovering there for
        /// the rest of the mission.</summary>
        [Fact]
        public void AHelicopterPinnedOnItsLiftOffIsMovedOutOnce()
        {
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = SimProfiles.Utility();
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(0, field, AirframeClass.Rotary, spawn, 0) { RoofOverhead = true };
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            var events = new WingEventRing();
            var stuck = new AircraftState
            {
                Pos = spawn.Pos + Vec3.Up * 5f, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd), RadarAlt = 5f, RotorRpm = 1f,
                Dt = Dt,
            };
            int moves = 0;
            Pose to = default;
            for (float t = 0f; t < 120f; t += Dt)
            {
                field.Step(Dt);
                pilot.Step(stuck, p, pipe, t, Dt, events, 0);
                if (pilot.TakeRelocation(out Pose pose))
                {
                    moves++;
                    to = pose;
                }
            }
            Assert.Equal(1, moves);
            Assert.True(Vec3.Dot(to.Pos - spawn.Pos, spawn.Fwd.Horizontal.Normalized) >= TaxiGraph.HangarExitDistance - 1f, $"moved to {to.Pos}");
        }

        [Fact]
        public void AHelicopterStillClimbingOutOfItsHangarIsNeverMoved()
        {
            // Night-2 sim (UH-90 at Boscali North): two helicopters slid out of their hangars for 30 s, left them and were climbing
            // through 4-9 m when the 40 s timer moved them to the exit on the ground — both were destroyed. Only a lift-off that has
            // stopped making progress (no metre gained toward the exit or upward for the whole time) is moved.
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = SimProfiles.Utility();
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(0, field, AirframeClass.Rotary, spawn, 0) { RoofOverhead = true };
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            var events = new WingEventRing();
            Vec3 fwd = spawn.Fwd.Horizontal.Normalized;
            int moves = 0;
            for (float t = 0f; t < 90f; t += Dt)
            {
                // Slides out at 0.4 m/s for 40 s (on the ground), then climbs at 0.5 m/s: slow, but never stuck.
                float out1 = Math.Min(t, 40f) * 0.4f, up = Math.Max(0f, t - 40f) * 0.5f;
                var s = new AircraftState
                {
                    Pos = spawn.Pos + fwd * out1 + Vec3.Up * up, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd),
                    RadarAlt = up, RotorRpm = 1f, Dt = Dt,
                };
                field.Step(Dt);
                pilot.Step(s, p, pipe, t, Dt, events, 0);
                if (pilot.TakeRelocation(out _)) moves++;
            }
            Assert.Equal(0, moves);
        }

        [Fact]
        public void AnAirborneLiftOffThatStopsClimbingHandsOverAndIsNeverMoved()
        {
            // Night-2 sim (SAH-46, mountain field): out of their hangars and hovering at 6-15 m, three helicopters gained no metre for
            // 40 s and the watchdog moved them — a teleport from the air onto the ground, and all three were lost. One that is off the
            // ground and clear of its hangar hands over to flight (the climb goes on with the flight's terrain floor) instead.
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = SimProfiles.Utility();
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(0, field, AirframeClass.Rotary, spawn, -1);
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            var events = new WingEventRing();
            int moves = 0;
            for (float t = 0f; t < 90f && pilot.Phase != GroundPhase.Done; t += Dt)
            {
                // Up to 12 m in the first 10 s, then hanging there.
                float up = Math.Min(12f, t * 1.2f);
                var s = new AircraftState
                {
                    Pos = spawn.Pos + Vec3.Up * up, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd),
                    RadarAlt = up, RotorRpm = 1f, Dt = Dt,
                };
                field.Step(Dt);
                pilot.Step(s, p, pipe, t, Dt, events, 0);
                if (pilot.TakeRelocation(out _)) moves++;
            }
            Assert.Equal(0, moves);
            Assert.Equal(GroundPhase.Done, pilot.Phase);
        }

        [Fact]
        public void AVtolStandingTallAboveItsHangarFloorAsksToClimbNotToDescend()
        {
            // Night-2 sim (VL-49 at Boscali North): the spawn pose is the hangar floor, but the aircraft's root stands its spawn
            // offset (5.3 m on its tall gear) above it. The hover-out height was measured from the floor, so 3 m up was 2.3 m below
            // the root: the lift-off asked to descend, the collective sank to its floor (0.24) and two VL-49s sat until moved.
            var field = new FieldTraffic(Field(), 0, false);
            AirframeProfile p = SimProfiles.Utility();
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(0, field, AirframeClass.Rotary, spawn, 0) { RoofOverhead = true };
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            var events = new WingEventRing();
            const float offset = 5.3f;
            var s = new AircraftState
            {
                Pos = spawn.Pos + Vec3.Up * offset, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd),
                RadarAlt = 0f, RotorRpm = 1f, Dt = Dt,
            };
            ControlOutput o = default;
            for (float t = 0f; t < 12f; t += Dt)
            {
                field.Step(Dt);
                o = pilot.Step(s, p, pipe, t, Dt, events, 0);
            }
            Assert.Equal(GroundPhase.LiftOff, pilot.Phase);
            Assert.True(o.Throttle > p.HoverCollective, $"collective {o.Throttle:0.00} with hover at {p.HoverCollective:0.00}");
        }

        [Fact]
        public void ABlockedTaxiwayIsReroutedAroundWithoutARelocation()
        {
            // T3: a wreck appears on the parallel taxiway south of the apron while the wing taxis.
            var field = new FieldTraffic(Field(innerTaxiway: true), 0, false);
            AirframeProfile p = Jet();
            List<Member> members = Launch(field, p, 2);
            var events = new WingEventRing();
            int blocked = -1;
            for (int e = 0; e < field.Graph.EdgeCount; e++)
            {
                Vec3 a = field.Graph.NodePos(field.Graph.EdgeFrom(e)), c = field.Graph.NodePos(field.Graph.EdgeTo(e));
                if (Math.Abs(a.X + 150f) < 1f && Math.Abs(c.X + 150f) < 1f && Math.Min(a.Z, c.Z) < 1f && Math.Max(a.Z, c.Z) > 499f) blocked = e;
            }
            Assert.True(blocked >= 0);
            const float blockedAt = 15f;
            float reroutedAt = float.NaN;
            for (int i = 0; i < 400 * 30; i++)
            {
                float t = i * Dt;
                if (Math.Abs(t - blockedAt) < Dt / 2f) field.Reservations.Block(blocked, true);
                Step(field, members, p, t, events);
                if (float.IsNaN(reroutedAt) && events.CountOf(WingEventKind.Rerouted) > 0) reroutedAt = t;
            }
            Assert.True(reroutedAt - blockedAt < 25f, $"rerouted {reroutedAt - blockedAt:0} s after the block");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
            for (int k = 0; k < members.Count; k++) Assert.True(members[k].Pilot.Done, $"member {k} still {members[k].Pilot.Phase}");
        }
    }
}
