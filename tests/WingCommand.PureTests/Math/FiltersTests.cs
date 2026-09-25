using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class FiltersTests
    {
        [Fact]
        public void FirstOrderInitialisesOnFirstSampleThenReachesSixtyThreePercentAfterTau()
        {
            var f = new FirstOrder();
            Assert.Equal(2f, f.Update(2f, 1f, 0.01f));
            for (int i = 0; i < 100; i++) f.Update(3f, 1f, 0.01f);
            Assert.Equal(2f + (1f - (float)Math.Exp(-1.0)), f.Value, 3);
        }

        [Fact]
        public void FirstOrderWithZeroTauPassesThrough()
        {
            var f = new FirstOrder();
            f.Update(1f, 0f, 0.01f);
            Assert.Equal(5f, f.Update(5f, 0f, 0.01f));
        }

        [Fact]
        public void LatchNeedsBothThresholdsToChangeState()
        {
            var l = new Latch();
            Assert.False(l.Update(0.9f, 1f, 0.5f));
            Assert.True(l.Update(1f, 1f, 0.5f));
            Assert.True(l.Update(0.6f, 1f, 0.5f));
            Assert.False(l.Update(0.5f, 1f, 0.5f));
        }

        [Fact]
        public void PersistenceRequiresAnUnbrokenCondition()
        {
            var p = new Persistence();
            for (int i = 0; i < 29; i++) Assert.False(p.Update(true, 0.5f, 1f / 60f));
            Assert.False(p.Update(false, 0.5f, 1f / 60f));
            for (int i = 0; i < 29; i++) p.Update(true, 0.5f, 1f / 60f);
            Assert.True(p.Update(true, 0.5f, 1f / 60f));
        }

        [Fact]
        public void SlewMovesAtMostRateTimesDt()
        {
            Assert.Equal(0.1f, Slew.Step(0f, 1f, 1f, 0.1f), 5);
            Assert.Equal(0.5f, Slew.Step(0.4f, 0.5f, 10f, 0.1f), 5);
        }
    }
}
