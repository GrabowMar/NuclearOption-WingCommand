using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class PerkCardsTests
    {
        [Fact]
        public void EveryPerkHasAShortLineThatFitsTwoCardLines()
        {
            foreach (PilotPerk p in (PilotPerk[])Enum.GetValues(typeof(PilotPerk)))
            {
                string s = PerkCards.Short(p);
                Assert.False(string.IsNullOrEmpty(s), p.ToString());
                Assert.True(s.Length <= PerkCards.LineChars, p + ": " + s);
                Assert.DoesNotContain("…", s);
            }
        }

        [Fact]
        public void TheUnwiredPerksSayTheyAreNotActiveYet()
        {
            Assert.False(PerkCards.Active(PilotPerk.Marksman));
            Assert.False(PerkCards.Active(PilotPerk.Burnthrough));
            Assert.True(PerkCards.Active(PilotPerk.Toughness));
        }

        [Fact]
        public void OwnedPerksShowInOrderAndTheRestAreLocked()
        {
            var owned = new List<PilotPerk> { PilotPerk.Toughness };
            PerkCard first = PerkCards.For(0, owned, WingRank.Wingman);
            Assert.True(first.Owned);
            Assert.Equal(PilotPerks.Name(PilotPerk.Toughness).ToUpperInvariant(), first.Title);
            PerkCard locked = PerkCards.For(1, owned, WingRank.Wingman);
            Assert.True(locked.Locked);
            Assert.Equal("SLOT 2 · VETERAN", locked.Title);
            Assert.Equal("EARNED AT 360 XP", locked.Line);
        }

        [Fact]
        public void ATalentedPilotHasItsFirstThreeSlotsOpenAtOnce()
        {
            var owned = new List<PilotPerk> { PilotPerk.Talented, PilotPerk.Ghost, PilotPerk.Snapshot };
            Assert.True(PerkCards.For(2, owned, WingRank.Rookie).Owned);
            PerkCard fourth = PerkCards.For(3, owned, WingRank.Rookie);
            Assert.True(fourth.Locked);
            Assert.Equal("SLOT 4 · WINGMAN", fourth.Title);
        }

        [Fact]
        public void MoreThanFourPerksTurnTheLastCardIntoTheRestByName()
        {
            var owned = new List<PilotPerk> { PilotPerk.Talented, PilotPerk.Ghost, PilotPerk.Snapshot, PilotPerk.Toughness, PilotPerk.Commando, PilotPerk.Standoff };
            PerkCard last = PerkCards.For(3, owned, WingRank.Veteran);
            Assert.Equal("+3 MORE", last.Title);
            Assert.True(last.Line.Length <= PerkCards.LineChars);
            Assert.Contains("TOUGHNESS", last.Tip.ToUpperInvariant());
        }

        [Fact]
        public void TheHeadSaysRecordClosedForAKiaAndOffWhenProgressionIsOff()
        {
            Assert.Equal("1 OF 4 · NEXT AT 360 XP", PerkCards.Head(1, WingRank.Wingman, false, false));
            Assert.Equal("RECORD CLOSED", PerkCards.Head(1, WingRank.Wingman, true, false));
            Assert.Equal("OFF IN SETTINGS", PerkCards.Head(1, WingRank.Wingman, false, true));
            Assert.Equal("4 OF 4", PerkCards.Head(4, WingRank.Legend, false, false));
        }
    }
}
