using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// A page of past messages, oldest first, answering one <see cref="ChatHistoryRequest"/>.
    /// </summary>
    /// <remarks>
    /// Also the frame used for the history push a joining player receives, in which case
    /// <see cref="RequestId"/> is zero: nobody asked, the server volunteered. Ordering oldest first
    /// means a client can append the page straight into its view without reversing it.
    /// </remarks>
    public struct ChatHistoryResponse : INetMessage {
        /// <summary>Creates a response carrying the given messages.</summary>
        public ChatHistoryResponse(uint requestId, ChatChannelId channel, List<ChatMessage> messages) {
            RequestId = requestId;
            Channel = channel;
            Messages = messages ?? new List<ChatMessage>();
        }

        /// <summary>The request being answered, or zero for an unsolicited history push.</summary>
        public uint RequestId { get; set; }

        /// <summary>Which channel these messages belong to.</summary>
        public ChatChannelId Channel { get; set; }

        /// <summary>The page, oldest first.</summary>
        public List<ChatMessage> Messages { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            List<ChatMessage> messages = Messages ?? new List<ChatMessage>();

            if (messages.Count > ChatHistoryRequest.MaxCount) {
                throw new NetProtocolException("Chat history page of " + messages.Count.ToString()
                    + " messages exceeds the cap of " + ChatHistoryRequest.MaxCount.ToString() + ".");
            }

            writer.WriteVarUInt(RequestId);
            Channel.Serialize(ref writer);
            writer.WriteVarUInt((uint)messages.Count);

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++) {
                ChatMessage message = messages[messageIndex] ?? new ChatMessage();
                message.Serialize(ref writer);
            }
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            RequestId = reader.ReadVarUInt();
            Channel = ChatChannelId.Deserialize(ref reader);

            int count = (int)reader.ReadVarUInt();

            if (count > ChatHistoryRequest.MaxCount) {
                throw new NetProtocolException("Chat history page declared " + count.ToString()
                    + " messages, which exceeds the cap of " + ChatHistoryRequest.MaxCount.ToString() + ".");
            }

            List<ChatMessage> messages = new List<ChatMessage>(count);

            for (int messageIndex = 0; messageIndex < count; messageIndex++) {
                ChatMessage message = new ChatMessage();
                message.Deserialize(ref reader);
                messages.Add(message);
            }

            Messages = messages;
        }
    }
}
