using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Departures on the game's own airfields (the nomodkit dump of Free Flight: no hangars, so launches stand on
    /// service points), against the taxi roads, runway entries and service points as the game lays them out.</summary>
    public class RealFieldScenarioTests
    {
        private const float Dt = 1f / 30f;
        private const int Count = 4;

        private static AirbaseSample Load(string name) =>
            AirbaseSample.FromDumpJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "airbases", "free-flight.json")))
                .Single(a => a.Name == name);

        [Theory]
        [InlineData("airbase_city")]
        [InlineData("airbase_desert1")]
        [InlineData("airbase_mountains")]
        [InlineData("airbase_boscali_north")]
        [InlineData("airstrip_city2")]
        [InlineData("highwaystrip1")]
        public void AFourShipDepartsFromTheServicePointsWithoutRelocationsOrAborts(string name)
        {
            AirbaseSample sample = Load(name);
            Assert.True(FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse));
            var field = new FieldTraffic(sample, runway, reverse);
            AirframeProfile p = GroundScenarioTests.Jet();
            var pilots = new List<GroundPilot>();
            var plants = new List<DepartingPlant>();
            var pipelines = new List<IFlightPipeline>();
            var taken = new List<Vec3>();
            for (int k = 0; k < Count; k++)
            {
                Assert.True(ServiceSpots.Pick(field, at => taken.Exists(t => (t - at).Horizontal.Length < ServiceSpots.ClearRadius),
                    out ServiceSpot spot));
                taken.Add(spot.Pose.Pos);
                field.Departures.Expect(k, LineupPlanner.Abreast(field.Runway.Width, p.SpanM));
                pilots.Add(new GroundPilot(k, field, AirframeClass.FixedWing, spot.Pose, -1, spot.StartNode));
                plants.Add(new DepartingPlant(PlantParams.GenericFighter, spot.Pose, p.TakeoffSpeed, p.WheelbaseM, p.SteerLockDeg));
                pipelines.Add(FlightStack.NewPipeline(AirframeClass.FixedWing));
            }
            var events = new WingEventRing();
            for (int i = 0; i < 400 * 30 && pilots.Exists(x => !x.Done && x.Phase != GroundPhase.Aborted); i++)
            {
                field.Step(Dt);
                for (int k = 0; k < Count; k++)
                {
                    if (pilots[k].Done || pilots[k].Phase == GroundPhase.Aborted) continue;
                    plants[k].Step(pilots[k].Step(plants[k].Read(Dt), p, pipelines[k], i * Dt, Dt, events, k), Dt);
                    if (pilots[k].TakeRelocation(out Pose to)) plants[k].Teleport(to);
                }
            }
            for (int k = 0; k < Count; k++) Assert.True(pilots[k].Done, $"{name}: member {k} is {pilots[k].Phase} at {plants[k].Position}");
            Assert.Equal(0, events.CountOf(WingEventKind.Relocated));
            Assert.Equal(0, events.CountOf(WingEventKind.DepartureAborted));
        }
        /// <summary>The in-game boscali_north of 2026-09-24 (a dump from a run: 8 hangars, one service point, runway exits
        /// on the centreline, no entry points), launching out of its first hangars as the game hands them out.</summary>
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void BoscaliNorthDepartsFromItsHangarsWithoutRelocationsOrAborts(int count)
        {
            int[] hangars = new int[count];
            for (int k = 0; k < count; k++) hangars[k] = k;
            DepartBoscaliNorth(hangars);
        }

        /// <summary>Every pair of the field's hangars (the game hands out whichever can spawn the type).</summary>
        [Fact]
        public void BoscaliNorthDepartsAPairFromAnyTwoHangars()
        {
            var failures = new List<string>();
            for (int a = 0; a < 8; a++)
                for (int b = a + 1; b < 8; b++)
                {
                    string why = TryDepartBoscaliNorth(new[] { a, b });
                    if (why != null) failures.Add($"hangars {a},{b}: {why}");
                }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        private static void DepartBoscaliNorth(int[] hangars)
        {
            string why = TryDepartBoscaliNorth(hangars);
            Assert.True(why == null, why);
        }

        private static string TryDepartBoscaliNorth(int[] hangars)
        {
            int count = hangars.Length;
            AirbaseSample sample = AirbaseSample.FromDumpJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "airbases",
                "boscali-north-hangars.json"))).Single();
            Assert.True(FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse));
            var field = new FieldTraffic(sample, runway, reverse);
            AirframeProfile p = GroundScenarioTests.Jet();
            var pilots = new List<GroundPilot>();
            var plants = new List<DepartingPlant>();
            var pipelines = new List<IFlightPipeline>();
            for (int k = 0; k < count; k++)
            {
                Pose spawn = sample.Hangars[hangars[k]].Spawn;
                field.Departures.Expect(k, LineupPlanner.Abreast(field.Runway.Width, p.SpanM));
                pilots.Add(new GroundPilot(k, field, AirframeClass.FixedWing, spawn, hangars[k]));
                plants.Add(new DepartingPlant(PlantParams.GenericFighter, spawn, p.TakeoffSpeed, p.WheelbaseM, p.SteerLockDeg));
                pipelines.Add(FlightStack.NewPipeline(AirframeClass.FixedWing));
            }
            var events = new WingEventRing();
            for (int i = 0; i < 400 * 30 && pilots.Exists(x => !x.Done && x.Phase != GroundPhase.Aborted); i++)
            {
                field.Step(Dt);
                for (int k = 0; k < count; k++)
                {
                    if (pilots[k].Done || pilots[k].Phase == GroundPhase.Aborted) continue;
                    plants[k].Step(pilots[k].Step(plants[k].Read(Dt), p, pipelines[k], i * Dt, Dt, events, k), Dt);
                    if (pilots[k].TakeRelocation(out Pose to)) plants[k].Teleport(to);
                }
            }
            for (int k = 0; k < count; k++)
                if (!pilots[k].Done) return $"member {k} (hangar {hangars[k]}) is {pilots[k].Phase} ({pilots[k].Stop}) at {plants[k].Position}";
            if (events.CountOf(WingEventKind.Relocated) > 0) return $"{events.CountOf(WingEventKind.Relocated)} relocations";
            if (events.CountOf(WingEventKind.DepartureAborted) > 0) return "aborted";
            return null;
        }
    }
}
