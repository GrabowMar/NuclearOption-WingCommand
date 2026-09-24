using System;
using System.Text;

namespace WingCommand
{
    /// <summary>Wire protocol constants (spec M6 §2).</summary>
    internal static class Protocol
    {
        public const byte Version = 1;
        public const int MaxString = 64, MaxMessage = 1024;

        /// <summary>Reading is strict (invalid UTF-8 fails the read); writing is lenient (a lone surrogate becomes U+FFFD
        /// instead of an exception: review M6a I1).</summary>
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static readonly UTF8Encoding Utf8Write = new UTF8Encoding(false, false);
    }

    /// <summary>Little-endian writer into a caller's buffer (spec M6 §2.1): no allocation for numbers; past the end it
    /// latches <see cref="Overflow"/> and writes nothing more.</summary>
    internal sealed class ByteWriter
    {
        private readonly byte[] buffer;

        public ByteWriter(byte[] buffer) => this.buffer = buffer;

        public int Length { get; private set; }
        public bool Overflow { get; private set; }
        public byte[] Buffer => buffer;

        public void Reset()
        {
            Length = 0;
            Overflow = false;
        }

        private bool Room(int n)
        {
            if (Overflow || Length + n > buffer.Length) Overflow = true;
            return !Overflow;
        }

        public void U8(byte v)
        {
            if (!Room(1)) return;
            buffer[Length++] = v;
        }

        public void U16(ushort v)
        {
            if (!Room(2)) return;
            buffer[Length++] = (byte)v;
            buffer[Length++] = (byte)(v >> 8);
        }

        public void U32(uint v)
        {
            if (!Room(4)) return;
            buffer[Length++] = (byte)v;
            buffer[Length++] = (byte)(v >> 8);
            buffer[Length++] = (byte)(v >> 16);
            buffer[Length++] = (byte)(v >> 24);
        }

        public void F32(float v) => U32((uint)BitConverter.SingleToInt32Bits(v));

        public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);

        /// <summary>UTF-8 with a one-byte length, at most <see cref="Protocol.MaxString"/> bytes (cut at a character
        /// boundary); null writes as empty.</summary>
        public void String(string s)
        {
            s = s ?? "";
            // One pass over the characters: the longest whole-character prefix within MaxString bytes (a surrogate pair
            // is 4 bytes and never split; a lone surrogate is written as the 3-byte replacement character).
            int bytes = 0, chars = 0;
            while (chars < s.Length)
            {
                char c = s[chars];
                bool pair = char.IsHighSurrogate(c) && chars + 1 < s.Length && char.IsLowSurrogate(s[chars + 1]);
                int width = pair ? 4 : c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                if (bytes + width > Protocol.MaxString) break;
                bytes += width;
                chars += pair ? 2 : 1;
            }
            if (!Room(1 + bytes)) return;
            buffer[Length++] = (byte)bytes;
            Length += Protocol.Utf8Write.GetBytes(s, 0, chars, buffer, Length);
        }
    }

    /// <summary>Bounds-checked little-endian reader over a window of a buffer (spec M6 §2.1): past the end, or on a bad
    /// string, it latches <see cref="Failed"/> and returns zeros; it never throws.</summary>
    internal sealed class ByteReader
    {
        private readonly byte[] buffer;
        private readonly int end;
        private int at;

        public ByteReader(byte[] buffer, int offset, int count)
        {
            this.buffer = buffer;
            at = offset;
            end = offset + count;
        }

        public bool Failed { get; private set; }
        public int Remaining => Failed ? 0 : end - at;

        private bool Take(int n)
        {
            if (Failed || at + n > end) Failed = true;
            return !Failed;
        }

        public byte U8() => Take(1) ? buffer[at++] : (byte)0;

        public ushort U16()
        {
            if (!Take(2)) return 0;
            ushort v = (ushort)(buffer[at] | (buffer[at + 1] << 8));
            at += 2;
            return v;
        }

        public uint U32()
        {
            if (!Take(4)) return 0;
            uint v = (uint)(buffer[at] | (buffer[at + 1] << 8) | (buffer[at + 2] << 16) | (buffer[at + 3] << 24));
            at += 4;
            return v;
        }

        public float F32() => BitConverter.Int32BitsToSingle((int)U32());

        public bool Bool() => U8() != 0;

        public string String()
        {
            int n = U8();
            if (Failed) return "";
            if (n > Protocol.MaxString || !Take(n))
            {
                Failed = true;
                return "";
            }
            try
            {
                string s = Protocol.Utf8.GetString(buffer, at, n);
                at += n;
                return s;
            }
            catch (ArgumentException)
            {
                Failed = true;
                return "";
            }
        }
    }
}
