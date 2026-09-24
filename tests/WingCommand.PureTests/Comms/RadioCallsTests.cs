using Xunit;

namespace WingCommand.PureTests
{
    public class RadioCallsTests
    {
        private static WingEvent E(WingEventKind k, TransitionReason r = TransitionReason.None, BehaviourId to = BehaviourId.StationKeep) =>
            new WingEvent { Kind = k, Reason = r, To = to, Member = 1 };

        [Theory]
        [InlineData(WingEventKind.BehaviourChanged, TransitionReason.MissileInbound, RadioClass.Emergency, "DEFENDING", false)]
        [InlineData(WingEventKind.BehaviourChanged, TransitionReason.MissileClear, RadioClass.Tactical, "DEFENSIVECLEAR", false)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.Outnumbered, RadioClass.Tactical, "FALLINGBACK", true)]   // the fall back takes each member back with this reason
        [InlineData(WingEventKind.Engaged, TransitionReason.Commanded, RadioClass.Tactical, "ENGAGING", true)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.Leash, RadioClass.Status, "REJOINING", true)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.NoTarget, RadioClass.Status, "REJOINING", true)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.Commanded, RadioClass.Status, "REJOINING", true)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.Winchester, RadioClass.Status, "WINCHESTER", false)]
        [InlineData(WingEventKind.Joker, TransitionReason.None, RadioClass.Status, "JOKER", false)]
        [InlineData(WingEventKind.Bingo, TransitionReason.None, RadioClass.Status, "BINGO", false)]
        [InlineData(WingEventKind.FallingBehind, TransitionReason.None, RadioClass.Status, "FALLINGBEHIND", false)]
        [InlineData(WingEventKind.GcasActivated, TransitionReason.None, RadioClass.Emergency, "PULLUP", false)]
        [InlineData(WingEventKind.CollisionEmergency, TransitionReason.None, RadioClass.Emergency, "BREAKOFF", false)]
        [InlineData(WingEventKind.AnchorLost, TransitionReason.None, RadioClass.Tactical, "LEADLOST", true)]
        [InlineData(WingEventKind.Airborne, TransitionReason.None, RadioClass.Status, "AIRBORNEREJOINING", false)]
        [InlineData(WingEventKind.Landed, TransitionReason.None, RadioClass.Chatter, "DOWN", false)]
        [InlineData(WingEventKind.LandingFailed, TransitionReason.None, RadioClass.Status, "GOAROUND", false)]
        [InlineData(WingEventKind.TaskCompleted, TransitionReason.None, RadioClass.Status, "TASKDONE", true)]
        [InlineData(WingEventKind.TaskFailed, TransitionReason.None, RadioClass.Status, "UNABLEORDER", true)]
        public void EventsMapToTheirCalls(object kind, object reason, object cls, string name, bool wing)
        {
            Assert.True(RadioCalls.For(E((WingEventKind)kind, (TransitionReason)reason), out RadioCall call));
            Assert.Equal((RadioClass)cls, call.Class);
            Assert.Equal(name, call.Name);
            Assert.Equal(wing, call.WingWide);
        }

        [Theory]
        [InlineData(WingEventKind.BehaviourChanged, TransitionReason.Captured)]
        [InlineData(WingEventKind.Disengaged, TransitionReason.Fuel)]
        [InlineData(WingEventKind.Taxiing, TransitionReason.None)]
        [InlineData(WingEventKind.Reserved, TransitionReason.None)]
        public void OtherEventsSayNothing(object kind, object reason) =>
            Assert.False(RadioCalls.For(E((WingEventKind)kind, (TransitionReason)reason), out _));

        [Theory]
        [InlineData("JOKER")] [InlineData("FALLINGBEHIND")] [InlineData("PULLUP")] [InlineData("BREAKOFF")]
        [InlineData("LEADLOST")] [InlineData("GOAROUND")] [InlineData("TASKDONE")]
        public void NewLinesHaveTheirOwnWordsForEveryPersona(string name)
        {
            foreach (ChatterPersona p in new[] { ChatterPersona.Professional, ChatterPersona.Aggressive, ChatterPersona.Calm, ChatterPersona.Dry })
                for (int seed = 0; seed < 4; seed++)
                {
                    string line = ChatterDialogue.Event(p, name, "2", seed);
                    Assert.False(string.IsNullOrWhiteSpace(line));
                    Assert.NotEqual(ChatterDialogue.Event(p, "NO-SUCH-CALL", null, seed), line);
                }
        }

        [Fact]
        public void JokerSaysTheMinutesToBingo() =>
            Assert.Contains("3", ChatterDialogue.Event(ChatterPersona.Professional, "JOKER", "3", 0));

        [Fact]
        public void NewEventsAreReadOnceEvenAfterTheRingWraps()
        {
            var ring = new WingEventRing();
            var cursor = new EventCursor();
            ring.Push(new WingEvent { Kind = WingEventKind.Joker });
            Assert.True(cursor.Next(ring, out WingEvent first));
            Assert.Equal(WingEventKind.Joker, first.Kind);
            Assert.False(cursor.Next(ring, out _));
            for (int i = 0; i < WingEventRing.Capacity + 10; i++) ring.Push(new WingEvent { Kind = WingEventKind.Bingo, Member = i });
            int n = 0;
            int firstMember = -1;
            while (cursor.Next(ring, out WingEvent e))
            {
                if (n == 0) firstMember = e.Member;
                n++;
            }
            Assert.Equal(WingEventRing.Capacity, n);                              // the overwritten ones are gone, the rest read once
            Assert.Equal(10, firstMember);
        }
    }
}
