using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>A3: attack orders per element (spec WMC rebuild §AI R8): each order claims its members, the newest claim on a member
    /// wins, and a set left with nobody ends — so element B's ATTACK no longer takes element A's targets away.</summary>
    public class AttackClaimsTests
    {
        [Fact]
        public void TwoElementsAttackTheirOwnTargetsSideBySide()
        {
            var c = new AttackClaims();
            int a = c.Claim(new uint[] { 1, 2 });
            int b = c.Claim(new uint[] { 3, 4 });
            Assert.NotEqual(a, b);
            Assert.True(c.Active(a));
            Assert.True(c.Active(b));
            Assert.Equal(a, c.SetOf(2));
            Assert.Equal(b, c.SetOf(3));
            Assert.Equal(-1, c.SetOf(9));
        }

        [Fact]
        public void TheNewestClaimOnAMemberWinsAndAnEmptiedSetEnds()
        {
            var c = new AttackClaims();
            int a = c.Claim(new uint[] { 1, 2 });
            int b = c.Claim(new uint[] { 3, 4 });
            int wing = c.Claim(new uint[] { 1, 2, 3, 4 });
            int active = 0;
            for (int s = 0; s < AttackClaims.MaxSets; s++) active += c.Active(s) ? 1 : 0;
            Assert.Equal(1, active);                        // A's and B's sets ended; the wing's may reuse either
            Assert.True(c.Active(wing));
            Assert.Equal(wing, c.SetOf(1));
            Assert.Equal(wing, c.SetOf(3));
            Assert.Equal(4, c.Count(wing));
            int split = c.Claim(new uint[] { 4 });
            Assert.Equal(3, c.Count(wing));
            Assert.Equal(split, c.SetOf(4));
        }

        [Fact]
        public void ReleasingMembersTakesThemOutOfEverySet()
        {
            var c = new AttackClaims();
            int a = c.Claim(new uint[] { 1, 2 });
            c.Release(new uint[] { 1 });
            Assert.Equal(-1, c.SetOf(1));
            Assert.Equal(1, c.Count(a));
            c.Release(new uint[] { 2 });
            Assert.False(c.Active(a));
            c.Claim(new uint[] { 5 });
            c.ClearAll();
            Assert.Equal(-1, c.SetOf(5));
        }

        [Fact]
        public void AClaimOfNobodyTakesNoSetAndAFullBookReusesTheOldest()
        {
            var c = new AttackClaims();
            Assert.Equal(-1, c.Claim(new uint[0]));
            int first = c.Claim(new uint[] { 1 });
            for (uint id = 2; id <= AttackClaims.MaxSets; id++) c.Claim(new[] { id });
            int next = c.Claim(new uint[] { 99 });
            Assert.Equal(first, next);
            Assert.Equal(-1, c.SetOf(1));
            Assert.Equal(next, c.SetOf(99));
        }
    }
}
