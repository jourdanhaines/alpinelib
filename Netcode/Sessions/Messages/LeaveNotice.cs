using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// A client bowing out on purpose, sent before it closes its own transport.
    /// </summary>
    /// <remarks>
    /// Empty: the server already knows which connection sent it, and the identity behind that
    /// connection was settled at auth. What the message buys is intent — a deliberate leave releases
    /// the roster slot immediately, where a silent drop would be held open as a rejoin reservation.
    /// </remarks>
    public struct LeaveNotice : INetMessage {
        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
        }
    }
}
