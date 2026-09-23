using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>A kinematic taxiing aircraft: bicycle model (nose wheel = yaw × steering lock), 3 m/s² per unit throttle,
    /// 4 m/s² per unit brake, a little rolling drag; it stays on the ground.</summary>
    internal sealed class TestGroundPlant
    {
        public Vec3 Pos, Fwd;
        public float Speed;
        public float WheelbaseM = 6.6f, SteerLockDeg = 45f;

        public TestGroundPlant(Pose at)
        {
            Pos = at.Pos;
            Fwd = at.Fwd.Horizontal.Normalized;
        }

        public AircraftState Read(float dt) => new AircraftState
        {
            Pos = Pos, Vel = Fwd * Speed, Fwd = Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, Fwd), Tas = Speed,
            RadarAlt = Pos.Y, Dt = dt,
        };

        public void Step(in ControlOutput o, float dt)
        {
            Speed = Math.Max(0f, Speed + (3f * o.Throttle - 4f * o.Brake - 0.05f) * dt);
            float delta = Scalar.Clamp(o.Yaw, -1f, 1f) * SteerLockDeg * Scalar.Deg2Rad;
            float turn = Speed * (float)Math.Tan(delta) / WheelbaseM * dt;
            Vec3 right = Vec3.Cross(Vec3.Up, Fwd);
            Fwd = (Fwd * (float)Math.Cos(turn) + right * (float)Math.Sin(turn)).Normalized;
            Pos += Fwd * Speed * dt;
        }
    }

    public class GroundPilotTests
    {
        private const float Dt = 1f / 30f;

        private static AirframeProfile Jet() => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 216f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = 11f,
            TakeoffSpeed = 70f,
        });

        [Fact]
        public void AJetTaxisLinesUpAndRollsInOrder()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            AirbaseSample sample = field.Field;
            Pose spawn = sample.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1);
            AirframeProfile p = Jet();
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            var phases = new List<GroundPhase> { pilot.Phase };
            Vec3 linedUpAt = Vec3.Zero;
            for (int i = 0; i < 600 * 30 && pilot.Phase != GroundPhase.ClimbOut; i++)
            {
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                ControlOutput o = pilot.Step(s, p, pipeline, i * Dt, Dt, events, 0);
                plant.Step(o, Dt);
                if (phases[phases.Count - 1] != pilot.Phase)
                {
                    phases.Add(pilot.Phase);
                    if (pilot.Phase == GroundPhase.Roll) linedUpAt = plant.Pos;
                }
            }
            Assert.Equal(new[] { GroundPhase.Parked, GroundPhase.TaxiOut, GroundPhase.HoldShort, GroundPhase.LineUp, GroundPhase.Roll, GroundPhase.ClimbOut },
                phases.ToArray());
            Vec3 slot = LineupPlanner.Slot(sample.Runways[0], false, 0, 0, 1, 1);
            Assert.True((linedUpAt - slot).Horizontal.Length < 5f, $"lined up at {linedUpAt}, slot {slot}");
            Assert.True(plant.Speed >= p.TakeoffSpeed - 1f);
            Assert.Equal(1, events.CountOf(WingEventKind.Rolling));
        }

        [Fact]
        public void ADepartingMemberStopsShortOfAnObstacleOnItsPath()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            // A native aircraft parked on the parallel taxiway, 250 m down from the apron junction.
            var obstacle = new Vec3(-150f, 0f, 250f);
            field.Obstacles.Add(obstacle);
            AirframeProfile p = Jet();
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            float closest = float.MaxValue;
            bool relocated = false;
            for (int i = 0; i < 150 * 30; i++)
            {
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                plant.Step(pilot.Step(s, p, pipeline, i * Dt, Dt, null, 0), Dt);
                if (pilot.TakeRelocation(out Pose to))
                {
                    // What the engine does: the aircraft is moved past the blockage to the hold-short.
                    plant.Pos = to.Pos;
                    plant.Fwd = to.Fwd;
                    plant.Speed = 0f;
                    relocated = true;
                }
                closest = Math.Min(closest, (plant.Pos - obstacle).Horizontal.Length);
            }
            Assert.True(closest > GroundPilot.ObstacleGap - 10f, $"came within {closest:0} m of the obstacle");
            Assert.True(relocated, "the only way to the runway is blocked: after 60 s it is relocated");
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"after the relocation it carries on: {pilot.Phase}");
        }

        [Fact]
        public void ABlockedEdgeAheadIsReroutedAroundAtOnce()
        {
            // The simple field plus a longer second taxiway at x = −100 (a detour via z = 600) from the apron junction
            // (z = 500) to the south connector: the outer taxiway is the first choice.
            AirbaseSample sample = TestFields.Simple();
            var roads = new List<Vec3[]>(sample.Roads)
            {
                new[] { new Vec3(-150f, 0f, 500f), new Vec3(-100f, 0f, 600f), new Vec3(-100f, 0f, 0f) },
            };
            // Split the south connector at x = −100 so both taxiways join it at a node.
            roads.RemoveAll(r => r[0].Z == 0f && r[1].Z == 0f);
            roads.Add(new[] { new Vec3(-150f, 0f, 0f), new Vec3(-100f, 0f, 0f) });
            roads.Add(new[] { new Vec3(-100f, 0f, 0f), new Vec3(-40f, 0f, 0f) });
            sample.Roads = roads.ToArray();
            var field = new FieldTraffic(sample, 0, false);
            Pose spawn = sample.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            var events = new WingEventRing();
            int outer = -1;
            for (int e = 0; e < field.Graph.EdgeCount; e++)
            {
                Vec3 a = field.Graph.NodePos(field.Graph.EdgeFrom(e)), c = field.Graph.NodePos(field.Graph.EdgeTo(e));
                if (Math.Abs(a.X + 150f) < 1f && Math.Abs(c.X + 150f) < 1f && Math.Min(a.Z, c.Z) < 1f && Math.Max(a.Z, c.Z) > 499f) outer = e;
            }
            Assert.True(outer >= 0);
            float minX = 0f;
            for (int i = 0; i < 120 * 30; i++)
            {
                if (i == 12 * 30) field.Reservations.Block(outer, true);   // a wreck appears on the outer taxiway
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                plant.Step(pilot.Step(s, Jet(), FlightStack.NewPipeline(AirframeClass.FixedWing), i * Dt, Dt, events, 0), Dt);
                if (plant.Pos.Z < 450f && plant.Pos.Z > 50f) minX = Math.Min(minX, plant.Pos.X);
            }
            Assert.True(events.CountOf(WingEventKind.Rerouted) == 1, $"rerouted {events.CountOf(WingEventKind.Rerouted)} times, relocated {events.CountOf(WingEventKind.Relocated)}, phase {pilot.Phase}, minX {minX:0}, at {plant.Pos}");
            Assert.True(minX > -120f, $"went down the blocked taxiway (x {minX:0})");
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
        }

        [Fact]
        public void ARelocationPutsTheMemberAtTheHoldShortOnce()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            // Stuck in the hangar (no progress at all): reroute at 20 s, relocation at 60 s.
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd) };
            int relocations = 0;
            Pose to = default;
            for (int i = 0; i < 150 * 30; i++)
            {
                field.Step(Dt);
                pilot.Step(s, Jet(), FlightStack.NewPipeline(AirframeClass.FixedWing), i * Dt, Dt, null, 0);
                if (pilot.TakeRelocation(out Pose pose))
                {
                    relocations++;
                    to = pose;
                }
            }
            Assert.Equal(1, relocations);
            Assert.True((to.Pos - field.Graph.NodePos(field.Graph.HoldShort(0, false))).Length < 1f);
        }

        [Fact]
        public void LeavingTheFieldFreesEveryClaim()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(7, field, AirframeClass.FixedWing, spawn, 0);
            for (int i = 0; i < 10 * 30; i++)
            {
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                plant.Step(pilot.Step(s, Jet(), FlightStack.NewPipeline(AirframeClass.FixedWing), i * Dt, Dt, null, 0), Dt);
            }
            pilot.Leave();
            for (int n = 0; n < field.Graph.NodeCount; n++) Assert.NotEqual(7, field.Reservations.OwnerOfNode(n));
            for (int e = 0; e < field.Graph.EdgeCount; e++) Assert.NotEqual(7, field.Reservations.OwnerOfEdge(e));
        }

        [Fact]
        public void AHelicopterLiftsOffInPlaceAndIsDoneAboveTheLiftOffHeight()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var pilot = new GroundPilot(3, field, AirframeClass.Rotary, spawn, 1);
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 134f });
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.Rotary);
            var phases = new List<GroundPhase> { pilot.Phase };
            for (int i = 0; i < 60 * 30 && !pilot.Done; i++)
            {
                // Scripted climb once it lifts off: 2 m/s straight up.
                float height = pilot.Phase == GroundPhase.LiftOff ? Math.Min(40f, (i * Dt - GroundPilot.ParkedSeconds) * 2f) : 0f;
                var s = new AircraftState
                {
                    Pos = spawn.Pos + Vec3.Up * Math.Max(0f, height), Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd),
                    RadarAlt = Math.Max(0f, height), RotorRpm = 1f,
                };
                ControlOutput o = pilot.Step(s, helo, pipeline, i * Dt, Dt, null, 0);
                if (phases[phases.Count - 1] != pilot.Phase) phases.Add(pilot.Phase);
            }
            Assert.Equal(new[] { GroundPhase.Parked, GroundPhase.LiftOff, GroundPhase.Done }, phases.ToArray());
        }
    }
}
