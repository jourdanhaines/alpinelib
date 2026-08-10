using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Little-endian binary writer over a caller-supplied byte array. Deliberately a plain (non-ref)
    /// struct so C# 9 lets it sit in fields and be handed to <see cref="INetMessage.Serialize"/> by
    /// <c>ref</c>; always pass it by reference, because copying it forks the cursor.
    ///
    /// Every message on every wire in the project goes through this type: one serializer, one endianness,
    /// one place where quantization happens.
    /// </summary>
    public struct NetWriter {
        private readonly byte[] buffer;
        private readonly int origin;
        private readonly int capacity;
        private int position;

        public NetWriter(byte[] buffer) : this(buffer, 0, buffer == null ? 0 : buffer.Length) { }

        public NetWriter(byte[] buffer, int offset, int count) {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length) {
                throw new ArgumentOutOfRangeException(nameof(offset), "NetWriter window falls outside the buffer.");
            }

            this.buffer = buffer;
            origin = offset;
            capacity = count;
            position = 0;
        }

        /// <summary>Cursor position relative to the window start. Settable so callers can patch a header.</summary>
        public int Position {
            get => position;
            set {
                if (value < 0 || value > capacity) {
                    throw new ArgumentOutOfRangeException(nameof(value), "NetWriter position falls outside the window.");
                }

                position = value;
            }
        }

        /// <summary>Bytes written so far, i.e. the payload length to hand to the transport.</summary>
        public int Written => position;

        /// <summary>Bytes still available before the window is full.</summary>
        public int Remaining => capacity - position;

        /// <summary>Total size of the window this writer was given.</summary>
        public int Capacity => capacity;

        /// <summary>The written payload as a segment of the underlying array — no copy.</summary>
        public ArraySegment<byte> ToSegment() {
            return new ArraySegment<byte>(buffer, origin, position);
        }

        /// <summary>The written payload as a span — no copy.</summary>
        public ReadOnlySpan<byte> AsSpan() {
            return new ReadOnlySpan<byte>(buffer, origin, position);
        }

        /// <summary>Copies the written payload into a fresh array. For tests and for archival paths only.</summary>
        public byte[] ToArray() {
            var copy = new byte[position];
            Array.Copy(buffer, origin, copy, 0, position);
            return copy;
        }

        /// <summary>Rewinds the cursor without touching the buffer contents.</summary>
        public void Reset() {
            position = 0;
        }

        public void WriteByte(byte value) {
            Reserve(1);
            buffer[origin + position] = value;
            position += 1;
        }

        public void WriteSByte(sbyte value) {
            WriteByte(unchecked((byte)value));
        }

        public void WriteBool(bool value) {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteShort(short value) {
            Reserve(2);
            BinaryPrimitives.WriteInt16LittleEndian(Window(2), value);
            position += 2;
        }

        public void WriteUShort(ushort value) {
            Reserve(2);
            BinaryPrimitives.WriteUInt16LittleEndian(Window(2), value);
            position += 2;
        }

        public void WriteInt(int value) {
            Reserve(4);
            BinaryPrimitives.WriteInt32LittleEndian(Window(4), value);
            position += 4;
        }

        public void WriteUInt(uint value) {
            Reserve(4);
            BinaryPrimitives.WriteUInt32LittleEndian(Window(4), value);
            position += 4;
        }

        public void WriteLong(long value) {
            Reserve(8);
            BinaryPrimitives.WriteInt64LittleEndian(Window(8), value);
            position += 8;
        }

        public void WriteULong(ulong value) {
            Reserve(8);
            BinaryPrimitives.WriteUInt64LittleEndian(Window(8), value);
            position += 8;
        }

        /// <summary>IEEE-754 single, written as its raw bits so the layout never depends on the runtime.</summary>
        public void WriteFloat(float value) {
            WriteInt(BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>
        /// LEB128-style variable length unsigned integer: 7 payload bits per byte, high bit = continue.
        /// Small ids, counts and lengths — the overwhelming majority of what this protocol writes — cost
        /// a single byte.
        /// </summary>
        public void WriteVarUInt(uint value) {
            uint remaining = value;
            while (remaining >= 0x80u) {
                WriteByte((byte)(remaining | 0x80u));
                remaining >>= 7;
            }

            WriteByte((byte)remaining);
        }

        /// <summary>
        /// UTF-8 text prefixed with its byte length as a var-uint. A null string is written as length 0
        /// and therefore reads back as <see cref="string.Empty"/>; the protocol has no use for the
        /// distinction and paying a byte to preserve it would be waste.
        /// </summary>
        public void WriteString(string value) {
            if (string.IsNullOrEmpty(value)) {
                WriteVarUInt(0u);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            WriteVarUInt((uint)byteCount);
            Reserve(byteCount);
            Encoding.UTF8.GetBytes(value.AsSpan(), Window(byteCount));
            position += byteCount;
        }

        /// <summary>Writes raw bytes with no length prefix — for envelope payloads that carry their own framing.</summary>
        public void WriteRaw(ReadOnlySpan<byte> value) {
            if (value.Length == 0) {
                return;
            }

            Reserve(value.Length);
            value.CopyTo(Window(value.Length));
            position += value.Length;
        }

        /// <summary>Writes a var-uint length followed by the bytes themselves.</summary>
        public void WriteBytes(ReadOnlySpan<byte> value) {
            WriteVarUInt((uint)value.Length);
            WriteRaw(value);
        }

        /// <summary>Full-precision position vector: three floats, twelve bytes.</summary>
        public void WriteVector3(Vector3 value) {
            WriteFloat(value.X);
            WriteFloat(value.Y);
            WriteFloat(value.Z);
        }

        /// <summary>Yaw compressed to a ushort — see <see cref="NetQuantization"/> for the tolerance.</summary>
        public void WriteQuantizedYaw(float degrees) {
            WriteUShort(NetQuantization.EncodeYaw(degrees));
        }

        /// <summary>Velocity compressed to three fixed-point shorts: six bytes instead of twelve.</summary>
        public void WriteQuantizedVelocity(Vector3 velocity) {
            WriteShort(NetQuantization.EncodeVelocityComponent(velocity.X));
            WriteShort(NetQuantization.EncodeVelocityComponent(velocity.Y));
            WriteShort(NetQuantization.EncodeVelocityComponent(velocity.Z));
        }

        /// <summary>Serializes a message in place. The struct is passed by <c>in</c> to avoid a defensive copy.</summary>
        public void WriteMessage<TMessage>(in TMessage message) where TMessage : struct, INetMessage {
            TMessage local = message;
            local.Serialize(ref this);
        }

        private Span<byte> Window(int count) {
            return new Span<byte>(buffer, origin + position, count);
        }

        private void Reserve(int count) {
            if (position + count <= capacity) {
                return;
            }

            throw new NetProtocolException(
                $"NetWriter overflow: {count} byte(s) requested with {capacity - position} remaining of {capacity}.");
        }
    }
}
