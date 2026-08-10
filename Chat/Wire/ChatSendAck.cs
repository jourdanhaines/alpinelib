using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// The server's ruling on one <see cref="ChatSendRequest"/>, matched to it by nonce.
    /// </summary>
    /// <remarks>
    /// Sent for accepted lines as well as rejected ones. The sender still receives the accepted line
    /// through the ordinary broadcast — the acknowledgement carries the id so the client can recognise
    /// its own message when it arrives rather than showing it twice.
    /// </remarks>
    public struct ChatSendAck : INetMessage {
        /// <summary>Creates an acknowledgement from a send result.</summary>
        public ChatSendAck(uint nonce, ChatSendResult result) {
            Nonce = nonce;
            Status = result.Status;
            MessageId = result.MessageId;
            RetryAfterMs = result.RetryAfterMs;
        }

        /// <summary>Creates an acknowledgement field by field.</summary>
        public ChatSendAck(uint nonce, ChatSendStatus status, ulong messageId, int retryAfterMs) {
            Nonce = nonce;
            Status = status;
            MessageId = messageId;
            RetryAfterMs = retryAfterMs < 0 ? 0 : retryAfterMs;
        }

        /// <summary>The nonce of the request being answered.</summary>
        public uint Nonce { get; set; }

        /// <summary>What became of the line.</summary>
        public ChatSendStatus Status { get; set; }

        /// <summary>The id the accepted line was given. Zero for a rejection.</summary>
        public ulong MessageId { get; set; }

        /// <summary>How long to wait before retrying, milliseconds. Zero when retrying will not help.</summary>
        public int RetryAfterMs { get; set; }

        /// <summary>The ruling as the provider's public result type.</summary>
        public ChatSendResult ToResult() {
            return new ChatSendResult(Status, MessageId, RetryAfterMs);
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteVarUInt(Nonce);
            writer.WriteByte((byte)Status);
            writer.WriteULong(MessageId);
            writer.WriteVarUInt((uint)(RetryAfterMs < 0 ? 0 : RetryAfterMs));
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Nonce = reader.ReadVarUInt();
            Status = (ChatSendStatus)reader.ReadByte();
            MessageId = reader.ReadULong();
            RetryAfterMs = (int)reader.ReadVarUInt();
        }
    }
}
