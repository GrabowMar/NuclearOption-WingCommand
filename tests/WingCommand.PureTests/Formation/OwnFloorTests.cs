using Xunit;

namespace WingCommand.PureTests
{
    public class OwnFloorTests
    {
        private static WingFrame Frame()
        {
            // The wing's floor is the terrain ahead of a leader far away; this member's own look-ahead is the airfield's.
            var f = new WingFrame { FloorY = 1400f };
            f.OwnFloorY[1] = 60f;
            f.HasOwnFloor[1] = true;
            return f;
        }

        [Fact]
        public void AMemberFarFromItsSlotFliesUnderItsOwnTerrainOnly()
        {
            // Day-1 sim: EW-25s just off the runway, 20 km from a leader over the hills, zoomed at 45 deg to clear the leader's
            // terrain at once and stalled.
            Assert.Equal(60f, FormationWing.FloorFor(Frame(), 1, 20000f));
        }

        [Fact]
        public void NearItsSlotItSharesTheWingsFloor() =>
            Assert.Equal(1400f, FormationWing.FloorFor(Frame(), 1, FormationWing.OwnFloorMetres - 100f));

        [Fact]
        public void WithoutAProbeOfItsOwnItKeepsTheWingsFloor() =>
            Assert.Equal(1400f, FormationWing.FloorFor(Frame(), 2, 20000f));
    }
}
