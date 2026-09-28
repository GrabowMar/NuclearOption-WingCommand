using Xunit;

namespace WingCommand.PureTests
{
    public class FlightListTests
    {
        private static SnapshotMember M(uint id, int slot, int element) => new SnapshotMember { Id = id, Slot = (byte)slot, Element = (byte)element };

        [Fact]
        public void EveryElementGetsAHeaderThenItsMembers()
        {
            var rows = new[] { M(13, 2, 1), M(11, 0, 0), M(12, 1, 1) };
            var into = new FlightLine[16];
            int n = FlightList.Page(rows, 3, new int[8], 636f, 0, into, out int pages);
            Assert.Equal(1, pages);
            Assert.Equal(5, n);
            Assert.True(into[0].Header); Assert.Equal(0, into[0].Element);
            Assert.False(into[1].Header); Assert.Equal(11u, rows[into[1].Row].Id);
            Assert.True(into[2].Header); Assert.Equal(1, into[2].Element);
            Assert.Equal(12u, rows[into[3].Row].Id);
            Assert.Equal(13u, rows[into[4].Row].Id);
        }

        [Fact]
        public void ShortCapacityPagesAndRepeatsTheHeader()
        {
            // 3 rows need 112 px; at 110 the pager takes 20 and leaves 90 = header 22 + 2 rows 60.
            var rows = new[] { M(1, 0, 0), M(2, 1, 0), M(3, 2, 0) };
            var into = new FlightLine[16];
            int n = FlightList.Page(rows, 3, new int[8], 110f, 1, into, out int pages);
            Assert.Equal(2, pages);
            Assert.Equal(2, n);
            Assert.True(into[0].Header);
            Assert.Equal(3u, rows[into[1].Row].Id);
        }

        [Fact]
        public void AHeaderIsNeverLastOnAPage()
        {
            // A: 2 rows, B: 1 row need 134 px; at 116 the pager leaves 96: A's header + 2 rows (82), and B's header + row (52)
            // would overflow - B's header goes to page 1 with its row, never alone at the foot of page 0.
            var rows = new[] { M(1, 0, 0), M(2, 1, 0), M(3, 2, 1) };
            var into = new FlightLine[16];
            int n = FlightList.Page(rows, 3, new int[8], 116f, 0, into, out int pages);
            Assert.Equal(2, pages);
            Assert.False(into[n - 1].Header);
        }

        [Fact]
        public void APageBeyondTheLastIsClampedAndEmptyWingHasOnePage()
        {
            var into = new FlightLine[16];
            Assert.Equal(0, FlightList.Page(new SnapshotMember[0], 0, new int[8], 300f, 3, into, out int pages));
            Assert.Equal(1, pages);
        }
    }
}
