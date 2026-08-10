using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// The one place that knows how a chat frame is laid out: a type byte followed by the frame's own
    /// fields, written with the same <see cref="NetWriter"/> as everything else in the protocol.
    /// </summary>
    /// <remarks>
    /// Chat frames are not routed messages. They ride as the opaque payload of a single netcode envelope
    /// (message id 192, reliable chat channel), which keeps chat's evolution independent of the
    /// protocol-wide id map — adding a frame type here never renumbers anything else, and the netcode
    /// router never learns what chat is saying.
    /// <para>
    /// The write and read halves are deliberately per-frame overloads rather than one generic pair. A
    /// generic that took the type byte as an argument would let a caller pair a
    /// <see cref="ChatWireMessageType.Broadcast"/> tag with an acknowledgement body, and the mistake
    /// would only surface as a decode failure on another machine.
    /// </para>
    /// </remarks>
    public static class ChatWireCodec {
        /// <summary>
        /// Buffer size that comfortably holds any frame this codec produces: a full history page of
        /// maximum-length messages is the worst case, and the server caps pages well below that.
        /// </summary>
        public const int MaxFrameBytes = 8192;

        /// <summary>Rents nothing and allocates a buffer big enough for any frame.</summary>
        public static byte[] CreateBuffer() {
            return new byte[MaxFrameBytes];
        }

        /// <summary>Writes a send request. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatSendRequest frame) {
            return WriteFrame(buffer, ChatWireMessageType.SendRequest, frame);
        }

        /// <summary>Writes a send acknowledgement. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatSendAck frame) {
            return WriteFrame(buffer, ChatWireMessageType.SendAck, frame);
        }

        /// <summary>Writes a delivered line. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatBroadcast frame) {
            return WriteFrame(buffer, ChatWireMessageType.Broadcast, frame);
        }

        /// <summary>Writes a history request. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatHistoryRequest frame) {
            return WriteFrame(buffer, ChatWireMessageType.HistoryRequest, frame);
        }

        /// <summary>Writes a page of history. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatHistoryResponse frame) {
            return WriteFrame(buffer, ChatWireMessageType.HistoryResponse, frame);
        }

        /// <summary>Writes a channel membership change. Returns the number of bytes written.</summary>
        public static int Write(byte[] buffer, in ChatChannelEvent frame) {
            return WriteFrame(buffer, ChatWireMessageType.ChannelEvent, frame);
        }

        /// <summary>
        /// Opens a received payload for reading and consumes its type byte, so the caller can switch on
        /// the type and then call the matching read method with the same reader.
        /// </summary>
        public static NetReader OpenPayload(ArraySegment<byte> payload, out ChatWireMessageType messageType) {
            NetReader reader = new NetReader(payload);
            messageType = ReadType(ref reader);
            return reader;
        }

        /// <summary>Reads and validates the leading type byte.</summary>
        public static ChatWireMessageType ReadType(ref NetReader reader) {
            byte raw = reader.ReadByte();
            ChatWireMessageType messageType = (ChatWireMessageType)raw;

            if (!IsKnownType(messageType)) {
                throw new NetProtocolException("Unknown chat frame type " + raw.ToString() + ".");
            }

            return messageType;
        }

        /// <summary>Reads a send request whose type byte has already been consumed.</summary>
        public static ChatSendRequest ReadSendRequest(ref NetReader reader) {
            return reader.ReadMessage<ChatSendRequest>();
        }

        /// <summary>Reads a send acknowledgement whose type byte has already been consumed.</summary>
        public static ChatSendAck ReadSendAck(ref NetReader reader) {
            return reader.ReadMessage<ChatSendAck>();
        }

        /// <summary>Reads a delivered line whose type byte has already been consumed.</summary>
        public static ChatBroadcast ReadBroadcast(ref NetReader reader) {
            return reader.ReadMessage<ChatBroadcast>();
        }

        /// <summary>Reads a history request whose type byte has already been consumed.</summary>
        public static ChatHistoryRequest ReadHistoryRequest(ref NetReader reader) {
            return reader.ReadMessage<ChatHistoryRequest>();
        }

        /// <summary>Reads a page of history whose type byte has already been consumed.</summary>
        public static ChatHistoryResponse ReadHistoryResponse(ref NetReader reader) {
            return reader.ReadMessage<ChatHistoryResponse>();
        }

        /// <summary>Reads a channel membership change whose type byte has already been consumed.</summary>
        public static ChatChannelEvent ReadChannelEvent(ref NetReader reader) {
            return reader.ReadMessage<ChatChannelEvent>();
        }

        private static bool IsKnownType(ChatWireMessageType messageType) {
            switch (messageType) {
                case ChatWireMessageType.SendRequest:
                case ChatWireMessageType.SendAck:
                case ChatWireMessageType.Broadcast:
                case ChatWireMessageType.HistoryRequest:
                case ChatWireMessageType.HistoryResponse:
                case ChatWireMessageType.ChannelEvent:
                    return true;
                default:
                    return false;
            }
        }

        private static int WriteFrame<TFrame>(byte[] buffer, ChatWireMessageType messageType, in TFrame frame)
            where TFrame : struct, INetMessage {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            }

            NetWriter writer = new NetWriter(buffer);
            writer.WriteByte((byte)messageType);
            writer.WriteMessage(frame);
            return writer.Written;
        }
    }
}
