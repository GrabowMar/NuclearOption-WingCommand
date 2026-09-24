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
    }
}
