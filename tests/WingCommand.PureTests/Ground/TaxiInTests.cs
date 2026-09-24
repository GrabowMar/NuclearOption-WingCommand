using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Taxi-in (spec M3 §4): from the runway after a landing, or turned round while taxiing out, to a stand.</summary>
    public class TaxiInTests
    {
        private const float Dt = 1f / 30f;

        private static AirframeProfile Jet() => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 216f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = 11f,
            TakeoffSpeed = 70f,
        });

        [Fact]
        public void ARunwayExitIsANodeJoinedToTheTaxiway()
        {
            TaxiGraph g = TaxiGraph.Build(TestFields.WithServicePointAndExit());
            int exit = g.NearestNode(new Vec3(0f, 0f, 1000f));
            Assert.Equal(NodeKind.RunwayExit, g.Kind(exit));
            Assert.Single(g.EdgesOf(exit));
            Assert.True((g.NodePos(g.OtherEnd(g.EdgesOf(exit)[0], exit)) - new Vec3(-150f, 0f, 1000f)).Length < 1f);
        }

        [Fact]
        public void ALandedMemberTaxisOffTheRunwayToAServicePointAndStands()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var landed = new Pose(new Vec3(0f, 0f, 930f), new Vec3(0f, 0f, 1f));
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            Assert.True(pilot.TaxiIn(plant.Read(Dt), 0f, events, 0));
            Assert.Equal(GroundPhase.TaxiIn, pilot.Phase);
            for (int i = 0; i < 240 * 30 && pilot.Phase == GroundPhase.TaxiIn; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            Assert.True((plant.Pos - new Vec3(-200f, 0f, 300f)).Horizontal.Length < 5f, $"stands at {plant.Pos}");
            Assert.True(plant.Speed < 0.5f);
            Assert.True(pilot.DespawnOnRelease(plant.Read(Dt)), "a member standing on the field goes back to the reserve");
            Assert.Equal(1, events.CountOf(WingEventKind.Parked));
        }

        [Fact]
        public void AMemberRecalledWhileTaxiingOutTurnsForAStandAndLeavesTheDeparture()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 1);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            int i = 0;
            for (; i < 30 * 30; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.TaxiOut, pilot.Phase);
            Assert.True(pilot.TaxiIn(plant.Read(Dt), i * Dt, events, 0));
            for (; i < 300 * 30 && pilot.Phase == GroundPhase.TaxiIn; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            Assert.True((plant.Pos - new Vec3(-200f, 0f, 300f)).Horizontal.Length < 5f, $"stands at {plant.Pos}");
            Assert.False(field.Departures.RunwayLocked);
            Assert.Equal(0, field.Departures.Rows);
        }

        [Fact]
        public void AnArrivalAndADepartureMeetingOnTheOnlyTaxiwayBothGetThrough()
        {
            // One taxiway: the departure goes south from the hangar at z = 700 to the hold-short at z = 0; the arrival
            // leaves the runway at z = 1000... no: at z = 100 and goes north to the service point at z = 300.
            AirbaseSample sample = TestFields.WithServicePoint();
            sample.Runways[0].Exits = new[] { new Pose(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 1f)) };
            var field = new FieldTraffic(sample, 0, false);
            Pose spawn = field.Field.Hangars[1].Spawn;
            var departing = new TestGroundPlant(spawn);
            var departure = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 1);
            field.Departures.Expect(1, 1);
            var landed = new Pose(new Vec3(0f, 0f, 60f), new Vec3(0f, 0f, 1f));
            var arriving = new TestGroundPlant(landed) { Speed = 8f };
            var arrival = new GroundPilot(2, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline p1 = FlightStack.NewPipeline(AirframeClass.FixedWing), p2 = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            bool landedYet = false;
            for (int i = 0; i < 400 * 30 && (departure.Phase < GroundPhase.LineUp || arrival.Phase != GroundPhase.Stand); i++)
            {
                float t = i * Dt;
                field.Step(Dt);
                departing.Step(departure.Step(departing.Read(Dt), Jet(), p1, t, Dt, events, 0), Dt);
                if (departure.TakeRelocation(out Pose d)) departing = new TestGroundPlant(d);
                // The arrival lands once the departure is on the taxiway heading south.
                if (!landedYet && departing.Pos.X > -155f && departing.Pos.Z < 450f)
                {
                    landedYet = true;
                    Assert.True(arrival.TaxiIn(arriving.Read(Dt), t, events, 1));
                }
                if (landedYet)
                {
                    arriving.Step(arrival.Step(arriving.Read(Dt), Jet(), p2, t, Dt, events, 1), Dt);
                    if (arrival.TakeRelocation(out Pose a)) arriving = new TestGroundPlant(a);
                }
            }
            Assert.True(landedYet);
            Assert.True(departure.Phase >= GroundPhase.LineUp, $"the departure is {departure.Phase} at {departing.Pos}");
            Assert.Equal(GroundPhase.Stand, arrival.Phase);
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
        }

        [Fact]
        public void AMemberAlreadyRollingCannotBeTurnedRound()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            for (int i = 0; i < 300 * 30 && pilot.Phase != GroundPhase.Roll; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
            }
            Assert.Equal(GroundPhase.Roll, pilot.Phase);
            Assert.False(pilot.TaxiIn(plant.Read(Dt), 300f, null, 0));
            Assert.Equal(GroundPhase.Roll, pilot.Phase);
        }

        [Fact]
        public void AStandingMemberDepartsAgainFromItsStand()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var landed = new Pose(new Vec3(0f, 0f, 930f), new Vec3(0f, 0f, 1f));
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            pilot.TaxiIn(plant.Read(Dt), 0f, null, 0);
            int i = 0;
            for (; i < 240 * 30 && pilot.Phase != GroundPhase.Stand; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            field.Departures.Expect(4, 1);
            pilot.Depart(i * Dt);
            Assert.Equal(GroundPhase.Parked, pilot.Phase);
            for (; i < 600 * 30 && pilot.Phase != GroundPhase.ClimbOut; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
            }
            Assert.Equal(GroundPhase.ClimbOut, pilot.Phase);
        }
        [Fact]
        public void TheLastResortStandIsARoadNodeOffEveryRunway()
        {
            // Review M3b I2: with the service points and hangar exits taken, the stand was the node nearest the field
            // centre — on five of eight real fields a runway node.
            AirbaseSample sample = TestFields.WithServicePointAndExit();
            sample.Center = new Vec3(0f, 0f, 1000f);
            var field = new FieldTraffic(sample, 0, false);
            field.Obstacles.Add(new Vec3(-200f, 0f, 300f));
            for (int h = 0; h < sample.Hangars.Length; h++) field.Obstacles.Add(field.Graph.NodePos(field.Graph.HangarExit(h)));
            var landed = new Pose(new Vec3(0f, 0f, 930f), Vec3.Forward);
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            Assert.True(pilot.TaxiIn(new TestGroundPlant(landed).Read(Dt), 0f, new WingEventRing(), 0));
            Vec3 stand = field.Graph.NodePos(pilot.StandNode);
            Assert.Equal(NodeKind.Road, field.Graph.Kind(pilot.StandNode));
            foreach (RunwaySample r in sample.Runways) Assert.False(r.Contains(stand, GroundPilot.StandRunwayMargin), $"stand {stand}");
        }

        [Fact]
        public void AMemberTaxiingInOnTheRunwayKeepsItBusyForDepartures()
        {
            // Review M3b I3: the runway counted as free while our landed member still rolled along it to an exit.
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var landed = new Pose(new Vec3(0f, 0f, 930f), Vec3.Forward);
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            Assert.True(pilot.TaxiIn(plant.Read(Dt), 0f, events, 0));
            plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, 0f, Dt, events, 0), Dt);
            field.Step(Dt);
            Assert.True(field.Departures.RunwayBusy);
            for (int i = 1; i < 240 * 30 && pilot.Phase == GroundPhase.TaxiIn; i++)
            {
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
                field.Step(Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            Assert.False(field.Departures.RunwayBusy);
        }

        [Fact]
        public void AMemberWhoseStandIsTakenOnTheWayStandsElsewhere()
        {
            // Review M3b I6: a stand occupied after it was chosen stranded the member short of it for good.
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var landed = new Pose(new Vec3(0f, 0f, 930f), Vec3.Forward);
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            Assert.True(pilot.TaxiIn(plant.Read(Dt), 0f, events, 0));
            Assert.Equal(field.Graph.ServiceNode(0), pilot.StandNode);
            field.Obstacles.Add(new Vec3(-200f, 0f, 300f));
            for (int i = 0; i < 400 * 30 && pilot.Phase == GroundPhase.TaxiIn; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            Assert.True((plant.Pos - new Vec3(-200f, 0f, 300f)).Horizontal.Length > ServiceSpots.ClearRadius, $"stands at {plant.Pos}");
            Assert.False(field.Runway.Contains(plant.Pos, FieldTraffic.RunwayMargin), $"stands at {plant.Pos}");
        }
    }
}
