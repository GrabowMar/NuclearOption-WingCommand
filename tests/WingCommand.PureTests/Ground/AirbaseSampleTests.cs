using Xunit;

namespace WingCommand.PureTests
{
    public class AirbaseSampleTests
    {
        private const string Dump = @"{
  ""mission"": ""Free Flight"",
  ""airbases"": [
    { ""name"": ""airbase_desert1"", ""center"": [100, 20, 200], ""radius"": 3000, ""attached"": false,
      ""runways"": [ { ""index"": 0, ""start"": [0, 20, 0], ""end"": [0, 20, 2000], ""width"": 45, ""length"": 2000,
                       ""reversable"": true, ""takeoff"": true, ""landing"": true, ""arrestor"": false, ""skiJump"": false,
                       ""entryPoints"": [ { ""position"": [40, 20, 100], ""forward"": [-1, 0, 0] } ], ""exitPoints"": [] } ],
      ""taxiRoads"": [ { ""points"": [[40, 20, 100], [40, 20, 600], [200, 20, 600]], ""bridge"": false } ],
      ""hangars"": [ { ""index"": 0, ""position"": [230, 20, 600], ""forward"": [-1, 0, 0], ""available"": true,
                       ""disabled"": false, ""carrierDoors"": false, ""types"": [""FS-20 Vortex"", ""CI-22 Cricket""] } ],
      ""servicePoints"": [ { ""position"": [150, 20, 650], ""forward"": [0, 0, 1] } ],
      ""verticalPads"": [] },
    { ""name"": ""broken"", ""error"": ""reflection failed"" },
    { ""name"": ""carrier1"", ""center"": [9000, 18, 0], ""radius"": 500, ""attached"": true,
      ""runways"": [], ""taxiRoads"": [], ""hangars"": [], ""servicePoints"": [],
      ""verticalPads"": [ { ""position"": [9010, 18, 5], ""forward"": [0, 0, 1], ""size"": 40 } ] }
  ]
}";

        [Fact]
        public void TheDumpReadsIntoSamplesSkippingUnreadableAirbases()
        {
            var fields = AirbaseSample.FromDumpJson(Dump);
            Assert.Equal(2, fields.Count);
            AirbaseSample desert = fields[0];
            Assert.Equal("airbase_desert1", desert.Name);
            Assert.Equal(45f, desert.Runways[0].Width);
            Assert.True(desert.Runways[0].Reversable && desert.Runways[0].Takeoff);
            Assert.Equal(new Vec3(0f, 20f, 2000f), desert.Runways[0].End);
            Assert.Single(desert.Runways[0].Entries);
            Assert.Equal(3, desert.Roads[0].Length);
            Assert.Equal(new[] { "FS-20 Vortex", "CI-22 Cricket" }, desert.Hangars[0].Types);
            Assert.Equal(new Vec3(-1f, 0f, 0f), desert.Hangars[0].Spawn.Fwd);
            Assert.Single(desert.ServicePoints);
            AirbaseSample carrier = fields[1];
            Assert.True(carrier.Attached);
            Assert.Empty(carrier.Roads);
            Assert.Single(carrier.Pads);
        }

        [Fact]
        public void MalformedJsonGivesNoFields() => Assert.Empty(AirbaseSample.FromDumpJson("{ not json"));
    }
}
