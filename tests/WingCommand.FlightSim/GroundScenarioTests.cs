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

        private static List<Member> Launch(FieldTraffic field, AirframeProfile p, int count)
        {
            var members = new List<Member>();
            for (int i = 0; i < count; i++)
            {
                Pose spawn = field.Field.Hangars[i].Spawn;
                field.Departures.Expect(i);
                members.Add(new Member
                {
                    Pilot = new GroundPilot(i, field, AirframeClass.FixedWing, spawn, i),
                    Plant = new DepartingPlant(PlantParams.GenericFighter, spawn, p.TakeoffSpeed, p.WheelbaseM, p.SteerLockDeg),
                    Pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing),
                });
            }
            field.Departures.Abreast = LineupPlanner.Abreast(field.Runway.Width, p.SpanM);
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
            Assert.True(last < 300f, $"the last member finished its climb-out at {last:0} s");
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
