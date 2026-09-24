using Xunit;

namespace WingCommand.PureTests
{
    public class AlertListTests
    {
        [Fact]
        public void MissileBeatsBingoBeatsWinchesterBeatsJoker()
        {
            var rows = new[]
            {
                new SnapshotMember { Id = 1, Slot = 0, Flags = (byte)SnapshotFlags.Joker },
                new SnapshotMember { Id = 2, Slot = 1, Flags = (byte)SnapshotFlags.Winchester },
                new SnapshotMember { Id = 3, Slot = 2, Duty = (byte)MemberDuty.Defending },
                new SnapshotMember { Id = 4, Slot = 3, Flags = (byte)(SnapshotFlags.Bingo | SnapshotFlags.Joker) },
            };
            var into = new Alert[AlertList.Max];
            int n = AlertList.Fill(rows, 4, into);
            Assert.Equal(4, n);   // bingo hides joker on the same aircraft
            Assert.Equal(new[] { AlertKind.Missile, AlertKind.Bingo, AlertKind.Winchester, AlertKind.Joker },
                new[] { into[0].Kind, into[1].Kind, into[2].Kind, into[3].Kind });
            Assert.Equal(3u, into[0].Id);
        }

        [Fact]
        public void FallingBehindIsTheLeastAlert()
        {
            var rows = new[] { new SnapshotMember { Id = 7, Flags = (byte)SnapshotFlags.FallingBehind } };
            var into = new Alert[AlertList.Max];
            Assert.Equal(1, AlertList.Fill(rows, 1, into));
            Assert.Equal(AlertKind.Behind, into[0].Kind);
            Assert.Equal("BEHIND", AlertList.Word(AlertKind.Behind));
        }

        [Fact]
        public void AQuietWingHasNoAlertsAndWordsFitTheColumn()
        {
            var rows = new[] { new SnapshotMember { Id = 1 } };
            Assert.Equal(0, AlertList.Fill(rows, 1, new Alert[AlertList.Max]));
            foreach (AlertKind k in System.Enum.GetValues(typeof(AlertKind))) Assert.InRange(AlertList.Word(k).Length, 4, 10);
        }
    }
}
