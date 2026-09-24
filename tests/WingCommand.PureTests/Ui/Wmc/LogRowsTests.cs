using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class LogRowsTests
    {
        [Fact]
        public void DescribesNotableEventsAndSkipsBehaviourNoise()
        {
            Assert.Null(LogRows.Describe(new WingEvent { Kind = WingEventKind.BehaviourChanged }));
            Assert.Null(LogRows.Describe(new WingEvent { Kind = WingEventKind.FallingBehindCleared }));
            Assert.Equal("bingo fuel", LogRows.Describe(new WingEvent { Kind = WingEventKind.Bingo }));
            Assert.Equal("ROUTE started", LogRows.Describe(new WingEvent { Kind = WingEventKind.TaskStarted, Task = TaskKind.Route }));
            Assert.Equal("ORBIT failed (no wing)",
                LogRows.Describe(new WingEvent { Kind = WingEventKind.TaskFailed, Task = TaskKind.Orbit, Reason = TransitionReason.NoWing }));
            Assert.Equal("disengaged (no target)",
                LogRows.Describe(new WingEvent { Kind = WingEventKind.Disengaged, Reason = TransitionReason.NoTarget }));
            Assert.Equal("disengaged", LogRows.Describe(new WingEvent { Kind = WingEventKind.Disengaged }));
        }

        [Fact]
        public void LossesDamageKillsAndWinchesterHaveWords()
        {
            Assert.Equal("shot down", LogRows.Describe(new WingEvent { Kind = WingEventKind.MemberLost, Reason = TransitionReason.Killed }));
            Assert.Equal("ejected", LogRows.Describe(new WingEvent { Kind = WingEventKind.MemberLost, Reason = TransitionReason.Ejected }));
            Assert.Equal("left the wing", LogRows.Describe(new WingEvent { Kind = WingEventKind.MemberLost, Reason = TransitionReason.Released }));
            Assert.Equal("lost", LogRows.Describe(new WingEvent { Kind = WingEventKind.MemberLost, Reason = TransitionReason.Gone }));
            Assert.Equal("damaged", LogRows.Describe(new WingEvent { Kind = WingEventKind.Damaged }));
            Assert.Equal("target destroyed", LogRows.Describe(new WingEvent { Kind = WingEventKind.TargetDestroyed }));
            Assert.Equal("winchester", LogRows.Describe(new WingEvent { Kind = WingEventKind.Winchester }));
        }

        [Fact]
        public void AnElementFilterKeepsItsLossesAfterTheAircraftIsGone()
        {
            var events = new WingEventRing();
            events.Push(new WingEvent { Time = 3f, Member = 1, Kind = WingEventKind.MemberLost, Reason = TransitionReason.Killed, Element = 1, Id = 7u });
            var into = new List<LogRow>();
            Assert.Equal(1, LogRows.Fill(events, null, into, LogRows.MaxRows, new LogFilter { Element = 1, Rows = new SnapshotMember[0], Count = 0 }));
            Assert.Equal(0, LogRows.Fill(events, null, into, LogRows.MaxRows, new LogFilter { Element = 0, Rows = new SnapshotMember[0], Count = 0 }));
        }

        [Fact]
        public void EveryEventKindHasAnAnswer()
        {
            foreach (WingEventKind k in System.Enum.GetValues(typeof(WingEventKind)))
            {
                string s = LogRows.Describe(new WingEvent { Kind = k });
                Assert.True(s == null || s.Length > 0, k.ToString());
            }
        }

        [Fact]
        public void WhoIsTheMemberNumberOrTheWing()
        {
            Assert.Equal("#3", LogRows.Who(1));
            Assert.Equal("WING", LogRows.Who(-1));
        }

        [Fact]
        public void FillMergesNewestFirstAndCaps()
        {
            var events = new WingEventRing();
            events.Push(new WingEvent { Time = 10f, Member = 0, Kind = WingEventKind.Bingo });
            events.Push(new WingEvent { Time = 20f, Member = 1, Kind = WingEventKind.BehaviourChanged });
            events.Push(new WingEvent { Time = 30f, Member = -1, Kind = WingEventKind.TaskStarted, Task = TaskKind.Move });
            var radio = new RadioLog();
            radio.Push(15f, "Viper 2: bingo");
            radio.Push(30f, "Viper 3: copy");
            var rows = new List<LogRow>();
            Assert.Equal(4, LogRows.Fill(events, radio, rows));
            Assert.Equal("MOVE started", rows[0].Text);          // a tie lists the event first
            Assert.Equal("Viper 3: copy", rows[1].Text);
            Assert.True(rows[1].Radio);
            Assert.Equal(15f, rows[2].Time);
            Assert.Equal(0, rows[3].Member);
            Assert.Equal(2, LogRows.Fill(events, radio, rows, max: 2));
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void FillWithNothingIsEmpty()
        {
            var rows = new List<LogRow> { new LogRow() };
            Assert.Equal(0, LogRows.Fill(null, null, rows));
            Assert.Empty(rows);
        }

        private static (WingEventRing, RadioLog, SnapshotMember[]) Mixed()
        {
            var events = new WingEventRing();
            events.Seat(0, 1u);
            events.Seat(2, 3u);
            events.Push(new WingEvent { Time = 5f, Member = -1, Kind = WingEventKind.TaskStarted, Task = TaskKind.Orbit, Element = 1 });
            events.Push(new WingEvent { Time = 6f, Member = -1, Kind = WingEventKind.TaskStarted, Task = TaskKind.Move, Element = 0 });
            events.Push(new WingEvent { Time = 7f, Member = 2, Kind = WingEventKind.Bingo });
            events.Push(new WingEvent { Time = 8f, Member = 0, Kind = WingEventKind.Bingo });
            var radio = new RadioLog();
            radio.Push(9f, "Viper 2: copy");
            var rows = new[] { new SnapshotMember { Id = 1, Slot = 0, Element = 0 }, new SnapshotMember { Id = 3, Slot = 2, Element = 1 } };
            return (events, radio, rows);
        }

        [Fact]
        public void FilterByElementKeepsItsEvents()
        {
            // Spec WMC program §4 LOG: an element's task events and its members' events, no radio.
            var (events, radio, rows) = Mixed();
            var into = new List<LogRow>();
            Assert.Equal(2, LogRows.Fill(events, radio, into, LogRows.MaxRows, new LogFilter { Element = 1, Rows = rows, Count = 2 }));
            Assert.Equal(7f, into[0].Time);
            Assert.Equal("ORBIT started", into[1].Text);
        }

        [Fact]
        public void FilterByAircraftKeepsItsEvents()
        {
            var (events, radio, rows) = Mixed();
            var into = new List<LogRow>();
            Assert.Equal(1, LogRows.Fill(events, radio, into, LogRows.MaxRows, new LogFilter { Element = -1, ById = true, Id = 1u, Rows = rows, Count = 2 }));
            Assert.Equal(8f, into[0].Time);
            Assert.Equal(1u, into[0].Id);
            Assert.Equal(5, LogRows.Fill(events, radio, into, LogRows.MaxRows, LogFilter.None));
            // Selected but gone: nothing matches.
            Assert.Equal(0, LogRows.Fill(events, radio, into, LogRows.MaxRows, new LogFilter { Element = -1, ById = true, Id = 0u }));
        }

        [Fact]
        public void TheAircraftFilterFollowsTheAircraftNotTheSeat()
        {
            // Review P3 I5: #2 (seat 0, id 1) is lost, id 3 moves up to seat 0; its filter keeps only its own events.
            var (events, radio, rows) = Mixed();
            events.Seat(0, 3u);
            events.Push(new WingEvent { Time = 10f, Member = 0, Kind = WingEventKind.Joker });
            rows = new[] { new SnapshotMember { Id = 3, Slot = 0, Element = 1 } };
            var into = new List<LogRow>();
            Assert.Equal(2, LogRows.Fill(events, radio, into, LogRows.MaxRows, new LogFilter { Element = -1, ById = true, Id = 3u, Rows = rows, Count = 1 }));
            Assert.Equal(10f, into[0].Time);
            Assert.Equal(7f, into[1].Time);
            // The element filter finds the member by aircraft too: the seat-0 Bingo at 8 s was id 1, not in B.
            Assert.Equal(3, LogRows.Fill(events, radio, into, LogRows.MaxRows, new LogFilter { Element = 1, Rows = rows, Count = 1 }));
        }
    }
}
