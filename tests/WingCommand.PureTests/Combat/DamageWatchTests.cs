using Xunit;

namespace WingCommand.PureTests
{
    public class DamageWatchTests
    {
        [Fact]
        public void FullHealthOrNoPartsIsWholeAndQuiet()
        {
            var w = new DamageWatch();
            Assert.False(w.Update(10f, 10, false));
            Assert.Equal(1f, w.Hull, 3);
            Assert.False(new DamageWatch().Update(0f, 0, false));
            Assert.Equal(1f, new DamageWatch().Hull, 3);
        }

        [Fact]
        public void DamageEdgesOnceAndClearsOnlyFivePointsHigher()
        {
            var w = new DamageWatch();
            w.Update(10f, 10, false);
            Assert.True(w.Update(8.5f, 10, false));   // hull 0.85: the edge
            Assert.True(w.Damaged);
            Assert.False(w.Update(8.4f, 10, false));   // still damaged: quiet
            Assert.False(w.Update(8.8f, 10, false));
            Assert.True(w.Damaged);                    // 0.88 < 0.90
            Assert.False(w.Update(9.0f, 10, false));
            Assert.False(w.Damaged);                   // cleared at 0.90
            Assert.True(w.Update(8.0f, 10, false));    // a later drop edges again
        }

        [Fact]
        public void ADetachedPartAloneIsDamage()
        {
            var w = new DamageWatch();
            Assert.True(w.Update(10f, 10, true));
            Assert.True(w.Damaged);
        }

        [Fact]
        public void APartListThatShrinksReadsAgainstTheBaseline()
        {
            var w = new DamageWatch();
            w.Update(10f, 10, false);
            // Two parts gone off the list (deregistered): 8 healthy parts of the 10 known.
            w.Update(8f, 8, false);
            Assert.Equal(0.8f, w.Hull, 3);
            Assert.True(w.Damaged);
        }
    }
}
