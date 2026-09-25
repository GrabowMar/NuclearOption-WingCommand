using Xunit;

namespace WingCommand.PureTests
{
    public class WingRowsTests
    {
        private static SnapshotMember M(int slot, MemberDuty duty, BehaviourId b = BehaviourId.StationKeep, float fuel = 1f,
            float ammo = 1f, bool behind = false, bool bingo = false, bool joker = false, bool winchester = false) =>
            SnapshotBuilder.Member((uint)(100 + slot), slot, (byte)b, duty, fuel, ammo, behind, bingo, joker, winchester);

        [Theory]
        [InlineData((int)MemberDuty.Engaged, "FIGHT")]
        [InlineData((int)MemberDuty.Recovering, "RTB")]
        [InlineData((int)MemberDuty.Defending, "DEFEND")]
        [InlineData((int)MemberDuty.Grounded, "GROUND")]
        [InlineData((int)MemberDuty.Settled, "LANDED")]
        [InlineData((int)MemberDuty.Formation, "SLOT")]
        public void StateWordFollowsTheDuty(int duty, string word) => Assert.Equal(word, WingRows.State(M(0, (MemberDuty)duty)));

        [Fact]
        public void InFormationTheStateIsTheHudPhase()
        {
            Assert.Equal("JOIN", WingRows.State(M(0, MemberDuty.Formation, BehaviourId.Rejoin)));
            Assert.Equal("BEHIND", WingRows.State(M(0, MemberDuty.Formation, BehaviourId.Rejoin, behind: true)));
            Assert.Equal("HOLD", WingRows.State(M(0, MemberDuty.Formation, BehaviourId.HoldOverhead)));
        }

        [Fact]
        public void FlagsPutFuelBeforeAmmoAndBingoOverJoker()
        {
            Assert.Equal("", WingRows.Flags(M(0, MemberDuty.Formation).Flags));
            Assert.Equal("JOKER", WingRows.Flags(M(0, MemberDuty.Formation, joker: true).Flags));
            Assert.Equal("BINGO", WingRows.Flags(M(0, MemberDuty.Formation, bingo: true, joker: true).Flags));
            Assert.Equal("WINCHESTER", WingRows.Flags(M(0, MemberDuty.Formation, winchester: true).Flags));
            Assert.Equal("BINGO WINCHESTER", WingRows.Flags(M(0, MemberDuty.Formation, bingo: true, winchester: true).Flags));
        }

        [Fact]
        public void NumberCountsFromTwoAsTheHud()
        {
            Assert.Equal("#2", WingRows.Number(0));
            Assert.Equal("#8", WingRows.Number(6));
        }

        [Fact]
        public void SummaryTakesTheLowestFuelAndAmmo()
        {
            var rows = new[] { M(0, MemberDuty.Formation, fuel: 1f, ammo: 0.5f), M(1, MemberDuty.Formation, fuel: 0.2f, ammo: 1f) };
            WingSummary s = WingRows.Summary(rows, 2);
            Assert.Equal(2, s.Count);
            Assert.Equal(0.2f, s.MinFuel, 2);
            Assert.Equal(0.5f, s.MinAmmo, 2);
            Assert.False(s.Bingo);
            rows[1] = M(1, MemberDuty.Formation, fuel: 0.1f, bingo: true);
            Assert.True(WingRows.Summary(rows, 2).Bingo);
        }

        [Fact]
        public void SummaryOfNobodyIsUnknown()
        {
            WingSummary s = WingRows.Summary(new SnapshotMember[8], 0);
            Assert.Equal(0, s.Count);
            Assert.True(float.IsNaN(s.MinFuel));
            Assert.True(float.IsNaN(s.MinAmmo));
        }

        [Fact]
        public void BarIsZeroForUnknownAndClamped()
        {
            // Review M7b-1 I1: an empty wing's NaN summary must not reach a fill's geometry.
            Assert.Equal(0f, WingRows.Bar(float.NaN));
            Assert.Equal(1f, WingRows.Bar(1.5f));
            Assert.Equal(0f, WingRows.Bar(-1f));
            Assert.Equal(0.4f, WingRows.Bar(0.4f), 3);
        }

        [Fact]
        public void IndexOfFollowsTheAircraftWhenSlotsRenumber()
        {
            // Review M7b-1 I2: the selection is an aircraft, not a slot.
            var rows = new[] { M(0, MemberDuty.Formation), M(1, MemberDuty.Formation), M(2, MemberDuty.Formation) };
            uint third = rows[2].Id;
            Assert.Equal(2, WingRows.IndexOf(rows, 3, third));
            rows[0] = rows[2];
            rows[0].Slot = 0;   // #2 died: the old #4 now flies slot 0
            Assert.Equal(0, WingRows.IndexOf(rows, 1, third));
            Assert.Equal(-1, WingRows.IndexOf(rows, 1, rows[1].Id + 99u));
            Assert.Equal(-1, WingRows.IndexOf(rows, 3, 0u));
        }

        [Fact]
        public void TextHelpersUseInvariantUnitsAndADashForUnknown()
        {
            Assert.Equal("1:05", WmcText.Clock(65.4f));
            Assert.Equal("62:00", WmcText.Clock(3720f));
            Assert.Equal("0:00", WmcText.Clock(-3f));
            Assert.Equal("12.4 km", WmcText.Km(12440f));
            Assert.Equal("38%", WmcText.Percent(0.384f));
            Assert.Equal("—", WmcText.Percent(float.NaN));
            Assert.Equal("—", WmcText.Clock(float.NaN));
        }
    }
}
