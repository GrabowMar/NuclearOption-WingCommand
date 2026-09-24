using Xunit;

namespace WingCommand.PureTests
{
    public class WinchesterLatchTests
    {
        [Fact]
        public void DryInFormationCallsOnce()
        {
            var l = new WinchesterLatch();
            Assert.True(l.Update(true, true));
            Assert.False(l.Update(true, true));
        }

        [Fact]
        public void DryWhileFightingStaysQuietAndSoDoesTheRejoin()
        {
            // The fight's own Winchester disengage speaks; the latch keeps the rejoin from calling it twice.
            var l = new WinchesterLatch();
            Assert.False(l.Update(true, false));
            Assert.False(l.Update(true, true));
        }

        [Fact]
        public void LatchedByTheDisengageStaysQuiet()
        {
            var l = new WinchesterLatch { Latched = true };
            Assert.False(l.Update(true, true));
        }

        [Fact]
        public void RearmedCallsAgainWhenDryAgain()
        {
            var l = new WinchesterLatch();
            l.Update(true, true);
            Assert.False(l.Update(false, true));
            Assert.True(l.Update(true, true));
        }
    }
}
