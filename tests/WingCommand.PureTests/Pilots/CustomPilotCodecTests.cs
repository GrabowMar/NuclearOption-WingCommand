using System;
using System.Threading.Tasks;
using Xunit;

namespace WingCommand.PureTests
{
    public class CustomPilotCodecTests
    {
        [Fact]
        public void OldEquipmentFieldsAreIgnoredWithoutLosingAppearance()
        {
            var pilot = Assert.Single(CustomPilotCodec.Decode(
                "{\"callsign\":\"LEGACY\",\"portraitVersion\":2,\"body\":\"female\",\"face\":4,\"hair\":3,\"uniform\":2,\"accessory\":8}").Pilots);
            Assert.Equal(0, pilot.Accessory);
            Assert.Equal(4, pilot.Face);
            Assert.Equal(3, pilot.Hair);
            Assert.Equal(2, pilot.Uniform);
        }

        [Theory]
        [InlineData("4294967297")]
        [InlineData("-4294967295")]
        [InlineData("1e30")]
        [InlineData("NaN")]
        public void OutOfRangeStatisticsFallBackInsteadOfWrapping(string value)
        {
            var record = Assert.Single(CustomPilotCodec.Decode(
                "{\"callsign\":\"FOX\",\"xp\":" + value + "}").Pilots);
            Assert.Equal(0, record.Xp);
        }

        [Theory]
        [InlineData("{\"pilots\": ]}")]
        [InlineData("[}")]
        [InlineData("{\"pilots\": /}")]
        [InlineData("{\"callsign\": \"unfinished}")]
        [InlineData("{\"callsign\": \"TEST\"")]
        [InlineData("/* unfinished")]
        public async Task MalformedInputFinishesWithoutImportingPartialRecords(string input)
        {
            var payload = await Task.Run(() => CustomPilotCodec.Decode(input))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Empty(payload.Pilots);
            Assert.Empty(payload.Chatters);
        }


        [Fact]
        public void DeepNestingIsRejectedBeforeExhaustingTheStack()
        {
            Assert.Empty(CustomPilotCodec.Decode(new string('[', 10000) + new string(']', 10000)).Pilots);
        }

        [Fact]
        public void CommentsCaseInsensitiveFieldsAndSupportedRootShapesSurvive()
        {
            const string pilot = "{\"CALLSIGN\":\"  FOX  \",/* note */\"name\":\"A\\u006cex\",\"xp\":7}";
            foreach (string input in new[] { pilot, "[" + pilot + "]", "{\"PILOTS\":[" + pilot + "]}// end" })
            {
                var record = Assert.Single(CustomPilotCodec.Decode(input).Pilots);
                Assert.Equal("FOX", record.Callsign);
                Assert.Equal("Alex", record.Name);
                Assert.Equal(7, record.Xp);
            }
            var sample = CustomPilotCodec.Decode(CustomPilotCodec.SampleJson());
            Assert.NotEmpty(sample.Pilots);
            Assert.NotEmpty(sample.Chatters);
        }

        [Fact]
        public void EncodeAndDecodeRoundTripPreservesAllPilotData()
        {
            var original = new CustomPilotRecord
            {
                Name = "Jane Doe",
                Callsign = "VIPER",
                DialogueTag = "VIPER",
                Persona = ChatterPersona.Aggressive,
                Background = "Former test pilot who likes low-level passes.",
                Xp = 350,
                Kills = 12,
                Sorties = 20,
                PortraitVersion = 2,
                Body = PortraitBody.Female,
                Face = 1,
                Hair = 3,
                Uniform = 1,
                Accessory = 6,
                Backdrop = 2,
            };

            string json = CustomPilotCodec.EncodeSingle(original);
            Assert.Contains("\"name\": \"Jane Doe\"", json);
            Assert.Contains("\"callsign\": \"VIPER\"", json);
            Assert.Contains("\"portraitVersion\": 2", json);
            Assert.Contains("\"body\": \"female\"", json);
            Assert.DoesNotContain("\"accessory\"", json);

            var decoded = Assert.Single(CustomPilotCodec.Decode(json).Pilots);
            Assert.Equal("Jane Doe", decoded.Name);
            Assert.Equal("VIPER", decoded.Callsign);
            Assert.Equal("VIPER", decoded.DialogueTag);
            Assert.Equal(ChatterPersona.Aggressive, decoded.Persona);
            Assert.Equal("Former test pilot who likes low-level passes.", decoded.Background);
            Assert.Equal(350, decoded.Xp);
            Assert.Equal(12, decoded.Kills);
            Assert.Equal(20, decoded.Sorties);
            Assert.Equal(PortraitBody.Female, decoded.Body);
            Assert.Equal(1, decoded.Face);
            Assert.Equal(3, decoded.Hair);
            Assert.Equal(1, decoded.Uniform);
            Assert.Equal(0, decoded.Accessory);
            Assert.Equal(2, decoded.Backdrop);
            Assert.True(decoded.HasCustomPortrait);
        }

        [Fact]
        public void RemovePilotWorksForSeededFileAndPreservesChatter()
        {
            string json = CustomPilotCodec.RemovePilot(CustomPilotCodec.SampleJson(), "ghost", out bool removed);

            Assert.True(removed);
            CustomPilotPayload payload = CustomPilotCodec.Decode(json);
            Assert.DoesNotContain(payload.Pilots, p => p.Callsign == "GHOST");
            Assert.Equal(2, payload.Pilots.Count);
            Assert.Equal(9, payload.Chatters.Count);
        }

        [Fact]
        public void LegacyAndPreviouslyExportedPortraitsNormalizeSafely()
        {
            var rawLegacy = Assert.Single(CustomPilotCodec.Decode(
                "{\"callsign\":\"OLD\",\"face\":4,\"hair\":2,\"uniform\":1,\"backdrop\":2}").Pilots);
            Assert.Equal(PortraitBody.Female, rawLegacy.Body);
            Assert.Equal(1, rawLegacy.Face);
            Assert.Equal(3, rawLegacy.Hair);
            Assert.Equal(1, rawLegacy.Uniform);
            Assert.Equal(0, rawLegacy.Accessory);

            var badExport = Assert.Single(CustomPilotCodec.Decode(
                "{\"callsign\":\"OLD-EXPORT\",\"face\":1,\"hair\":8,\"uniform\":15,\"backdrop\":1}").Pilots);
            Assert.Equal(PortraitBody.Male, badExport.Body);
            Assert.Equal(1, badExport.Face);
            Assert.Equal(3, badExport.Hair);
            Assert.Equal(1, badExport.Uniform);
        }
    }
}
