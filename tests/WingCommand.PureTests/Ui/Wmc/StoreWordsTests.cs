using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class StoreWordsTests
    {
        [Fact]
        public void APopupDetailSaysTheRoleAndRoundsOrWhyNot()
        {
            Assert.Equal("A-A ×4", StoreWords.Detail(StoreVerdict.Ok, StoreKind.AirToAir, 4, 0));
            Assert.Equal("CARGO", StoreWords.Detail(StoreVerdict.Ok, StoreKind.Cargo, 0, 0));
            Assert.Equal("RESTRICTED", StoreWords.Detail(StoreVerdict.Restricted, StoreKind.Bomb, 2, 0));
            Assert.Equal("NOT YET", StoreWords.Detail(StoreVerdict.NuclearNotYet, StoreKind.Bomb, 1, 0));
            Assert.Equal("RANK 4", StoreWords.Detail(StoreVerdict.NuclearRank, StoreKind.Bomb, 1, 4));
        }

        [Fact]
        public void EveryStoreWordFitsItsColumnWithoutAnEllipsis()
        {
            foreach (StoreVerdict v in (StoreVerdict[])Enum.GetValues(typeof(StoreVerdict)))
                foreach (StoreKind k in (StoreKind[])Enum.GetValues(typeof(StoreKind)))
                {
                    string d = StoreWords.Detail(v, k, 99, 12);
                    Assert.True(d.Length <= StoreWords.DetailChars, d);
                    Assert.DoesNotContain("…", d);
                    Assert.False(string.IsNullOrEmpty(StoreWords.Why(v, 12)) && v != StoreVerdict.Ok, v.ToString());
                }
        }

        [Fact]
        public void AFittedStoreThatWillNotFlySaysSoAfterItsName()
        {
            Assert.Equal("Mk 82 ×4 · NOT YET", StoreWords.Row("Mk 82 ×4", StoreVerdict.NuclearNotYet, 0));
            Assert.Equal("AIM-9 ×2", StoreWords.Row("AIM-9 ×2", StoreVerdict.Ok, 0));
            Assert.True(StoreWords.Row(new string('A', 90), StoreVerdict.Restricted, 0).Length <= LoadoutWords.StoreChars);
            Assert.Equal("UNKNOWN STORE", StoreWords.Row(null, StoreVerdict.Missing, 0));
        }

        [Fact]
        public void ABlockedStationNamesWhatBlocksIt()
        {
            Assert.Equal("BLOCKED BY BAY", StoreWords.BlockedBy("Bay"));
            Assert.True(StoreWords.BlockedBy(new string('B', 80)).Length <= LoadoutWords.StoreChars);
        }
    }
}
