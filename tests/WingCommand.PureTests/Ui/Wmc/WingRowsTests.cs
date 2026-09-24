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
