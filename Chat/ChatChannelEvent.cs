using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// A membership change on a chat channel, raised by <see cref="IChatProvider.ChannelChanged"/> and
    /// carried on the wire as its own frame.
    /// </summary>
    /// <remarks>
    /// A mutable struct because it is also a wire frame: <see cref="INetMessage.Deserialize"/> fills one
    /// in place, exactly like every other message in the protocol, so no allocation happens on the
    /// receive path.
    /// </remarks>
    public struct ChatChannelEvent : INetMessage {
        /// <summary>Builds an event describing a change to one player's membership.</summary>
        public ChatChannelEvent(ChatChannelId channel, ChatChannelChange change, PlayerId playerId, string displayName) {
            Channel = channel;
            Change = change;
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
        }

        /// <summary>The channel whose membership changed.</summary>
        public ChatChannelId Channel { get; set; }

        /// <summary>What the change was.</summary>
        public ChatChannelChange Change { get; set; }

        /// <summary>Whose membership changed. <see cref="Netcode.Sessions.PlayerId.None"/> for a channel closing.</summary>
        public PlayerId PlayerId { get; set; }

        /// <summary>The affected player's name, snapshotted so a leave line still reads correctly.</summary>
        public string DisplayName { get; set; }

        /// <summary>True when the event describes the local player's own subscription.</summary>
        public bool IsLocalSubscription => Change == ChatChannelChange.Joined || Change == ChatChannelChange.Left;

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            Channel.Serialize(ref writer);
            writer.WriteByte((byte)Change);
            PlayerId.Serialize(ref writer);
            writer.WriteString(DisplayName ?? string.Empty);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Channel = ChatChannelId.Deserialize(ref reader);
            Change = (ChatChannelChange)reader.ReadByte();
            PlayerId = AlpineLib.Netcode.Sessions.PlayerId.Deserialize(ref reader);
            DisplayName = reader.ReadString() ?? string.Empty;
        }
    }
}
