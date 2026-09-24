using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class PilotSkillTests
    {
        [Fact]
        public void ARookieFliesAtTheBaseSkill()
        {
            PilotSkill.For(WingRank.Rookie, new List<PilotPerk>(), 1f, out float precision, out float aggression);
            Assert.Equal(PilotSkill.BasePrecision, precision, 3);
            Assert.Equal(PilotSkill.BaseAggression, aggression, 3);
        }

        [Fact]
        public void RankRaisesPrecisionAndAggressionUpToTheirCaps()
        {
            PilotSkill.For(WingRank.Veteran, new List<PilotPerk>(), 1f, out float vp, out float va);
            PilotSkill.For(WingRank.Legend, new List<PilotPerk> { PilotPerk.WingmanInstinct, PilotPerk.EnergyFighter, PilotPerk.ApexHunter },
                2f, out float lp, out float la);
            Assert.True(vp > PilotSkill.BasePrecision && va > PilotSkill.BaseAggression);
            Assert.Equal(PilotSkill.MaxPrecision, lp, 3);
            Assert.Equal(PilotSkill.MaxAggression, la, 3);
        }

        [Fact]
        public void RankEffectZeroLeavesEveryPilotAtTheBase()
        {
            PilotSkill.For(WingRank.Legend, new List<PilotPerk>(), 0f, out float precision, out float aggression);
            Assert.Equal(PilotSkill.BasePrecision, precision, 3);
            Assert.Equal(PilotSkill.BaseAggression, aggression, 3);
        }

        [Fact]
        public void CombatPerksTiltAggressionAndFormationPerksPrecision()
        {
            PilotSkill.For(WingRank.Wingman, new List<PilotPerk>(), 1f, out float p0, out float a0);
            PilotSkill.For(WingRank.Wingman, new List<PilotPerk> { PilotPerk.WingmanInstinct }, 1f, out float p1, out float a1);
            PilotSkill.For(WingRank.Wingman, new List<PilotPerk> { PilotPerk.EnergyFighter }, 1f, out float p2, out float a2);
            Assert.True(p1 > p0 && a1 == a0);
            Assert.True(a2 > a0 && p2 == p0);
        }
    }
}
