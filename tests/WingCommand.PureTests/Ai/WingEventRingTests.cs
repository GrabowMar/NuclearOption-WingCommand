using Xunit;

namespace WingCommand.PureTests
{
    public class WingEventRingTests
    {
        [Fact]
        public void RingKeepsTheNewestEventsOldestFirst()
        {
            var ring = new WingEventRing();
            for (int i = 0; i < 300; i++)
                ring.Push(new WingEvent { Time = i, Kind = i % 2 == 0 ? WingEventKind.GcasActivated : WingEventKind.FallingBehind });
            Assert.Equal(WingEventRing.Capacity, ring.Count);
            Assert.Equal(300, ring.Total);
            Assert.Equal(300f - WingEventRing.Capacity, ring[0].Time);
            Assert.Equal(299f, ring[ring.Count - 1].Time);
            Assert.Equal(128, ring.CountOf(WingEventKind.GcasActivated));
        }

        [Fact]
        public void CountOfFiltersByMember()
        {
            var ring = new WingEventRing();
            ring.Push(new WingEvent { Member = 0, Kind = WingEventKind.FallingBehind });
            ring.Push(new WingEvent { Member = 1, Kind = WingEventKind.FallingBehind });
            Assert.Equal(1, ring.CountOf(WingEventKind.FallingBehind, 1));
            Assert.Equal(2, ring.CountOf(WingEventKind.FallingBehind));
        }

        [Fact]
        public void CountOfAReasonCountsOnlyBehaviourTransitions()
        {
            var ring = new WingEventRing();
            ring.Push(new WingEvent { Kind = WingEventKind.BehaviourChanged, Reason = TransitionReason.MissileInbound });
            ring.Push(new WingEvent { Kind = WingEventKind.Disengaged, Reason = TransitionReason.MissileInbound });
            ring.Push(new WingEvent { Kind = WingEventKind.BehaviourChanged, Reason = TransitionReason.MissileClear });
            Assert.Equal(1, ring.CountOf(TransitionReason.MissileInbound));
            Assert.Equal(1, ring.CountOf(TransitionReason.MissileClear));
        }
    }
}
