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
            Assert.Equal(PilotPerk.Luck, owned[0]);
            PilotPerks.GrantThroughRank(owned, WingRank.Legend, n => n - 1);
            var before = owned.ToArray();
            PilotPerks.GrantThroughRank(owned, WingRank.Legend, n => throw new Exception("Must not reroll"));
            Assert.Equal(before, owned);
            Assert.Equal(4, owned.Distinct().Count());
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
        public void ExperienceCannotWrapOrDecrease()
        {
            Assert.Equal(int.MaxValue, PilotPerks.AddXp(int.MaxValue - 2, 40));
            Assert.Equal(100, PilotPerks.AddXp(100, -40));
        }

        [Fact]
        public void FuelAndDamageBenefitsPreserveZeroAndDisabledEffects()
        {
            Assert.Equal(70f, PilotPerks.FuelUse(100f, true));
            Assert.Equal(100f, PilotPerks.FuelUse(100f, false));
            Assert.Equal(0f, PilotPerks.FuelUse(0f, true));
            Assert.Equal(-10f, PilotPerks.FuelUse(-10f, true));
            Assert.Equal(65f, PilotPerks.PilotDamage(100f, true));
            Assert.Equal(100f, PilotPerks.PilotDamage(100f, false));
            Assert.Equal(0f, PilotPerks.PilotDamage(0f, true));
        }

        [Fact]
        public void GToleranceReducesOnlyDamageAboveNativeThreshold()
        {
            Assert.Equal(300f, PilotPerks.GLoad(300f, true));
            Assert.Equal(400f, PilotPerks.GLoad(400f, true));
            Assert.Equal(525f, PilotPerks.GLoad(900f, true));
            Assert.Equal(900f, PilotPerks.GLoad(900f, false));
        }

        [Fact]
        public void CatalogHasTwentyFourDistinctNamedDescribedPerks()
        {
            Assert.Equal(24, PilotPerks.Count);
            var names = new HashSet<string>();
            for (int i = 0; i < PilotPerks.Count; i++)
            {
                Assert.True(names.Add(PilotPerks.Name((PilotPerk)i)));
                Assert.DoesNotContain("Unknown", PilotPerks.Description((PilotPerk)i));
                Assert.NotEqual("UNKNOWN", PilotPerks.Name((PilotPerk)i));
            }
        }

        [Fact]
        public void StackedBonusesStayBoundedAndEscapeChancesCombine()
        {
            Assert.Equal(0.8f, PilotPerks.MissileErrorChance(true, true, true, true, true));
            Assert.Equal(0f, PilotPerks.MissileErrorChance(false, false, false, false, false));
            Assert.InRange(PilotPerks.FuelMultiplier(true, true, true), 0.25f, 0.253f);
            Assert.Equal(1f, PilotPerks.FuelMultiplier(false, false, false));
            Assert.Equal(0.675f, PilotPerks.EscapeChance(true, true));
            Assert.Equal(0.35f, PilotPerks.EscapeChance(false, true));
            Assert.Equal(0.5f, PilotPerks.EscapeChance(true, false));
            Assert.Equal(0f, PilotPerks.EscapeChance(false, false));
            Assert.Equal(int.MaxValue, PilotPerks.Experience(int.MaxValue, true));
            Assert.Equal(38, PilotPerks.Experience(25, true));
        }
    }
}
