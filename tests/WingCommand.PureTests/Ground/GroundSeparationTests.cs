using System;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Wing aircraft never touch on the ground (overnight 2026-09-28 at Boscali North: an SFB-81 lined up past the one
    /// holding short and both were damaged; a refitted FS-20 U-turned out of its stand into something and never flew again).</summary>
    public class GroundSeparationTests
    {
        private const float Dt = 1f / 30f;

        private static AirframeProfile Jet(float span = 11f) => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 216f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = span,
            TakeoffSpeed = 70f,
        });

        [Fact]
        public void ABigAircraftStopsForAMemberParkedWithinItsSpanOfTheTaxiway()
        {
            // 25 m off the taxiway's centreline: outside the old fixed 15 m corridor, inside a 42 m wingspan.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            var parked = new Vec3(-125f, 0f, 250f);
            field.Report(2, parked);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            AirframeProfile bomber = Jet(42f);
            float closest = float.MaxValue;
            for (int i = 0; i < 50 * 30; i++)
            {
                field.Report(2, parked);
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), bomber, pipeline, i * Dt, Dt, null, 0), Dt);
                closest = Math.Min(closest, (plant.Pos - parked).Horizontal.Length);
            }
            Assert.True(closest > 42f, $"came within {closest:0} m of the parked member");
            Assert.Equal(GroundStop.Member, pilot.Stop);
        }

        [Fact]
        public void ALongAircraftStopsItsLengthAndAMarginBehindAMemberAhead()
        {
            // Day-1 sim (SFB-81, Boscali North): stopped 41.6 m centre to centre behind another — the fixed 40 m follow gap put a
            // bomber's nose into the tail ahead, and the physics slowed the game to a tenth of real time.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            var ahead = new Vec3(-150f, 0f, 250f);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            AirframeProfile bomber = Jet(42f);
            bomber.LengthM = 45f;
            float closest = float.MaxValue;
            for (int i = 0; i < 50 * 30; i++)
            {
                field.Report(2, ahead);
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), bomber, pipeline, i * Dt, Dt, null, 0), Dt);
                closest = Math.Min(closest, (plant.Pos - ahead).Horizontal.Length);
            }
            Assert.True(closest >= 55f - 3f, $"stopped {closest:0} m from the member ahead");
            Assert.Equal(GroundStop.Member, pilot.Stop);
        }

        [Fact]
        public void AFighterStillPassesAMemberWellClearOfItsWings()
        {
            // The same member, for an 11 m fighter: 25 m off the centreline is clear, no stop.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            var parked = new Vec3(-125f, 0f, 250f);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            for (int i = 0; i < 150 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                field.Report(2, parked);
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
            }
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}, stop {pilot.Stop}");
        }

        [Fact]
        public void TheMemberHoldingShortLinesUpBeforeOneWaitingFurtherBack()
        {
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Vec3 hold = field.Graph.NodePos(field.Graph.HoldShort(0, false));
            field.Departures.Expect(1, 1);
            field.Departures.Expect(2, 1);
            field.Departures.Enqueue(1, 0f);
            field.Departures.Enqueue(2, 0f);
            field.Report(1, hold + new Vec3(-10f, 0f, 45f));
            field.Report(2, hold);
            Assert.True(field.OtherLinesUpFirst(1, hold + new Vec3(-10f, 0f, 45f)));
            Assert.False(field.OtherLinesUpFirst(2, hold));
            Assert.True(field.Departures.MayLineUp(2, 1f));
            // Once it has lined up and cleared the threshold, the one behind goes.
            field.Departures.ClearedThreshold(2);
            field.Leave(2);
            Assert.False(field.OtherLinesUpFirst(1, hold + new Vec3(-10f, 0f, 45f)));
            Assert.True(field.Departures.MayLineUp(1, 2f));
        }

        [Fact]
        public void ALineUpThatCannotMoveIsPutOnItsSlotInsteadOfGivenUp()
        {
            // Day-1 sims (refit FS-20, SFB-81): from a hold point behind and beside the runway, the line-up turn stuck at 85 %
            // throttle for 80 s and the member was released after the line-up time ran out.
            var field = new FieldTraffic(TestFields.Simple(), 0, false);
            Pose spawn = field.Field.Hangars[0].Spawn;
            var plant = new TestGroundPlant(spawn);
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, spawn, 0);
            field.Departures.Expect(1, 1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            int i = 0;
            for (; i < 300 * 30 && pilot.Phase != GroundPhase.LineUp; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.LineUp, pilot.Phase);
            // Stuck: the aircraft no longer moves whatever it asks.
            AircraftState stuck = plant.Read(Dt);
            stuck.Vel = Vec3.Zero;
            stuck.Tas = 0f;
            Pose to = default;
            bool moved = false;
            for (int k = 0; k < 40 * 30 && !moved; k++, i++)
            {
                field.Step(Dt);
                pilot.Step(stuck, Jet(), pipeline, i * Dt, Dt, events, 0);
                moved = pilot.TakeRelocation(out to);
            }
            Assert.True(moved, $"still {pilot.Phase} after 40 s stuck");
            Vec3 slot = LineupPlanner.Slot(field.Field.Runways[0], false, 0, 0, 1, 1);
            Assert.True((to.Pos - slot).Horizontal.Length < 1f, $"put at {to.Pos}, slot {slot}");
            Assert.True(Vec3.Dot(to.Fwd, field.Runway.Direction(false)) > 0.99f, "facing down the runway");
            plant.Pos = to.Pos;
            plant.Fwd = to.Fwd;
            plant.Speed = 0f;
            for (int k = 0; k < 30 * 30 && pilot.Phase == GroundPhase.LineUp; k++, i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Roll, pilot.Phase);
            Assert.Equal(0, events.CountOf(WingEventKind.DepartureAborted));
        }

        [Fact]
        public void NoTowRoundWithAnotherAircraftWithinASpanOfTheStand()
        {
            // Review (day 1): turning in place with a neighbour inside the swept circle interpenetrates them; it drives out instead
            // (the span-aware corridor then stops it for the neighbour).
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var standAt = new Pose(new Vec3(-200f, 0f, 300f), new Vec3(-1f, 0f, 0f));
            var plant = new TestGroundPlant(standAt);
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, standAt, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            pilot.StandHere(standAt, 0f);
            field.Report(5, standAt.Pos + new Vec3(0f, 0f, 9f));
            field.Departures.Expect(4, 1);
            pilot.Depart(0f);
            bool towed = false;
            for (int i = 0; i < 10 * 30 && pilot.Phase == GroundPhase.Parked; i++)
            {
                field.Report(5, standAt.Pos + new Vec3(0f, 0f, 9f));
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, null, 0), Dt);
                towed |= pilot.TakeRelocation(out _);
            }
            Assert.Equal(GroundPhase.TaxiOut, pilot.Phase);
            Assert.False(towed, "towed round next to another aircraft");
        }

        [Fact]
        public void AJetParkedFacingAwayFromItsWayOutIsTowedRoundBeforeItTaxis()
        {
            var field = new FieldTraffic(TestFields.WithServicePointAndExit(), 0, false);
            var landed = new Pose(new Vec3(0f, 0f, 930f), new Vec3(0f, 0f, 1f));
            var plant = new TestGroundPlant(landed) { Speed = 10f };
            var pilot = new GroundPilot(4, field, AirframeClass.FixedWing, landed, -1);
            IFlightPipeline pipeline = FlightStack.NewPipeline(AirframeClass.FixedWing);
            var events = new WingEventRing();
            pilot.TaxiIn(plant.Read(Dt), 0f, events, 0);
            int i = 0;
            for (; i < 240 * 30 && pilot.Phase != GroundPhase.Stand; i++)
            {
                field.Step(Dt);
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
            }
            Assert.Equal(GroundPhase.Stand, pilot.Phase);
            // It came in from the taxiway (east of the service point): its nose points away from the way out.
            Assert.True(plant.Fwd.X < 0f, $"parked facing {plant.Fwd}");
            Vec3 standAt = plant.Pos;
            field.Departures.Expect(4, 1);
            pilot.Depart(i * Dt);
            bool towed = false;
            float travelled = 0f, worst = 1f;
            Vec3 startFwd = Vec3.Zero;
            for (; i < 300 * 30 && pilot.Phase < GroundPhase.HoldShort; i++)
            {
                field.Step(Dt);
                Vec3 before = plant.Pos;
                plant.Step(pilot.Step(plant.Read(Dt), Jet(), pipeline, i * Dt, Dt, events, 0), Dt);
                if (pilot.TakeRelocation(out Pose to))
                {
                    towed = true;
                    Assert.True((to.Pos - standAt).Horizontal.Length < 1f, "towed round where it stands");
                    plant.Pos = to.Pos;
                    plant.Fwd = to.Fwd.Horizontal.Normalized;
                    plant.Speed = 0f;
                    startFwd = plant.Fwd;
                    continue;
                }
                if (!towed || travelled > 30f) continue;
                travelled += (plant.Pos - before).Horizontal.Length;
                worst = Math.Min(worst, Vec3.Dot(plant.Fwd, startFwd));
            }
            Assert.True(towed, "towed round before taxiing");
            Assert.True(worst > 0f, "no U-turn on the way out of the stand");
            Assert.True(pilot.Phase >= GroundPhase.HoldShort, $"phase {pilot.Phase}");
        }
    }
}
