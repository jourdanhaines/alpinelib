using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// A client asking for messages older than one it already has.
    /// </summary>
    /// <remarks>
    /// Paging is by message id rather than by offset, because ids are stable while offsets shift every
    /// time a new line arrives. A <see cref="BeforeMessageId"/> of zero means "the newest ones".
    /// </remarks>
    public struct ChatHistoryRequest : INetMessage {
        /// <summary>Largest page a client may ask for in one request.</summary>
        public const int MaxCount = 128;

        /// <summary>Creates a request for one page of history.</summary>
        public ChatHistoryRequest(uint requestId, ChatChannelId channel, ulong beforeMessageId, int count) {
            RequestId = requestId;
            Channel = channel;
            BeforeMessageId = beforeMessageId;
            Count = count;
        }

        /// <summary>Client-assigned correlation id, echoed in the response.</summary>
        public uint RequestId { get; set; }

        /// <summary>Which channel's history is wanted.</summary>
        public ChatChannelId Channel { get; set; }

        /// <summary>Return messages with ids below this. Zero asks for the newest page.</summary>
        public ulong BeforeMessageId { get; set; }

        /// <summary>How many messages to return, capped at <see cref="MaxCount"/> by the server.</summary>
        public int Count { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteVarUInt(RequestId);
            Channel.Serialize(ref writer);
            writer.WriteULong(BeforeMessageId);
            writer.WriteVarUInt((uint)ClampCount(Count));
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            RequestId = reader.ReadVarUInt();
            Channel = ChatChannelId.Deserialize(ref reader);
            BeforeMessageId = reader.ReadULong();
            Count = ClampCount((int)reader.ReadVarUInt());
        }

        private static int ClampCount(int count) {
            if (count < 0) {
                return 0;
            }

            return count > MaxCount ? MaxCount : count;
        }
    }
}
