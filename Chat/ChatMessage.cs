using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// One delivered chat line, exactly as every client sees it.
    /// </summary>
    /// <remarks>
    /// The server stamps <see cref="MessageId"/> and <see cref="SentAtUnixMs"/>; clients never invent
    /// either. The id is monotonic per channel, which is what lets a late joiner merge a history push
    /// with the live messages already arriving without duplicating or reordering anything.
    /// <para>
    /// A class rather than a struct because it is held in ring buffers and UI lists and passed to
    /// event handlers far more often than it is serialised.
    /// </para>
    /// </remarks>
    public sealed class ChatMessage {
        /// <summary>Longest text accepted on the wire. Policy caps sit below this; this is the hard stop.</summary>
        public const int MaxTextLength = 512;

        /// <summary>Longest display name accepted on the wire.</summary>
        public const int MaxDisplayNameLength = 64;

        /// <summary>Creates an empty message, ready to be deserialised into.</summary>
        public ChatMessage() {
            SenderDisplayName = string.Empty;
            Text = string.Empty;
            Kind = ChatMessageKind.Player;
        }

        /// <summary>Creates a fully populated message.</summary>
        public ChatMessage(
            ulong messageId,
            ChatChannelId channel,
            PlayerId senderId,
            string senderDisplayName,
            string text,
            long sentAtUnixMs,
            ChatMessageKind kind) {
            MessageId = messageId;
            Channel = channel;
            SenderId = senderId;
            SenderDisplayName = senderDisplayName ?? string.Empty;
            Text = text ?? string.Empty;
            SentAtUnixMs = sentAtUnixMs;
            Kind = kind;
        }

        /// <summary>Server-assigned sequence number, monotonic within <see cref="Channel"/>.</summary>
        public ulong MessageId { get; set; }

        /// <summary>Where the line was said.</summary>
        public ChatChannelId Channel { get; set; }

        /// <summary>Who said it. <see cref="PlayerId.None"/> for server-authored lines.</summary>
        public PlayerId SenderId { get; set; }

        /// <summary>The sender's name at the moment of sending, snapshotted so history stays readable.</summary>
        public string SenderDisplayName { get; set; }

        /// <summary>The line itself, already filtered and sanitised by the server pipeline.</summary>
        public string Text { get; set; }

        /// <summary>Server clock at acceptance, Unix milliseconds.</summary>
        public long SentAtUnixMs { get; set; }

        /// <summary>How the UI should render the line.</summary>
        public ChatMessageKind Kind { get; set; }

        /// <summary>True when the server authored the line rather than a player.</summary>
        public bool IsFromServer => Kind != ChatMessageKind.Player;

        /// <summary>Builds a server-authored notice on a channel.</summary>
        public static ChatMessage FromSystem(ulong messageId, ChatChannelId channel, string text, long sentAtUnixMs) {
            return new ChatMessage(
                messageId,
                channel,
                PlayerId.None,
                string.Empty,
                text,
                sentAtUnixMs,
                ChatMessageKind.System);
        }

        /// <summary>Writes the message to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            string displayName = SenderDisplayName ?? string.Empty;
            string text = Text ?? string.Empty;

            if (displayName.Length > MaxDisplayNameLength) {
                throw new NetProtocolException("Chat display name of " + displayName.Length.ToString()
                    + " characters exceeds the cap of " + MaxDisplayNameLength.ToString() + ".");
            }

            if (text.Length > MaxTextLength) {
                throw new NetProtocolException("Chat text of " + text.Length.ToString()
                    + " characters exceeds the cap of " + MaxTextLength.ToString() + ".");
            }

            writer.WriteULong(MessageId);
            Channel.Serialize(ref writer);
            SenderId.Serialize(ref writer);
            writer.WriteString(displayName);
            writer.WriteString(text);
            writer.WriteLong(SentAtUnixMs);
            writer.WriteByte((byte)Kind);
        }

        /// <summary>Reads a message written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            MessageId = reader.ReadULong();
            Channel = ChatChannelId.Deserialize(ref reader);
            SenderId = PlayerId.Deserialize(ref reader);
            SenderDisplayName = ReadCapped(ref reader, MaxDisplayNameLength, "display name");
            Text = ReadCapped(ref reader, MaxTextLength, "text");
            SentAtUnixMs = reader.ReadLong();
            Kind = (ChatMessageKind)reader.ReadByte();
        }

        private static string ReadCapped(ref NetReader reader, int maxLength, string fieldName) {
            string value = reader.ReadString() ?? string.Empty;

            if (value.Length > maxLength) {
                throw new NetProtocolException("Chat " + fieldName + " declared " + value.Length.ToString()
                    + " characters, which exceeds the cap of " + maxLength.ToString() + ".");
            }

            return value;
        }
    }
}
