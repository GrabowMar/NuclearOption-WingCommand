using System.Collections.Generic;
using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The template codec is the only part of the loadout rework that touches persistence,
    /// and the only one that has to survive a file a human being can edit. These tests are
    /// mostly about what it does with input it did not write.
    /// </summary>
    public class LoadoutTemplateCodecTests
    {
        private static LoadoutTemplateRecord Record(string id, string airframe, string name,
                                                    params string[] keys) =>
            new LoadoutTemplateRecord(id, airframe, name, keys);

        [Fact]
        public void RoundTripsAnOrdinaryTemplate()
        {
            var original = Record("t1", "vt7", "Strike", "agm65", "agm65", "aim9");

            List<LoadoutTemplateRecord> back =
                LoadoutTemplateCodec.Decode(LoadoutTemplateCodec.Encode(new[] { original }));

            LoadoutTemplateRecord only = Assert.Single(back);
            Assert.Equal("t1", only.Id);
            Assert.Equal("vt7", only.AirframeKey);
            Assert.Equal("Strike", only.Name);
            Assert.Equal(new[] { "agm65", "agm65", "aim9" }, only.MountKeys);
        }

        [Fact]
        public void RoundTripsSeveralTemplatesAcrossAirframes()
        {
            var records = new[]
            {
                Record("t1", "vt7", "Strike", "agm65"),
                Record("t2", "vt7", "Sweep", "aim9", "aim9"),
                Record("t3", "ki90", "Haul", "cargo"),
            };

            List<LoadoutTemplateRecord> back =
                LoadoutTemplateCodec.Decode(LoadoutTemplateCodec.Encode(records));

            Assert.Equal(3, back.Count);
            Assert.Equal(new[] { "t1", "t2", "t3" }, back.ConvertAll(r => r.Id));
            Assert.Equal("ki90", back[2].AirframeKey);
        }

        /// <summary>
        /// An empty pylon is a real choice — a station the player deliberately left clean —
        /// so it has to survive the trip as a gap rather than collapsing the list.
        /// </summary>
        [Fact]
        public void PreservesEmptyPylonsIncludingTrailingOnes()
        {
            var original = Record("t1", "vt7", "Light", "aim9", null, "", null);

            LoadoutTemplateRecord back =
                Assert.Single(LoadoutTemplateCodec.Decode(
                    LoadoutTemplateCodec.Encode(new[] { original })));

            Assert.Equal(4, back.MountKeys.Count);
            Assert.Equal("aim9", back.MountKeys[0]);
            Assert.Null(back.MountKeys[1]);
            Assert.Null(back.MountKeys[2]);
            Assert.Null(back.MountKeys[3]);
        }

        [Fact]
        public void TemplateWithNoPylonsRoundTripsAsEmptyRatherThanOneBlankPylon()
        {
            LoadoutTemplateRecord back =
                Assert.Single(LoadoutTemplateCodec.Decode(
                    LoadoutTemplateCodec.Encode(new[] { Record("t1", "vt7", "Bare") })));

            Assert.Empty(back.MountKeys);
        }

        /// <summary>Every delimiter, in the field a player can actually type into.</summary>
        [Theory]
        [InlineData("CAS, low")]
        [InlineData("A|B")]
        [InlineData("one;two")]
        [InlineData("100% loaded")]
        [InlineData("%3B not a semicolon")]
        [InlineData(";|,%")]
        public void SurvivesDelimitersInNames(string name)
        {
            LoadoutTemplateRecord back =
                Assert.Single(LoadoutTemplateCodec.Decode(
                    LoadoutTemplateCodec.Encode(new[] { Record("t1", "vt7", name, "aim9") })));

            Assert.Equal(name, back.Name);
            Assert.Equal(new[] { "aim9" }, back.MountKeys);
        }

        [Fact]
        public void SurvivesDelimitersInKeysAndAirframeIdentifiers()
        {
            var original = Record("t;1", "vt|7", "Odd", "a,b", "c;d");

            LoadoutTemplateRecord back =
                Assert.Single(LoadoutTemplateCodec.Decode(
                    LoadoutTemplateCodec.Encode(new[] { original })));

            Assert.Equal("t;1", back.Id);
            Assert.Equal("vt|7", back.AirframeKey);
            Assert.Equal(new[] { "a,b", "c;d" }, back.MountKeys);
        }

        [Fact]
        public void DecodesNothingFromNothing()
        {
            Assert.Empty(LoadoutTemplateCodec.Decode(null));
            Assert.Empty(LoadoutTemplateCodec.Decode(""));
            Assert.Equal("", LoadoutTemplateCodec.Encode(null));
        }

        /// <summary>
        /// The case that matters most: a config file someone has edited by hand, or one
        /// truncated by a crash mid-write. One bad record must cost that record only.
        /// </summary>
        [Fact]
        public void DropsMalformedRecordsAndKeepsTheRest()
        {
            string good = LoadoutTemplateCodec.Encode(new[]
            {
                Record("t1", "vt7", "First", "aim9"),
                Record("t2", "vt7", "Second", "agm65"),
            });

            // A record with too few fields, wedged between two sound ones.
            string damaged = good.Replace("vt7|t2|Second|", "garbage");

            List<LoadoutTemplateRecord> back = LoadoutTemplateCodec.Decode(damaged);

            LoadoutTemplateRecord only = Assert.Single(back);
            Assert.Equal("t1", only.Id);
        }

        [Theory]
        [InlineData("vt7")]
        [InlineData("vt7|t1")]
        [InlineData("vt7|t1|Name")]
        [InlineData("|t1|Name|aim9")]
        [InlineData("vt7||Name|aim9")]
        [InlineData(";;;")]
        [InlineData("|||")]
        public void RefusesRecordsThatCannotBeIdentified(string encoded)
        {
            Assert.Empty(LoadoutTemplateCodec.Decode(encoded));
        }

        [Fact]
        public void TruncatedStringDecodesToWhateverSurvivedIntact()
        {
            string full = LoadoutTemplateCodec.Encode(new[]
            {
                Record("t1", "vt7", "First", "aim9"),
                Record("t2", "vt7", "Second", "agm65"),
            });

            List<LoadoutTemplateRecord> back =
                LoadoutTemplateCodec.Decode(full.Substring(0, full.Length - 6));

            // The first record is complete and must come back; the second is cut short and
            // may or may not still have its four fields. Neither may throw.
            Assert.NotEmpty(back);
            Assert.Equal("t1", back[0].Id);
        }

        [Fact]
        public void EncodeSkipsRecordsWithNoIdentity()
        {
            string encoded = LoadoutTemplateCodec.Encode(new[]
            {
                null,
                new LoadoutTemplateRecord(null, "vt7", "No id", new[] { "aim9" }),
                new LoadoutTemplateRecord("t2", null, "No airframe", new[] { "aim9" }),
                Record("t3", "vt7", "Fine", "aim9"),
            });

            LoadoutTemplateRecord only = Assert.Single(LoadoutTemplateCodec.Decode(encoded));
            Assert.Equal("t3", only.Id);
        }

        [Fact]
        public void EncodingIsStableSoTheConfigIsNotRewrittenForNoReason()
        {
            var records = new[] { Record("t1", "vt7", "Strike", "agm65", null, "aim9") };

            string once = LoadoutTemplateCodec.Encode(records);
            string twice = LoadoutTemplateCodec.Encode(LoadoutTemplateCodec.Decode(once));

            Assert.Equal(once, twice);
        }

        // ------------------------------------------------------------------ record

        [Fact]
        public void SetKeyAtGrowsThePylonListWithGaps()
        {
            var record = new LoadoutTemplateRecord("t1", "vt7", "Sparse", null);

            record.SetKeyAt(3, "aim9");

            Assert.Equal(4, record.MountKeys.Count);
            Assert.Null(record.MountKeys[0]);
            Assert.Equal("aim9", record.MountKeys[3]);
        }

        /// <summary>An airframe that gained a station must read as empty there, not throw.</summary>
        [Fact]
        public void KeyAtIsNullPastTheEndAndBelowZero()
        {
            var record = Record("t1", "vt7", "Short", "aim9");

            Assert.Equal("aim9", record.KeyAt(0));
            Assert.Null(record.KeyAt(7));
            Assert.Null(record.KeyAt(-1));
        }

        [Fact]
        public void CopyTakesTheStoresButNotTheIdentity()
        {
            var source = Record("t1", "vt7", "Strike", "agm65", "aim9");

            LoadoutTemplateRecord copy = source.Copy("t2", "Strike COPY");

            Assert.Equal("t2", copy.Id);
            Assert.Equal("Strike COPY", copy.Name);
            Assert.Equal("vt7", copy.AirframeKey);
            Assert.Equal(source.MountKeys, copy.MountKeys);

            // The lists must not be shared, or editing one template edits the other.
            copy.SetKeyAt(0, "changed");
            Assert.Equal("agm65", source.MountKeys[0]);
        }
    }
}
