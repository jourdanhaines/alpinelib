using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Little-endian binary reader mirroring <see cref="NetWriter"/> field for field. Like the writer it
    /// is a plain (non-ref) struct for C# 9 compatibility and must always be passed by <c>ref</c>.
    ///
    /// Every read is bounds-checked and throws <see cref="NetProtocolException"/> on violation, because
    /// the bytes come from the network: a truncated or hostile packet must fail loudly at the peer that
    /// sent it rather than silently decode into nonsense.
    /// </summary>
    public struct NetReader {
        private readonly byte[] buffer;
        private readonly int origin;
        private readonly int length;
        private int position;

        public NetReader(byte[] buffer) : this(buffer, 0, buffer == null ? 0 : buffer.Length) { }

        public NetReader(ArraySegment<byte> segment) : this(segment.Array, segment.Offset, segment.Count) { }

        public NetReader(byte[] buffer, int offset, int count) {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length) {
                throw new ArgumentOutOfRangeException(nameof(offset), "NetReader window falls outside the buffer.");
            }

            this.buffer = buffer;
            origin = offset;
            length = count;
            position = 0;
        }

        /// <summary>Cursor position relative to the window start.</summary>
        public int Position {
            get => position;
            set {
                if (value < 0 || value > length) {
                    throw new ArgumentOutOfRangeException(nameof(value), "NetReader position falls outside the window.");
                }

                position = value;
            }
        }

        /// <summary>Bytes consumed so far.</summary>
        public int Consumed => position;

        /// <summary>Bytes left in the window.</summary>
        public int Remaining => length - position;

        /// <summary>Total size of the window this reader was given.</summary>
        public int Length => length;

        /// <summary>True once every byte of the window has been consumed.</summary>
        public bool IsExhausted => position >= length;

        public byte ReadByte() {
            Require(1);
            byte value = buffer[origin + position];
            position += 1;
            return value;
        }

        public sbyte ReadSByte() {
            return unchecked((sbyte)ReadByte());
        }

        public bool ReadBool() {
            return ReadByte() != 0;
        }

        public short ReadShort() {
            Require(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(Window(2));
            position += 2;
            return value;
        }

        public ushort ReadUShort() {
            Require(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(Window(2));
            position += 2;
            return value;
        }

        public int ReadInt() {
            Require(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(Window(4));
            position += 4;
            return value;
        }

        public uint ReadUInt() {
            Require(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(Window(4));
            position += 4;
            return value;
        }

        public long ReadLong() {
            Require(8);
            long value = BinaryPrimitives.ReadInt64LittleEndian(Window(8));
            position += 8;
            return value;
        }

        public ulong ReadULong() {
            Require(8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Window(8));
            position += 8;
            return value;
        }

        public float ReadFloat() {
            return BitConverter.Int32BitsToSingle(ReadInt());
        }

        /// <summary>
        /// Decodes an LEB128-style var-uint. Five payload bytes is the maximum a 32-bit value can occupy,
        /// so a sixth continuation byte means the stream is corrupt and is rejected rather than wrapped.
        /// </summary>
        public uint ReadVarUInt() {
            uint result = 0u;
            int shift = 0;
            while (shift <= 28) {
                byte current = ReadByte();
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0) {
                    return result;
                }

                shift += 7;
            }

            throw new NetProtocolException("NetReader encountered a var-uint longer than five bytes.");
        }

        /// <summary>UTF-8 text with a var-uint byte-length prefix. Zero length decodes to <see cref="string.Empty"/>.</summary>
        public string ReadString() {
            uint byteCount = ReadVarUInt();
            if (byteCount == 0u) {
                return string.Empty;
            }

            Require((int)byteCount);
            string value = Encoding.UTF8.GetString(Window((int)byteCount));
            position += (int)byteCount;
            return value;
        }

        /// <summary>Reads a var-uint length prefix and copies the payload into a new array.</summary>
        public byte[] ReadBytes() {
            uint byteCount = ReadVarUInt();
            if (byteCount == 0u) {
                return Array.Empty<byte>();
            }

            Require((int)byteCount);
            var value = new byte[byteCount];
            Array.Copy(buffer, origin + position, value, 0, (int)byteCount);
            position += (int)byteCount;
            return value;
        }

        /// <summary>
        /// Reads a var-uint length prefix and returns the payload as a window onto the source buffer.
        /// Allocation-free, which is what the chat envelope path wants — but the span is only valid until
        /// the underlying buffer is recycled, so it must be consumed before the poll loop moves on.
        /// </summary>
        public ReadOnlySpan<byte> ReadBytesSpan() {
            uint byteCount = ReadVarUInt();
            Require((int)byteCount);
            ReadOnlySpan<byte> value = Window((int)byteCount);
            position += (int)byteCount;
            return value;
        }

        /// <summary>Returns the remainder of the window without a length prefix, and consumes it.</summary>
        public ReadOnlySpan<byte> ReadRemaining() {
            ReadOnlySpan<byte> value = Window(Remaining);
            position = length;
            return value;
        }

        public Vector3 ReadVector3() {
            float x = ReadFloat();
            float y = ReadFloat();
            float z = ReadFloat();
            return new Vector3(x, y, z);
        }

        /// <summary>Decodes a quantized yaw back into degrees within [0, 360).</summary>
        public float ReadQuantizedYaw() {
            return NetQuantization.DecodeYaw(ReadUShort());
        }

        /// <summary>Decodes a fixed-point velocity triple.</summary>
        public Vector3 ReadQuantizedVelocity() {
            float x = NetQuantization.DecodeVelocityComponent(ReadShort());
            float y = NetQuantization.DecodeVelocityComponent(ReadShort());
            float z = NetQuantization.DecodeVelocityComponent(ReadShort());
            return new Vector3(x, y, z);
        }

        /// <summary>Deserializes a message struct in place — the mirror of <see cref="NetWriter.WriteMessage"/>.</summary>
        public TMessage ReadMessage<TMessage>() where TMessage : struct, INetMessage {
            TMessage message = default;
            message.Deserialize(ref this);
            return message;
        }

        private ReadOnlySpan<byte> Window(int count) {
            return new ReadOnlySpan<byte>(buffer, origin + position, count);
        }

        private void Require(int count) {
            if (count >= 0 && position + count <= length) {
                return;
            }

            throw new NetProtocolException(
                $"NetReader underflow: {count} byte(s) requested with {length - position} remaining of {length}.");
        }
    }
}
