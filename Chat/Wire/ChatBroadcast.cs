using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// One delivered line on its way to a client.
    /// </summary>
    /// <remarks>
    /// A struct wrapper around the <see cref="ChatMessage"/> class so the frame plugs into the same
    /// <see cref="INetMessage"/> machinery as everything else on the wire. Deserialising allocates the
    /// message it fills, which is unavoidable: the message outlives the read.
    /// </remarks>
    public struct ChatBroadcast : INetMessage {
        /// <summary>Wraps a message for sending.</summary>
        public ChatBroadcast(ChatMessage message) {
            Message = message;
        }

        /// <summary>The line being delivered.</summary>
        public ChatMessage Message { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            ChatMessage message = Message ?? new ChatMessage();
            message.Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ChatMessage message = new ChatMessage();
            message.Deserialize(ref reader);
            Message = message;
        }
    }
}
