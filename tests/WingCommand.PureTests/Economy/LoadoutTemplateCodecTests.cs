using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class LoadoutTemplateCodecTests
    {
        [Fact]
        public void ATemplateSurvivesARoundTrip()
        {
            var r = new LoadoutTemplateRecord("t1a2b3c4d", "FS-20", "CAP TWO", new[] { "aim9", null, "tank" });
            List<LoadoutTemplateRecord> back = LoadoutTemplateCodec.Decode(LoadoutTemplateCodec.Encode(new[] { r }));
            Assert.Single(back);
            Assert.Equal("t1a2b3c4d", back[0].Id);
            Assert.Equal("FS-20", back[0].AirframeKey);
            Assert.Equal("CAP TWO", back[0].Name);
            Assert.Equal(new[] { "aim9", null, "tank" }, back[0].MountKeys);
        }

        [Fact]
        public void DelimitersInANameSurviveARoundTrip()
        {
            var r = new LoadoutTemplateRecord("t1", "FS-20", "A;B|C,D%E", new[] { "k;1" });
            LoadoutTemplateRecord back = LoadoutTemplateCodec.Decode(LoadoutTemplateCodec.Encode(new[] { r }))[0];
            Assert.Equal("A;B|C,D%E", back.Name);
            Assert.Equal("k;1", back.MountKeys[0]);
        }

        [Fact]
        public void AMalformedRecordIsDroppedAndTheOthersLoad()
        {
            string good = LoadoutTemplateCodec.Encode(new[] { new LoadoutTemplateRecord("t1", "FS-20", "OK", new[] { "a" }) });
            List<LoadoutTemplateRecord> back = LoadoutTemplateCodec.Decode("broken|record;" + good + ";|noairframe||");
            Assert.Single(back);
            Assert.Equal("OK", back[0].Name);
        }

        [Fact]
        public void ARecordWithoutAnIdIsNeverWritten() =>
            Assert.Equal("", LoadoutTemplateCodec.Encode(new[] { new LoadoutTemplateRecord(null, "FS-20", "X", null) }));

        [Fact]
        public void ATemplateWithNoStationsRoundTripsEmpty()
        {
            LoadoutTemplateRecord back = LoadoutTemplateCodec.Decode(
                LoadoutTemplateCodec.Encode(new[] { new LoadoutTemplateRecord("t1", "FS-20", "BARE", null) }))[0];
            Assert.Empty(back.MountKeys);
        }
    }
}
