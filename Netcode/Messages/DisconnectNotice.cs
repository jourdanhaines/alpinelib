using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Messages {
    /// <summary>
    /// Sent by the server immediately before it closes a connection, so the client learns why.
    /// </summary>
    /// <remarks>
    /// Without it every server-side close looks identical to the client: the transport reports a
    /// graceful disconnect and nothing more, which is indistinguishable from the player's own quit. The
    /// notice arrives first and the client remembers it, so the disconnect that follows is reported with
    /// the real reason — a kick, a shutdown — and the UI can say something true.
    /// </remarks>
    public struct DisconnectNotice : INetMessage {
        /// <summary>Creates a notice with a reason and no explanatory text.</summary>
        public DisconnectNotice(DisconnectReason reason) : this(reason, string.Empty) { }

        /// <summary>Creates a notice with a reason and a human-readable explanation.</summary>
        public DisconnectNotice(DisconnectReason reason, string message) {
            Reason = reason;
            Message = message;
        }

        /// <summary>Why the server is closing the connection.</summary>
        public DisconnectReason Reason { get; set; }

        /// <summary>Optional text for the player; empty when there is nothing useful to say.</summary>
        public string Message { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte((byte)Reason);
            writer.WriteString(Message);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Reason = (DisconnectReason)reader.ReadByte();
            Message = reader.ReadString();
        }
    }
}
