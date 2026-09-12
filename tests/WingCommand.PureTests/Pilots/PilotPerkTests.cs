using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WingCommand.PureTests
{
    public class PilotPerkTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(119, 0)]
        [InlineData(120, 1)]
        [InlineData(359, 1)]
        [InlineData(360, 2)]
        [InlineData(720, 3)]
        [InlineData(1200, 4)]
        [InlineData(int.MaxValue, 4)]
        public void RankBoundaryAndPerkCount(int xp, int expected)
        {
            var owned = new List<PilotPerk>();
            var rank = PilotPerks.RankFor(xp);
            Assert.Equal(expected, (int)rank);
            PilotPerks.GrantThroughRank(owned, rank, n => n - 1);
            Assert.Equal(expected, owned.Count);
            Assert.Equal(expected, owned.Distinct().Count());
        }

        [Fact]
        public void MultiRankAwardAndRepeatedSynchronizationPreserveEarnedPerks()
        {
            var owned = new List<PilotPerk>();
            PilotPerks.GrantThroughRank(owned, WingRank.Wingman, n => 0);
            Assert.Equal(PilotPerk.Toughness, owned[0]);
            PilotPerks.GrantThroughRank(owned, WingRank.Legend, n => n - 1);
            var before = owned.ToArray();
            PilotPerks.GrantThroughRank(owned, WingRank.Legend, n => throw new Exception("Must not reroll"));
            Assert.Equal(before, owned);
            Assert.True(owned.Distinct().Count() >= 4);
        }

        [Fact]
        public void EveryPerkCanBeTheFirstReward()
        {
            for (int i = 0; i < PilotPerks.Count; i++)
            {
                var owned = new List<PilotPerk>();
                PilotPerks.GrantThroughRank(owned, WingRank.Wingman, n => i);
                Assert.Equal((PilotPerk)i, owned[0]);
            }
        }

        [Fact]
        public void TalentedExpandsAvailableSlots()
        {
            var owned = new List<PilotPerk> { PilotPerk.Talented };
            Assert.Equal(4, PilotPerks.DesiredPerkCount(owned, WingRank.Wingman));
            Assert.Equal(7, PilotPerks.DesiredPerkCount(owned, WingRank.Legend));
            var regular = new List<PilotPerk> { PilotPerk.Toughness };
            Assert.Equal(1, PilotPerks.DesiredPerkCount(regular, WingRank.Wingman));
            Assert.Equal(4, PilotPerks.DesiredPerkCount(regular, WingRank.Legend));
        }

        [Fact]
        public void ExperienceCannotWrapOrDecrease()
        {
            Assert.Equal(int.MaxValue, PilotPerks.AddXp(int.MaxValue - 2, 40));
            Assert.Equal(100, PilotPerks.AddXp(100, -40));
        }

        [Fact]
        public void DamageBenefitsPreserveZeroAndDisabledEffects()
        {
            Assert.Equal(50f, PilotPerks.PilotDamage(100f, true));
            Assert.Equal(100f, PilotPerks.PilotDamage(100f, false));
            Assert.Equal(0f, PilotPerks.PilotDamage(0f, true));
            Assert.Equal(-10f, PilotPerks.PilotDamage(-10f, true));
        }

        [Fact]
        public void CatalogHasTwentyFiveDistinctNamedDescribedPerks()
        {
            Assert.Equal(25, PilotPerks.Count);
            var names = new HashSet<string>();
            for (int i = 0; i < PilotPerks.Count; i++)
            {
                Assert.True(names.Add(PilotPerks.Name((PilotPerk)i)));
                Assert.DoesNotContain("Unknown", PilotPerks.Description((PilotPerk)i));
                Assert.NotEqual("UNKNOWN", PilotPerks.Name((PilotPerk)i));
                Assert.NotEmpty(PilotPerks.IconKey((PilotPerk)i));
            }
        }

        [Fact]
        public void StackedBonusesStayBoundedAndEscapeChancesCombine()
        {
            Assert.Equal(0.6425f, PilotPerks.MissileErrorChance(true, true), 4);
            Assert.Equal(0f, PilotPerks.MissileErrorChance(false, false));
            Assert.Equal(0.60f, PilotPerks.EscapeChance(true));
            Assert.Equal(0f, PilotPerks.EscapeChance(false));
            Assert.Equal(int.MaxValue, PilotPerks.Experience(int.MaxValue, true));
            Assert.Equal(38, PilotPerks.Experience(25, true));
        }

        [Fact]
        public void CombatSkillMultipliersAreConsistent()
        {
            Assert.Equal(1.30f, PilotPerks.GunRangeMultiplier(true));
            Assert.Equal(1.0f, PilotPerks.GunRangeMultiplier(false));
            Assert.Equal(1.40f, PilotPerks.OffBoresightMultiplier(true));
            Assert.Equal(1.0f, PilotPerks.OffBoresightMultiplier(false));
            Assert.True(PilotPerks.IsHeadOn(400f, -0.8f));
            Assert.False(PilotPerks.IsHeadOn(300f, -0.8f));
            Assert.False(PilotPerks.IsHeadOn(400f, 0.5f));
            Assert.Equal(1.25f, PilotPerks.HeadOnRangeMultiplier(true));
            Assert.Equal(1.40f, PilotPerks.HighAltitudeRangeMultiplier(4500f, true));
            Assert.Equal(1.0f, PilotPerks.HighAltitudeRangeMultiplier(3500f, true));
            Assert.Equal(0.70f, PilotPerks.BombFloorScale(true));
            Assert.Equal(1.35f, PilotPerks.BombEnvelopeScale(true));
            Assert.Equal(0.60f, PilotPerks.SalvoDelayScale(true));
            Assert.Equal(1.50f, PilotPerks.SeadPriorityMultiplier(true, true));
            Assert.Equal(1.50f, PilotPerks.ClaimDurationMultiplier(true));
            Assert.Equal(1.30f, PilotPerks.FormationRejoinScale(true));
            Assert.Equal(1.40f, PilotPerks.CombatSpreadScale(true, true));
            Assert.Equal(-25f, PilotPerks.TerrainFloorOffset(true));
            Assert.Equal(1.50f, PilotPerks.DefensiveRollAuthority(true));
            Assert.Equal(1.5f, PilotPerks.EarlyWarningReactionLead(true));
            Assert.Equal(0.60f, PilotPerks.BurnthroughJammingScale(true));
        }
    }
}
