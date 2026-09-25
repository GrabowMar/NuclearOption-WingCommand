using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class StationLayoutTests
    {
        // A VT-7-like airframe: an unnamed mirrored pair, a two-pylon set, a centreline that a bay store blocks (one way), a bay, and an
        // unnamed nose set — 6 sets, 5 stations, 7 pylons.
        internal static StationLayout Vt7() => new StationLayout(
            new[] { "Left Fuselage Pylon", "Right Fuselage Pylon", "Inner wing pylons", "Centerline", "Bay", "" },
            new[] { "", "", "", "", "", "" },
            new[] { false, true, false, false, false, false },
            new[] { 1, 1, 2, 1, 1, 1 },
            new[] { new int[0], new int[0], new int[0], new[] { 4 }, new int[0], new int[0] });

        private static List<string> Keys(params string[] k) => new List<string>(k);

        [Fact]
        public void AMirroredPairIsOneStation()
        {
            StationLayout l = Vt7();
            Assert.Equal(6, l.Sets);
            Assert.Equal(5, l.Stations);
            Assert.Equal(7, l.Pylons);
            Assert.Equal(0, l.First(0));
            Assert.Equal(2, l.End(0));
            Assert.Equal(0, l.StationOf(1));
            Assert.Equal(4, l.StationOf(5));
            Assert.Equal(2, l.PylonsOf(0));
            Assert.Equal(2, l.PylonsOf(1));
        }

        [Fact]
        public void AnUnnamedPairTakesItsFirstSetsNameWithoutTheSide()
        {
            StationLayout l = Vt7();
            Assert.Equal("Fuselage Pylon", l.Name(0));
            Assert.Equal("Inner wing pylons", l.Name(1));
        }

        [Fact]
        public void ANamedPairUsesItsPairName()
        {
            var l = new StationLayout(new[] { "Left Tip", "Right Tip" }, new[] { "Wingtips", "" }, new[] { false, true }, new[] { 1, 1 },
                new[] { new int[0], new int[0] });
            Assert.Equal("Wingtips", l.Name(0));
        }

        [Fact]
        public void AStationWithoutANameIsNumbered() => Assert.Equal("STATION 5", Vt7().Name(4));

        [Fact]
        public void APickWritesEverySetOfTheStation()
        {
            List<string> keys = Keys(null, null, null, null, null, null);
            Assert.Equal(0, Vt7().Pick(keys, 0, "aim9"));
            Assert.Equal("aim9", keys[0]);
            Assert.Equal("aim9", keys[1]);
            Assert.Equal("aim9", Vt7().KeyOf(keys, 0));
        }

        [Fact]
        public void APickClearsTheStationsItBlocks()
        {
            List<string> keys = Keys(null, null, null, "tank", null, null);
            Assert.Equal(1, Vt7().Pick(keys, 3, "bomb"));
            Assert.Null(keys[3]);
            Assert.Equal("bomb", keys[4]);
        }

        [Fact]
        public void ABlockedStationTakesNoStore()
        {
            StationLayout l = Vt7();
            List<string> keys = Keys(null, null, null, null, "bomb", null);
            Assert.Equal(3, l.BlockedBy(keys, 2));
            Assert.Equal(-1, l.Pick(keys, 2, "tank"));
            Assert.Null(keys[3]);
            keys[4] = null;
            Assert.Equal(-1, l.BlockedBy(keys, 2));
        }

        [Fact]
        public void ClearingAStationEmptiesItsPair()
        {
            List<string> keys = Keys("aim9", "aim9", null, null, null, null);
            Vt7().Clear(keys, 0);
            Assert.Null(keys[0]);
            Assert.Null(keys[1]);
        }

        [Fact]
        public void NormalizingPadsTheKeysAndSeedsASplitPairFromItsFirstSet()
        {
            List<string> keys = Keys("aim9", "aim120", "rocket");
            Vt7().Normalize(keys);
            Assert.Equal(6, keys.Count);
            Assert.Equal("aim9", keys[1]);
            Assert.Null(keys[5]);
        }

        [Fact]
        public void TheLaunchClearsTheLowerOfTwoStoresThatBlockEachOther()
        {
            var l = new StationLayout(new[] { "A", "B" }, new[] { "", "" }, new[] { false, false }, new[] { 1, 1 },
                new[] { new[] { 1 }, new[] { 0 } });
            var cleared = new bool[2];
            Assert.Equal(1, l.WillClear(new[] { true, true }, cleared));
            Assert.True(cleared[0]);
            Assert.False(cleared[1]);
        }

        [Fact]
        public void TheLaunchClearsAOneWayBlockedStore()
        {
            var cleared = new bool[6];
            Assert.Equal(1, Vt7().WillClear(new[] { false, false, false, true, true, false }, cleared));
            Assert.True(cleared[3]);
            Assert.False(cleared[4]);
        }
    }
}
