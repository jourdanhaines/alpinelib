using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// A player asking the server to say something.
    /// </summary>
    /// <remarks>
    /// The nonce is the client's own counter, echoed back in <see cref="ChatSendAck"/>. It exists
    /// because the sender needs to know which of several in-flight lines a ruling refers to — the server
    /// assigns message ids only to lines it accepts, so a rejection has no id to identify itself by.
    /// </remarks>
    public struct ChatSendRequest : INetMessage {
        /// <summary>Creates a request for one line.</summary>
        public ChatSendRequest(uint nonce, ChatChannelId channel, string text) {
            Nonce = nonce;
            Channel = channel;
            Text = text ?? string.Empty;
        }

        /// <summary>Client-assigned correlation id, unique among the sender's in-flight requests.</summary>
        public uint Nonce { get; set; }

        /// <summary>Where the line should go.</summary>
        public ChatChannelId Channel { get; set; }

        /// <summary>What the player typed, before any server-side filtering.</summary>
        public string Text { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string text = Text ?? string.Empty;

            if (text.Length > ChatMessage.MaxTextLength) {
                throw new NetProtocolException("Chat text of " + text.Length.ToString()
                    + " characters exceeds the cap of " + ChatMessage.MaxTextLength.ToString() + ".");
            }

            writer.WriteVarUInt(Nonce);
            Channel.Serialize(ref writer);
            writer.WriteString(text);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Nonce = reader.ReadVarUInt();
            Channel = ChatChannelId.Deserialize(ref reader);
            Text = reader.ReadString() ?? string.Empty;

            if (Text.Length > ChatMessage.MaxTextLength) {
                throw new NetProtocolException("Chat text declared " + Text.Length.ToString()
                    + " characters, which exceeds the cap of " + ChatMessage.MaxTextLength.ToString() + ".");
            }
        }
    }
}
