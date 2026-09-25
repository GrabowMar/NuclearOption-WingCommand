using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class ElementGroupsTests
    {
        [Fact]
        public void RowsGroupByElementThenSeat()
        {
            var rows = new[]
            {
                new SnapshotMember { Id = 1, Slot = 0, Element = 1 }, new SnapshotMember { Id = 2, Slot = 1, Element = 0 },
                new SnapshotMember { Id = 3, Slot = 2, Element = 1 }, new SnapshotMember { Id = 4, Slot = 3, Element = 0 },
            };
            var order = new int[4];
            Assert.Equal(4, ElementGroups.Order(rows, 4, order));
            Assert.Equal(new[] { 1, 3, 0, 2 }, order);
        }

        [Fact]
        public void HeadersNameTheElementOnce()
        {
            Assert.Equal("A · 3 · FORM", ElementGroups.Header(0, "A", 3, "FORM"));
            Assert.Equal("B · COBRA · 2 · ORBIT", ElementGroups.Header(1, "COBRA", 2, "ORBIT"));
        }
    }

    public class MemberDetailTests
    {
        [Fact]
        public void DetailWithNothingKnownIsDashes()
        {
            var lines = new List<string>();
            DetailLines.Build(new MemberDetail { Fuel = float.NaN, Ammo = float.NaN, BingoSeconds = float.NaN, Damage = float.NaN, Radar = -1 }, lines);
            Assert.Equal("FUEL —   AMMO —   DMG —", lines[0]);
            Assert.Equal("RADAR —   TGT —", lines[1]);
            Assert.Equal("STORES —", lines[2]);
            Assert.Equal("PILOT —", lines[3]);
        }

        [Fact]
        public void DetailShowsBingoStoresAndPilot()
        {
            var lines = new List<string>();
            var d = new MemberDetail
            {
                Fuel = 0.62f, Ammo = 0.4f, BingoSeconds = 250f, Damage = 0.12f, Radar = 1, Target = "Ibis",
                Callsign = "VIPER", Rank = "Lieutenant", Perks = "Quick Draw, Snapshot",
                Stores = new[] { new StoreLine { Name = "AAM-36", Ammo = 2, Full = 4 }, new StoreLine { Name = "GUN", Ammo = 300, Full = 600 } },
                StoreCount = 2,
            };
            DetailLines.Build(d, lines);
            Assert.Equal("FUEL 62%  B 4:10   AMMO 40%   DMG 12%", lines[0]);
            Assert.Equal("RADAR ON   TGT Ibis", lines[1]);
            Assert.Equal("AAM-36 2/4   GUN 300/600", lines[2]);
            Assert.Equal("VIPER · Lieutenant · Quick Draw, Snapshot", lines[3]);
        }
    }
}
