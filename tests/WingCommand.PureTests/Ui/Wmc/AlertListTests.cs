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
        public void DamagedComesAfterLostAndBeforeBingo()
        {
            var rows = new[]
            {
                new SnapshotMember { Id = 1, Slot = 0, Flags = (byte)SnapshotFlags.Bingo },
                new SnapshotMember { Id = 2, Slot = 1, Flags = (byte)SnapshotFlags.Damaged },
            };
            var ring = new WingEventRing();
            ring.Push(new WingEvent { Time = 10f, Member = 2, Kind = WingEventKind.MemberLost, Reason = TransitionReason.Killed, Id = 99u });
            var into = new Alert[AlertList.Max];
            int n = AlertList.Fill(rows, 2, ring, 15f, into);
            Assert.Equal(3, n);
            Assert.Equal(new[] { AlertKind.Lost, AlertKind.Damaged, AlertKind.Bingo }, new[] { into[0].Kind, into[1].Kind, into[2].Kind });
            Assert.Equal(99u, into[0].Id);
            Assert.Equal(TransitionReason.Killed, into[0].Why);
        }

        [Fact]
        public void ALossShowsForTwentySecondsAndOnlyForRealLosses()
        {
            var ring = new WingEventRing();
            ring.Push(new WingEvent { Time = 10f, Member = 2, Kind = WingEventKind.MemberLost, Reason = TransitionReason.Killed, Id = 99u });
            ring.Push(new WingEvent { Time = 10f, Member = 3, Kind = WingEventKind.MemberLost, Reason = TransitionReason.Released, Id = 98u });
            var into = new Alert[AlertList.Max];
            var none = new SnapshotMember[0];
            Assert.Equal(1, AlertList.Fill(none, 0, ring, 29f, into));
            Assert.Equal(0, AlertList.Fill(none, 0, ring, 31f, into));
            // The aircraft is back in the wing (a re-adopt): no LOST for it.
            Assert.Equal(0, AlertList.Fill(new[] { new SnapshotMember { Id = 99u } }, 1, ring, 12f, into));
            Assert.Equal(0, AlertList.Fill(none, 0, null, 12f, into));
        }

        [Fact]
        public void AFullListKeepsTheMostSevere()
        {
            // Review R1 m (and R3 research): the cap used to drop by seat before the sort; a late missile went missing.
            var rows = new SnapshotMember[8];
            for (int i = 0; i < 7; i++)
                rows[i] = new SnapshotMember { Id = (uint)(i + 1), Slot = (byte)i,
                    Flags = (byte)(SnapshotFlags.Joker | SnapshotFlags.Winchester | SnapshotFlags.FallingBehind) };
            rows[7] = new SnapshotMember { Id = 8, Slot = 7, Duty = (byte)MemberDuty.Defending };
            var into = new Alert[AlertList.Max];
            Assert.Equal(AlertList.Max, AlertList.Fill(rows, 8, into));
            Assert.Equal(AlertKind.Missile, into[0].Kind);
            Assert.Equal(8u, into[0].Id);
        }

        [Fact]
        public void ALossSaysHowItWasLost()
        {
            Assert.Equal("shot down", AlertList.Detail(new Alert { Kind = AlertKind.Lost, Why = TransitionReason.Killed }));
            Assert.Equal("ejected", AlertList.Detail(new Alert { Kind = AlertKind.Lost, Why = TransitionReason.Ejected }));
            Assert.Equal("lost", AlertList.Detail(new Alert { Kind = AlertKind.Lost, Why = TransitionReason.Gone }));
            Assert.Equal("bingo fuel", AlertList.Detail(new Alert { Kind = AlertKind.Bingo }));
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
