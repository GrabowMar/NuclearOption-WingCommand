using System.Text;
using Xunit;

namespace WingCommand.PureTests
{
    public class BytesTests
    {
        [Fact]
        public void EveryPrimitiveRoundTrips()
        {
            var buffer = new byte[128];
            var w = new ByteWriter(buffer);
            w.U8(200);
            w.U16(65000);
            w.U32(4000000000u);
            w.F32(-12.5f);
            w.F32(float.NaN);
            w.F32(float.PositiveInfinity);
            w.Bool(true);
            w.String("Vortex");
            w.String("");
            w.String(null);
            Assert.False(w.Overflow);
            var r = new ByteReader(buffer, 0, w.Length);
            Assert.Equal(200, r.U8());
            Assert.Equal(65000, r.U16());
            Assert.Equal(4000000000u, r.U32());
            Assert.Equal(-12.5f, r.F32());
            Assert.True(float.IsNaN(r.F32()));
            Assert.True(float.IsPositiveInfinity(r.F32()));
            Assert.True(r.Bool());
            Assert.Equal("Vortex", r.String());
            Assert.Equal("", r.String());
            Assert.Equal("", r.String());
            Assert.False(r.Failed);
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void IntegersAreLittleEndian()
        {
            var buffer = new byte[8];
            var w = new ByteWriter(buffer);
            w.U16(0x0102);
            w.U32(0x03040506u);
            Assert.Equal(new byte[] { 0x02, 0x01, 0x06, 0x05, 0x04, 0x03 }, buffer[..6]);
        }

        [Fact]
        public void AWriterPastItsBufferLatchesOverflowAndWritesNothingMore()
        {
            var buffer = new byte[5];
            var w = new ByteWriter(buffer);
            w.U32(1u);
            w.U16(2);        // does not fit
            w.U8(3);         // would fit, but the overflow latched
            Assert.True(w.Overflow);
            Assert.Equal(4, w.Length);
            Assert.Equal(0, buffer[4]);
            w.Reset();
            Assert.False(w.Overflow);
            Assert.Equal(0, w.Length);
        }

        [Fact]
        public void AReaderPastTheEndLatchesFailedAndReturnsZeros()
        {
            var r = new ByteReader(new byte[] { 1, 2, 3 }, 0, 3);
            Assert.Equal(0x0201, r.U16());
            Assert.Equal(0u, r.U32());
            Assert.True(r.Failed);
            Assert.Equal(0, r.U8());
            Assert.True(r.Failed);
        }

        [Fact]
        public void ALongStringIsCutAtACharacterBoundary()
        {
            string wide = new string('é', 40);   // 80 bytes of UTF-8
            var buffer = new byte[128];
            var w = new ByteWriter(buffer);
            w.String(wide);
            Assert.Equal(1 + 64, w.Length);
            string back = new ByteReader(buffer, 0, w.Length).String();
            Assert.Equal(32, back.Length);
            Assert.StartsWith(back, wide);
        }

        [Fact]
        public void ABadStringFailsTheRead()
        {
            // Length beyond the bytes present.
            var r = new ByteReader(new byte[] { 5, (byte)'a' }, 0, 2);
            r.String();
            Assert.True(r.Failed);
            // Length beyond the protocol's cap.
            var big = new byte[80];
            big[0] = 70;
            r = new ByteReader(big, 0, big.Length);
            r.String();
            Assert.True(r.Failed);
            // Invalid UTF-8.
            r = new ByteReader(new byte[] { 2, 0xC3, 0x28 }, 0, 3);
            r.String();
            Assert.True(r.Failed);
        }

        [Fact]
        public void AReaderStaysInsideItsWindow()
        {
            byte[] b = Encoding.ASCII.GetBytes("xxABCyy");
            var r = new ByteReader(b, 2, 3);
            Assert.Equal((byte)'A', r.U8());
            Assert.Equal(2, r.Remaining);
            r.U32();
            Assert.True(r.Failed);
        }
    }
}
