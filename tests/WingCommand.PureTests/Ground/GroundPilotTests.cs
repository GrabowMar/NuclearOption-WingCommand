using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>A kinematic taxiing aircraft: bicycle model (nose wheel = yaw × steering lock), 3 m/s² per unit throttle,
    /// 4 m/s² per unit brake, a little rolling drag. Pitch stick rotates it (8°/s per unit above 20 m/s, settling back
    /// without); it lifts off only at <see cref="LiftSpeed"/> with the nose 6° up, then climbs (the in-game jets stayed
    /// on their wheels when nobody rotated them).</summary>
    internal sealed class TestGroundPlant
    {
        public Vec3 Pos, Fwd;
        public float Speed, PitchDeg;
        public float WheelbaseM = 6.6f, SteerLockDeg = 45f, LiftSpeed = 70f;

        public TestGroundPlant(Pose at)
        {
            Pos = at.Pos;
            Fwd = at.Fwd.Horizontal.Normalized;
        }

        public AircraftState Read(float dt) => new AircraftState
        {
            Pos = Pos, Vel = Fwd * Speed, Fwd = Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, Fwd), Tas = Speed,
            RadarAlt = Pos.Y, Dt = dt, PitchDeg = PitchDeg,
        };

        public void Step(in ControlOutput o, float dt)
        {
            Speed = Math.Max(0f, Speed + (3f * o.Throttle - 4f * o.Brake - 0.05f) * dt);
            float delta = Scalar.Clamp(o.Yaw, -1f, 1f) * SteerLockDeg * Scalar.Deg2Rad;
            float turn = Speed * (float)Math.Tan(delta) / WheelbaseM * dt;
            Vec3 right = Vec3.Cross(Vec3.Up, Fwd);
            Fwd = (Fwd * (float)Math.Cos(turn) + right * (float)Math.Sin(turn)).Normalized;
            Pos += Fwd * Speed * dt;
            PitchDeg = Scalar.Clamp(Speed > 20f && o.Pitch > 0f ? PitchDeg + o.Pitch * 8f * dt : PitchDeg - 5f * dt, 0f, 15f);
            if (Speed >= LiftSpeed && PitchDeg >= 6f) Pos += Vec3.Up * (0.1f * Speed * dt);
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
            field.Departures.Expect(1, 1);
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
            Assert.False(pilot.DespawnOnRelease(plant.Read(Dt)), "a member climbing out goes to the game's AI when released");
        }

        [Fact]
        public void AReleaseDespawnsAMemberOnlyWhileItIsStillOnTheSurface()
        {
            // Review M3a #2: a member dismissed on the ground went to the native combat AI, which ejects a stopped pilot.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var pilot = new GroundPilot(3, field, AirframeClass.Rotary, spawn, 1);
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 134f });
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.Rotary);
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd), RotorRpm = 1f };
            Assert.True(pilot.DespawnOnRelease(s));
            for (int i = 0; i < 5 * 30 && pilot.Phase == GroundPhase.Parked; i++) pilot.Step(s, helo, pipeline, i * Dt, Dt, null, 0);
            Assert.Equal(GroundPhase.LiftOff, pilot.Phase);
            s.RadarAlt = 2f;
            Assert.True(pilot.DespawnOnRelease(s));
            s.RadarAlt = 25f;
            Assert.False(pilot.DespawnOnRelease(s));
        }

        [Fact]
        public void TheRollRotatesFromSeventyPercentOfTakeoffSpeedAndClimbsOutOnlyOffTheGround()
        {
            // In game the jets were handed to the flight pipeline at takeoff speed on their wheels, never rotated,
            // drifted off the runway and stopped in the grass (RTB never started).
            (FieldTraffic field, GroundPilot pilot, TestGroundPlant plant, IFlightPipeline pipeline, WingEventRing events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.Roll);
            AirframeProfile p = Jet();
            bool early = false, rotated = false;
            for (int i = 0; i < 60 * 30 && pilot.Phase == GroundPhase.Roll; i++, t += Dt)
            {
                field.Step(Dt);
                float speed = plant.Speed;
                ControlOutput o = pilot.Step(plant.Read(Dt), p, pipeline, t, Dt, events, 0);
                if (speed < 0.65f * p.TakeoffSpeed && o.Pitch > 0f) early = true;
                if (speed > 0.75f * p.TakeoffSpeed && o.Pitch > 0.1f) rotated = true;
                plant.Step(o, Dt);
            }
            Assert.False(early, "rotated below 0.7 × takeoff speed");
            Assert.True(rotated, "never rotated");
            Assert.Equal(GroundPhase.ClimbOut, pilot.Phase);
            Assert.True(plant.Pos.Y > 1f, $"climb-out began at {plant.Pos.Y:0.0} m");
        }

        [Fact]
        public void AJetThatNeverLeavesTheGroundAbortsInsteadOfClimbingOut()
        {
            (FieldTraffic field, GroundPilot pilot, TestGroundPlant plant, IFlightPipeline pipeline, WingEventRing events) = OneJet();
            plant.LiftSpeed = float.PositiveInfinity;
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.Roll);
            for (int i = 0; i < 90 * 30 && pilot.Phase == GroundPhase.Roll; i++, t += Dt)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Aborted, pilot.Phase);
            Assert.Equal(0, events.CountOf(WingEventKind.Airborne));
        }

        [Fact]
        public void ClimbOutHoldsFullThrottle()
        {
            (FieldTraffic field, GroundPilot pilot, TestGroundPlant plant, IFlightPipeline pipeline, WingEventRing events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.ClimbOut);
            for (int i = 0; i < 3 * 30 && pilot.Phase == GroundPhase.ClimbOut; i++, t += Dt)
            {
                field.Step(Dt);
                ControlOutput o = pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0);
                Assert.Equal(1f, o.Throttle);
                Assert.False(o.Airbrake);
                plant.Step(o, Dt);
            }
        }

        /// <summary>One jet from its hangar until it reaches <paramref name="until"/>; the time reached.</summary>
        private static float RunUntil(FieldTraffic field, GroundPilot pilot, TestGroundPlant plant, IFlightPipeline pipeline,
            WingEventRing events, GroundPhase until)
        {
            float t = 0f;
            for (int i = 0; i < 600 * 30 && pilot.Phase != until; i++, t += Dt)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0), Dt);
            }
            Assert.Equal(until, pilot.Phase);
            return t;
        }

        /// <summary>The plant held still (stuck): the pilot steps on, the aircraft never moves.</summary>
        private static float Hold(FieldTraffic field, GroundPilot pilot, TestGroundPlant plant, IFlightPipeline pipeline,
            WingEventRing events, float t, float seconds)
        {
            plant.Speed = 0f;
            for (int i = 0; i < seconds * 30; i++, t += Dt)
            {
                field.Step(Dt);
                pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0);
            }
            return t;
        }

        private static (FieldTraffic, GroundPilot, TestGroundPlant, IFlightPipeline, WingEventRing) OneJet()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            field.Departures.Expect(1, 1);
            return (field, new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0), new TestGroundPlant(spawn),
                FlightStack.NewPipeline(AirframeClass.FixedWing), new WingEventRing());
        }

        [Fact]
        public void AMemberStoppedAtItsSlotOffHeadingStillRollsOnceSettled()
        {
            // Review M3a #6: a member stopped on its slot more than 10 degrees off the runway heading waited forever.
            var (field, pilot, plant, pipeline, events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.LineUp);
            RunwaySample r = field.Runway;
            plant.Pos = LineupPlanner.Slot(r, false, 0, 0, 1, 1);
            plant.Fwd = new Vec3((float)Math.Sin(20f * Scalar.Deg2Rad), 0f, (float)Math.Cos(20f * Scalar.Deg2Rad));
            Hold(field, pilot, plant, pipeline, events, t, GroundPilot.AlignSettleSeconds + 1f);
            Assert.Equal(GroundPhase.Roll, pilot.Phase);
        }

        [Fact]
        public void AMemberThatCannotLineUpGivesUpAndFreesTheRunway()
        {
            // Review M3a #6: a member stuck in the lineup held the runway lock forever.
            var (field, pilot, plant, pipeline, events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.LineUp);
            Assert.True(field.Departures.RunwayLocked);
            Hold(field, pilot, plant, pipeline, events, t, GroundPilot.LineUpSeconds + 1f);
            Assert.Equal(GroundPhase.Aborted, pilot.Phase);
            Assert.False(field.Departures.RunwayLocked);
            Assert.True(pilot.DespawnOnRelease(plant.Read(Dt)), "a member that gave up goes back to the reserve");
            Assert.Equal(1, events.CountOf(WingEventKind.DepartureAborted));
        }

        [Fact]
        public void AMemberThatNeverReachesTakeoffSpeedGivesUpAndFreesTheRunway()
        {
            var (field, pilot, plant, pipeline, events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.Roll);
            t = Hold(field, pilot, plant, pipeline, events, t, GroundPilot.RollSeconds - 5f);
            Assert.Equal(GroundPhase.Roll, pilot.Phase);
            Hold(field, pilot, plant, pipeline, events, t, 6f);
            Assert.Equal(GroundPhase.Aborted, pilot.Phase);
            Assert.False(field.Departures.RunwayLocked);
        }

        [Fact]
        public void AFollowerKeepsItsGapThroughTheJunctionsBehindItsLeader()
        {
            // Review M3a #5: the gap was only kept on a shared edge; past a node the follower closed to the leader's tail.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            var pilots = new List<GroundPilot>();
            var plants = new List<TestGroundPlant>();
            var pipelines = new List<IFlightPipeline>();
            for (int k = 0; k < 2; k++)
            {
                Pose spawn = field.Field.Hangars[1 - k].Spawn;   // the member at z = 700 first, south past the other's junction
                field.Departures.Expect(k + 1, 1);
                pilots.Add(new GroundPilot(k + 1, field, AirframeClass.FixedWing, spawn, 1 - k));
                plants.Add(new TestGroundPlant(spawn));
                pipelines.Add(FlightStack.NewPipeline(AirframeClass.FixedWing));
            }
            float minGap = float.MaxValue;
            bool stopped = false;
            for (int i = 0; i < 150 * 30; i++)
            {
                field.Step(Dt);
                // The leader stops 25 m past the follower's junction (it waits for something ahead) and stays.
                stopped |= plants[0].Pos.X > -160f && plants[0].Pos.Z < 475f;
                if (stopped) plants[0].Speed = 0f;
                for (int k = 0; k < 2; k++)
                {
                    if (k == 1 && i * Dt < 20f) continue;   // the follower is launched once the leader is past its junction
                    ControlOutput o = pilots[k].Step(plants[k].Read(Dt), Jet(), pipelines[k], i * Dt, Dt, null, k);
                    if (k == 1 || !stopped) plants[k].Step(o, Dt);
                }
                // Measured once the leader waits ahead (its crossing of the follower's junction earlier is not following).
                if (stopped && pilots[1].Phase != GroundPhase.Parked) minGap = Math.Min(minGap, (plants[0].Pos - plants[1].Pos).Horizontal.Length);
            }
            Assert.True(stopped);
            // The gap is kept along the path: round the 90-degree corner at the junction that is ~0.7 of it in a line.
            Assert.True(minGap > 0.65f * GroundPilot.FollowGap, $"the follower closed to {minGap:0} m");
        }

        [Fact]
        public void TheFieldMarksItsRunwayBusyWhileAForeignAircraftStandsOnIt()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            field.Obstacles.Add(new Vec3(-60f, 0f, 800f));   // beside the runway
            field.Step(FieldTraffic.DeadlockPeriod);
            Assert.False(field.Departures.RunwayBusy);
            field.Obstacles.Add(new Vec3(10f, 0f, 800f));    // on it
            field.Step(Dt);
            Assert.True(field.Departures.RunwayBusy);
        }

        [Fact]
        public void ARollRejectsTheTakeoffForAnAircraftOnTheRunwayAhead()
        {
            // Review M3a #7: the roll had no obstacle check.
            var (field, pilot, plant, pipeline, events) = OneJet();
            float t = RunUntil(field, pilot, plant, pipeline, events, GroundPhase.Roll);
            for (int i = 0; i < 3 * 30; i++, t += Dt)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0), Dt);
            }
            Assert.True(plant.Speed > 3f, "rolling");
            field.Obstacles.Add(plant.Pos + plant.Fwd * 300f);
            ControlOutput o = default;
            for (int i = 0; i < 2; i++, t += Dt)
            {
                field.Step(Dt);
                o = pilot.Step(plant.Read(Dt), Jet(), pipeline, t, Dt, events, 0);
                plant.Step(o, Dt);
            }
            Assert.Equal(1f, o.Brake);
            Assert.Equal(0f, o.Throttle);
        }

        [Fact]
        public void AMemberOnALongEdgeTakesItsFarNodeOnlyWithinTheClaimWindow()
        {
            // Seen on a real field: a member 700 m from the end of its edge held the junction there for a minute.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            int south = field.Graph.NearestNode(new Vec3(-150f, 0f, 0f));
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            bool checkedFar = false, checkedNear = false;
            for (int i = 0; i < 120 * 30 && pilot.Phase < GroundPhase.LineUp; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
                if (plant.Pos.X > -155f && plant.Pos.Z < 400f && plant.Pos.Z > 300f)
                {
                    checkedFar = true;
                    Assert.NotEqual(1, field.Reservations.OwnerOfNode(south));
                }
                if (plant.Pos.X > -155f && plant.Pos.Z < 60f && plant.Pos.Z > 20f)
                {
                    checkedNear = true;
                    Assert.Equal(1, field.Reservations.OwnerOfNode(south));
                }
            }
            Assert.True(checkedFar && checkedNear);
        }

        [Fact]
        public void AMemberAdoptedLateInAMissionStillWaitsParkedFirst()
        {
            // Review M3a #9: the parked timer started at 0, so at mission time 1000 s it left at once (hangar doors
            // still opening).
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd) };
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            pilot.Step(s, Jet(), pipeline, 1000f, Dt, null, 0);
            pilot.Step(s, Jet(), pipeline, 1000f + GroundPilot.ParkedSeconds - 0.5f, Dt, null, 0);
            Assert.Equal(GroundPhase.Parked, pilot.Phase);
            pilot.Step(s, Jet(), pipeline, 1000f + GroundPilot.ParkedSeconds + 0.1f, Dt, null, 0);
            Assert.Equal(GroundPhase.TaxiOut, pilot.Phase);
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

        /// <summary>The direct taxiway edge between the apron junction (z = 500) and the south end (z = 0).</summary>
        private static int DirectEdge(TaxiGraph g, out int south, out int junction)
        {
            south = g.NearestNode(new Vec3(-150f, 0f, 0f));
            junction = g.NearestNode(new Vec3(-150f, 0f, 500f));
            foreach (int e in g.EdgesOf(junction))
                if (g.OtherEnd(e, junction) == south && g.EdgePoints(e).Length == 2) return e;
            throw new InvalidOperationException("no direct edge");
        }

        [Fact]
        public void AHeadOnIsResolvedByTheMemberAtTheJunctionTakingAnotherWay()
        {
            // Review M3a #4: the victim released the node it stood on and rerouted around its own edge.
            var field = new FieldTraffic(TestFields.WithBypass(), 0, false);
            int direct = DirectEdge(field.Graph, out int south, out int junction);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            float minX = 0f;
            bool coming = false;
            for (int i = 0; i < 200 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                if (!coming && field.Reservations.OwnerOfNode(junction) == 1)
                {
                    // A member coming north up the direct taxiway: it holds the edge and waits for the junction.
                    coming = true;
                    field.Reservations.TryAdvance(2, TaxiPriority.Departing, new[] { south, junction }, new[] { direct }, 0, 1);
                    field.Reservations.ReleaseNode(2, south);
                    field.Report(2, field.Graph.NodePos(south) + new Vec3(0f, 0f, 250f));
                }
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                plant.Step(pilot.Step(s, Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
                minX = Math.Min(minX, plant.Pos.X);
            }
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
            Assert.True(minX < -190f, $"took the direct taxiway (x {minX:0})");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
        }

        [Fact]
        public void AnAircraftStandingOnATaxiwayBlocksItUntilItMoves()
        {
            // Review M3a #11: nothing in the game ever blocked an edge, so the blocked-edge reroute never ran.
            var field = new FieldTraffic(TestFields.WithBypass(), 0, false);
            int direct = DirectEdge(field.Graph, out _, out _);
            field.Obstacles.Add(new Vec3(-150f, 0f, 250f));
            for (float t = 0f; t < FieldTraffic.BlockSeconds - 1f; t += Dt) field.Step(Dt);
            Assert.False(field.Reservations.Blocked(direct), "a moment's stop is not a blockage");
            for (float t = 0f; t < 2f; t += Dt) field.Step(Dt);
            Assert.True(field.Reservations.Blocked(direct));
            field.Obstacles.Clear();
            for (float t = 0f; t < 2f; t += Dt) field.Step(Dt);
            Assert.False(field.Reservations.Blocked(direct));
        }

        [Fact]
        public void AMemberStoppedBehindAParkedAircraftTakesAnotherWayInsteadOfWaitingToBeRelocated()
        {
            var field = new FieldTraffic(TestFields.WithBypass(), 0, false);
            field.Obstacles.Add(new Vec3(-150f, 0f, 250f));
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            for (int i = 0; i < 200 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
                if (pilot.TakeRelocation(out Pose to)) plant = new TestGroundPlant(to);
            }
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
        }

        [Fact]
        public void ARouteAvoidsATaxiwayInUseTheOtherWay()
        {
            var field = new FieldTraffic(TestFields.WithBypass(), 0, false);
            int direct = DirectEdge(field.Graph, out int south, out int junction);
            field.Reservations.TryAdvance(2, TaxiPriority.Departing, new[] { south, junction }, new[] { direct }, 0, 1);
            field.Reservations.ReleaseNode(2, south);
            field.Reservations.ReleaseNode(2, junction);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            float minX = 0f;
            for (int i = 0; i < 200 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                AircraftState s = plant.Read(Dt);
                field.Step(Dt);
                plant.Step(pilot.Step(s, Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
                minX = Math.Min(minX, plant.Pos.X);
            }
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
            Assert.True(minX < -190f, $"took the direct taxiway (x {minX:0})");
            Assert.Equal(0, events.CountOf(WingEventKind.Rerouted));
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
        public void ARelocationWaitsForAFreeHoldShortAndClaimsIt()
        {
            // Review M3a #3: the relocation teleported onto the hold-short whoever stood there.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            int hold = field.Graph.HoldShort(0, false);
            field.Reservations.TryAdvance(2, TaxiPriority.Departing, new[] { hold }, new int[0], 0, 0);
            field.Report(2, field.Graph.NodePos(hold));
            Pose spawn = field.Field.Hangars[0].Spawn;
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd) };
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            int relocations = 0;
            int i = 0;
            for (; i < 150 * 30; i++)
            {
                field.Step(Dt);
                pilot.Step(s, Jet(), pipeline, i * Dt, Dt, null, 0);
                if (pilot.TakeRelocation(out _)) relocations++;
            }
            Assert.Equal(0, relocations);
            field.Leave(2);
            for (int j = 0; j < 5 * 30; j++, i++)
            {
                field.Step(Dt);
                pilot.Step(s, Jet(), pipeline, i * Dt, Dt, null, 0);
                if (pilot.TakeRelocation(out _)) relocations++;
            }
            Assert.Equal(1, relocations);
            Assert.Equal(1, field.Reservations.OwnerOfNode(hold));
        }

        [Fact]
        public void ASpotSomeoneWasJustMovedToIsNotGivenToAnotherWhateverTheirReportSays()
        {
            // In game (boscali_north, M3b RTB run): two stuck members were relocated 0.4 s apart onto the same hold-short
            // and collided; the first had lined up (claims released) while its reported position still lagged.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose a = field.Field.Hangars[0].Spawn, b = field.Field.Hangars[1].Spawn;
            var first = new GroundPilot(1, field, AirframeClass.FixedWing, a, 0);
            var second = new GroundPilot(2, field, AirframeClass.FixedWing, b, 1);
            var sa = new AircraftState { Pos = a.Pos, Fwd = a.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, a.Fwd) };
            var sb = new AircraftState { Pos = b.Pos, Fwd = b.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, b.Fwd) };
            IFlightPipeline pa = FlightStack.NewPipeline(AirframeClass.FixedWing), pb = FlightStack.NewPipeline(AirframeClass.FixedWing);
            float firstAt = float.NaN, secondAt = float.NaN;
            for (int i = 0; i < 200 * 30; i++)
            {
                float t = i * Dt;
                field.Step(Dt);
                first.Step(sa, Jet(), pa, t, Dt, null, 0);
                if (first.TakeRelocation(out _))
                {
                    firstAt = t;
                    field.Reservations.ReleaseAll(1);   // it lined up at once; its report still shows the old spot
                }
                second.Step(sb, Jet(), pb, t, Dt, null, 1);
                if (second.TakeRelocation(out _) && float.IsNaN(secondAt)) secondAt = t;
            }
            Assert.False(float.IsNaN(firstAt));
            Assert.True(float.IsNaN(secondAt) || secondAt - firstAt >= FieldTraffic.RelocationGuardSeconds,
                $"moved onto the same spot {secondAt - firstAt:0.0} s later");
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
        public void AHelicopterSpoolsItsRotorUpOnTheGroundBeforeLiftingOff()
        {
            // In game: spawned with the rotor stopped, full collective at once kept it from ever spinning up.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var pilot = new GroundPilot(3, field, AirframeClass.Rotary, spawn, 1);
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 134f });
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.Rotary);
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd), RotorRpm = 0.3f };
            ControlOutput o = default;
            for (int i = 0; i < 10 * 30; i++) o = pilot.Step(s, helo, pipeline, i * Dt, Dt, null, 0);
            Assert.Equal(GroundPhase.Parked, pilot.Phase);
            Assert.Equal(GroundPilot.SpoolCollective, o.Throttle, 3);
            Assert.Equal(1f, o.Brake);
            s.RotorRpm = GroundPilot.SpoolRpm + 0.01f;
            pilot.Step(s, helo, pipeline, 10f, Dt, null, 0);
            Assert.Equal(GroundPhase.LiftOff, pilot.Phase);
        }

        [Fact]
        public void AHelicopterWithNoRotorReadingLiftsOffAfterTheSpoolTimeout()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var pilot = new GroundPilot(3, field, AirframeClass.Rotary, spawn, 1);
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 134f });
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.Rotary);
            var s = new AircraftState { Pos = spawn.Pos, Fwd = spawn.Fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, spawn.Fwd), RotorRpm = 0f };
            for (int i = 0; i < (GroundPilot.SpoolSeconds + 1f) * 30; i++) pilot.Step(s, helo, pipeline, i * Dt, Dt, null, 0);
            Assert.Equal(GroundPhase.LiftOff, pilot.Phase);
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
