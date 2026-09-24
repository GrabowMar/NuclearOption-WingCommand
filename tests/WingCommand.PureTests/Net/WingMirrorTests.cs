using Xunit;

namespace WingCommand.PureTests
{
    public class WingMirrorTests
    {
        private static WcSnapshot Snap(uint tick, uint owner, params uint[] ids)
        {
            var members = new SnapshotMember[ids.Length];
            for (int i = 0; i < ids.Length; i++) members[i] = new SnapshotMember { Id = ids[i], Slot = (byte)i };
            return new WcSnapshot { Tick = tick, Owner = owner, Members = members };
        }

        [Theory]
        [InlineData(0.5f, 128)]
        [InlineData(1f, 255)]
        [InlineData(0f, 0)]
        [InlineData(-1f, 0)]
        [InlineData(2f, 255)]
        [InlineData(float.NaN, 0)]
        [InlineData(float.PositiveInfinity, 255)]   // review M6c I3: (int) of a huge float wraps to int.MinValue
        [InlineData(1e10f, 255)]
        public void FractionsMapIntoAByteWithoutWrapping(float fraction, int expected) =>
            Assert.Equal((byte)expected, SnapshotBuilder.Fraction(fraction));

        [Fact]
        public void TheDutyAndFlagsDescribeTheMember()
        {
            SnapshotMember m = SnapshotBuilder.Member(42u, 1, (byte)BehaviourId.StationKeep, MemberDuty.Engaged, 0.3f, 1f,
                fallingBehind: true, bingo: false, joker: true, winchester: false);
            Assert.Equal(42u, m.Id);
            Assert.Equal((byte)MemberDuty.Engaged, m.Duty);
            Assert.Equal((byte)(SnapshotFlags.FallingBehind | SnapshotFlags.Joker), m.Flags);
            Assert.Equal(SnapshotBuilder.Fraction(0.3f), m.Fuel);
        }

        [Fact]
        public void TheMirrorKeepsTheNewestSnapshotOfItsOwner()
        {
            var mirror = new WingMirror(7u);
            Assert.True(mirror.Apply(Snap(10, 7, 1, 2), 0f));
            Assert.Equal(2, mirror.Count);
            Assert.False(mirror.Apply(Snap(9, 7, 1), 0.1f), "an older snapshot (reordered) is ignored");
            Assert.False(mirror.Apply(Snap(10, 7, 1), 0.1f), "a repeat is ignored");
            Assert.False(mirror.Apply(Snap(11, 8, 1), 0.1f), "another player's wing is ignored");
            Assert.Equal(2, mirror.Count);
            Assert.True(mirror.Apply(Snap(12, 7, 2), 0.5f));
            Assert.Equal(1, mirror.Count);
            Assert.Equal(2u, mirror[0].Id);
        }

        [Fact]
        public void TheMirrorIsStaleWithoutSnapshots()
        {
            var mirror = new WingMirror(7u);
            Assert.True(mirror.Stale(0f), "nothing received yet");
            mirror.Apply(Snap(1, 7, 1), 0f);
            Assert.False(mirror.Stale(2.9f));
            Assert.True(mirror.Stale(WingMirror.StaleSeconds + 0.1f));
            mirror.Apply(Snap(2, 7, 1), 4f);
            Assert.False(mirror.Stale(4.5f));
        }
    }
}
