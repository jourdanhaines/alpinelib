using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The session is shutting down under every member at once.
    /// </summary>
    /// <remarks>
    /// The reason is the <see cref="SessionEndReason"/> enum, not free text, because clients branch on
    /// it: an owner closing the igloo returns to the main menu quietly, while a version mismatch or an
    /// auth failure needs its own copy and its own retry behaviour.
    /// </remarks>
    public struct SessionClosing : INetMessage {
        /// <summary>Creates the shutdown broadcast.</summary>
        public SessionClosing(SessionEndReason reason) {
            Reason = reason;
        }

        /// <summary>Why the session ended.</summary>
        public SessionEndReason Reason { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte((byte)Reason);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Reason = (SessionEndReason)reader.ReadByte();
        }
    }
}
