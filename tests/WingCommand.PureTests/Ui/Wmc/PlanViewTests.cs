using Xunit;

namespace WingCommand.PureTests
{
    public class PlanViewTests
    {
        [Fact]
        public void TheLeaderSitsAThirdDownTheMiddle()
        {
            PlanView.Fit(new[] { new SlotDef(1f, 1f, 0f) }, 100f, 180f, out float mpp);
            var (x, y) = PlanView.Point(0f, 0f, mpp, 180f);
            Assert.Equal(90f, x, 2);
            Assert.Equal(60f, y, 2);
        }

        [Fact]
        public void TheFarthestSlotFitsInsideTheMargin()
        {
            // Echelon right at 100 m: the last slot is 300 m right and 300 m aft; right room is 90 - 12 = 78 px.
            var slots = new[] { new SlotDef(1f, 1f, 0f), new SlotDef(2f, 2f, 0f), new SlotDef(3f, 3f, 0f) };
            PlanView.Fit(slots, 100f, 180f, out float mpp);
            var (x, y) = PlanView.Point(300f, 300f, mpp, 180f);
            Assert.Equal(168f, x, 1);
            Assert.True(y <= 180f - 12f + 0.01f);
        }

        [Fact]
        public void LiveDotsBeyondTheEdgeSitOnIt()
        {
            PlanView.Fit(new[] { new SlotDef(1f, 1f, 0f) }, 100f, 180f, out float mpp);
            var (x, y) = PlanView.Point(100000f, 100000f, mpp, 180f);
            Assert.Equal(176f, x, 2);
            Assert.Equal(176f, y, 2);
            var (lx, ly) = PlanView.Point(-100000f, -100000f, mpp, 180f);
            Assert.Equal(4f, lx, 2);
            Assert.Equal(4f, ly, 2);
        }

        [Fact]
        public void ATinyShapeIsNotBlownUp()
        {
            // One slot 1 spacing right at 40 m would fill the square; the floor keeps a spacing at 40 px at most.
            PlanView.Fit(new[] { new SlotDef(1f, 0f, 0f) }, 40f, 180f, out float mpp);
            Assert.True(mpp >= 1f);
        }
    }
}
