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
