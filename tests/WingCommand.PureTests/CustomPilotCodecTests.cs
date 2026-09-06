using System;
using System.Threading.Tasks;
using Xunit;

namespace WingCommand.PureTests
{
    public class CustomPilotCodecTests
    {
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
    }
}
