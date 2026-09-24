using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class WireCodecTests
    {
        private delegate void Write(ByteWriter w);

        private static byte[] Encode(Write write)
        {
            var b = new byte[Protocol.MaxMessage];
            var w = new ByteWriter(b);
            write(w);
            Assert.False(w.Overflow);
            return b[..w.Length];
        }

        /// <summary>Decodes whatever message the bytes hold; false on any failure.</summary>
        private static bool DecodeAny(byte[] b, int count)
        {
            ByteReader r = WireCodec.Open(b, 0, count, out MessageKind kind);
            switch (kind)
            {
                case MessageKind.Hello: return WcHello.TryDecode(r, out _);
                case MessageKind.HelloReply: return WcHelloReply.TryDecode(r, out _);
                case MessageKind.Command: return WcCommand.TryDecode(r, out _);
                case MessageKind.Ack: return WcAck.TryDecode(r, out _);
                case MessageKind.Snapshot: return WcSnapshot.TryDecode(r, out _);
                case MessageKind.Event: return WcEvent.TryDecode(r, out _);
                default: return false;
            }
        }

        private static WcCommand FullCommand()
        {
            var ids = new uint[WcCommand.MaxUnits];
            var points = new WcWaypoint[WcCommand.MaxWaypoints];
            for (int i = 0; i < ids.Length; i++) ids[i] = (uint)(1000 + i);
            for (int i = 0; i < points.Length; i++) points[i] = new WcWaypoint { X = i * 100f, Z = -i * 50f, Alt = i % 2 == 0 ? float.NaN : 800f };
            return new WcCommand
            {
                Seq = 42, Kind = CommandKind.Task, Units = ids, Waypoints = points, Args = new[] { 1f, 2f, 3f, 4f }, Text = "Patrol",
            };
        }

        private static WcSnapshot FullSnapshot()
        {
            var members = new SnapshotMember[WcSnapshot.MaxMembers];
            for (int i = 0; i < members.Length; i++)
                members[i] = new SnapshotMember { Id = (uint)(500 + i), Slot = (byte)i, Behaviour = 1, Duty = 2, Fuel = 200, Ammo = 100, Flags = 5 };
            return new WcSnapshot { Tick = 99, Owner = 7, Members = members };
        }

        [Fact]
        public void EveryMessageRoundTrips()
        {
            var hello = new WcHello { ModVersion = "1.0.0-alpha.9", PerPlayer = 8, Total = 16 };
            byte[] b = Encode(hello.Encode);
            Assert.True(WcHello.TryDecode(WireCodec.Open(b, 0, b.Length, out MessageKind k), out WcHello h));
            Assert.Equal(MessageKind.Hello, k);
            Assert.Equal(("1.0.0-alpha.9", (byte)8, (byte)16), (h.ModVersion, h.PerPlayer, h.Total));

            b = Encode(new WcHelloReply { ModVersion = "1.0.0-alpha.9" }.Encode);
            Assert.True(WcHelloReply.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcHelloReply hr));
            Assert.Equal("1.0.0-alpha.9", hr.ModVersion);

            WcCommand c = FullCommand();
            b = Encode(c.Encode);
            Assert.True(WcCommand.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcCommand cb));
            Assert.Equal(c.Seq, cb.Seq);
            Assert.Equal(c.Kind, cb.Kind);
            Assert.Equal(c.Units, cb.Units);
            Assert.Equal(c.Args, cb.Args);
            Assert.Equal("Patrol", cb.Text);
            for (int i = 0; i < c.Waypoints.Length; i++)
            {
                Assert.Equal(c.Waypoints[i].X, cb.Waypoints[i].X);
                Assert.Equal(c.Waypoints[i].Z, cb.Waypoints[i].Z);
                Assert.Equal(float.IsNaN(c.Waypoints[i].Alt), float.IsNaN(cb.Waypoints[i].Alt));
            }

            b = Encode(new WcCommand { Seq = 1, Kind = CommandKind.FormUp }.Encode);
            Assert.True(WcCommand.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcCommand empty));
            Assert.Empty(empty.Units);
            Assert.Empty(empty.Waypoints);
            Assert.Empty(empty.Args);

            b = Encode(new WcAck { Seq = 42, Accepted = false, Reason = "nobody can engage" }.Encode);
            Assert.True(WcAck.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcAck a));
            Assert.Equal((42u, false, "nobody can engage"), (a.Seq, a.Accepted, a.Reason));

            WcSnapshot s = FullSnapshot();
            b = Encode(s.Encode);
            Assert.True(b.Length <= 128, $"an 8-member snapshot is {b.Length} B");
            Assert.True(WcSnapshot.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcSnapshot sb));
            Assert.Equal((99u, 7u, 8), (sb.Tick, sb.Owner, sb.Members.Length));
            Assert.Equal(s.Members[7].Id, sb.Members[7].Id);
            Assert.Equal(s.Members[3].Fuel, sb.Members[3].Fuel);

            var e = new WcEvent { Time = 12.5f, Member = 2, Kind = 30, From = 1, To = 4, Reason = 16, Task = 3 };
            b = Encode(e.Encode);
            Assert.True(WcEvent.TryDecode(WireCodec.Open(b, 0, b.Length, out _), out WcEvent eb));
            Assert.Equal((12.5f, (byte)2, (byte)30, (byte)1, (byte)4, (byte)16, (byte)3), (eb.Time, eb.Member, eb.Kind, eb.From, eb.To, eb.Reason, eb.Task));
        }

        [Fact]
        public void EveryTruncationFailsWithoutThrowing()
        {
            byte[][] all =
            {
                Encode(new WcHello { ModVersion = "x", PerPlayer = 8, Total = 16 }.Encode),
                Encode(new WcHelloReply { ModVersion = "x" }.Encode),
                Encode(FullCommand().Encode),
                Encode(new WcAck { Seq = 1, Accepted = true, Reason = "ok" }.Encode),
                Encode(FullSnapshot().Encode),
                Encode(new WcEvent { Time = 1f }.Encode),
            };
            foreach (byte[] b in all)
            {
                Assert.True(DecodeAny(b, b.Length));
                for (int n = 0; n < b.Length; n++) Assert.False(DecodeAny(b, n), $"{(MessageKind)b[0]} truncated to {n} of {b.Length}");
            }
        }

        [Fact]
        public void TrailingBytesAWrongVersionOrAnUnknownKindFail()
        {
            byte[] b = Encode(new WcAck { Seq = 1, Accepted = true, Reason = "ok" }.Encode);
            byte[] longer = new byte[b.Length + 1];
            b.CopyTo(longer, 0);
            Assert.False(DecodeAny(longer, longer.Length));
            byte[] version = (byte[])b.Clone();
            version[1] = Protocol.Version + 1;
            Assert.False(DecodeAny(version, version.Length));
            byte[] kind = (byte[])b.Clone();
            kind[0] = 200;
            Assert.False(DecodeAny(kind, kind.Length));
        }

        [Fact]
        public void CountsAboveTheCapsFailEvenWithTheBytesPresent()
        {
            byte[] c = Encode(w =>
            {
                WireCodec.Header(w, MessageKind.Command);
                w.U32(1);
                w.U8((byte)CommandKind.Attack);
                w.U8(WcCommand.MaxUnits + 1);
                for (int i = 0; i <= WcCommand.MaxUnits; i++) w.U32((uint)i);
                w.U8(0);
                w.U8(0);
                w.String("");
            });
            Assert.False(DecodeAny(c, c.Length));
            byte[] s = Encode(w =>
            {
                WireCodec.Header(w, MessageKind.Snapshot);
                w.U32(1);
                w.U32(1);
                w.U8(WcSnapshot.MaxMembers + 1);
                for (int i = 0; i <= WcSnapshot.MaxMembers; i++)
                {
                    w.U32((uint)i);
                    for (int j = 0; j < 6; j++) w.U8(0);
                }
            });
            Assert.False(DecodeAny(s, s.Length));
        }

        [Fact]
        public void RandomBytesNeverThrow()
        {
            var random = new Random(1234);
            var b = new byte[300];
            for (int i = 0; i < 10000; i++)
            {
                random.NextBytes(b);
                if (i % 3 == 0) b[1] = Protocol.Version;
                if (i % 2 == 0) b[0] = (byte)(1 + random.Next(6));
                DecodeAny(b, random.Next(b.Length + 1));
            }
        }
    }
}
