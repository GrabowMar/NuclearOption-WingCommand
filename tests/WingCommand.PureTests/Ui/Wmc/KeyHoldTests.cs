using Xunit;

namespace WingCommand.PureTests
{
    public class KeyHoldTests
    {
        [Fact]
        public void HeldWhileAnyOwnerHoldsAndOneFrameAfterTheLastLetsGo()
        {
            // Critic §14.6: the text field and an armed map order share one hold, so neither restores the pause key under the other.
            var h = new KeyHold();
            Assert.False(h.Held(10));
            h.Set(KeyHold.Map, true, 10);
            h.Set(KeyHold.Field, true, 11);
            h.Set(KeyHold.Map, false, 12);
            Assert.True(h.Held(12));
            Assert.True(h.Held(20));
            h.Set(KeyHold.Field, false, 21);
            // The Esc that ended it must not pause this frame: held through frame 21, free from 22.
            Assert.True(h.Held(21));
            Assert.False(h.Held(22));
        }

        [Fact]
        public void ANewHoldCancelsAPendingRelease()
        {
            var h = new KeyHold();
            h.Set(KeyHold.Map, true, 1);
            h.Set(KeyHold.Map, false, 5);
            h.Set(KeyHold.Field, true, 5);
            Assert.True(h.Held(9));
        }

        [Fact]
        public void ClearDropsEveryHolderAndAPendingRelease()
        {
            // The panel going away with a release pending must not leave the key held (no tick runs after it).
            var h = new KeyHold();
            h.Set(KeyHold.Map, true, 1);
            h.Set(KeyHold.Map, false, 2);
            Assert.True(h.Held(2));
            h.Clear();
            Assert.False(h.Held(2));
        }

        [Fact]
        public void ReleasingNowFreesTheKeyThisFrameAndReleasingTwiceIsHarmless()
        {
            var h = new KeyHold();
            h.Set(KeyHold.Field, true, 1);
            h.Set(KeyHold.Field, false, 3, now: true);
            Assert.False(h.Held(3));
            h.Set(KeyHold.Field, false, 4);
            Assert.False(h.Held(4));
            h.Set(KeyHold.Map, false, 4);
            Assert.False(h.Held(5));
        }
    }
}
