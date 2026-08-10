using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The front desk refusing a create or join attempt.
    /// </summary>
    /// <remarks>
    /// An enum rather than free text: the client shows different copy and offers different recovery for
    /// a mistyped code, a full igloo, and a match already in progress, so the reason has to be
    /// machine-readable. The connection survives the refusal — it stays authenticated at the front desk
    /// and may try another code.
    /// </remarks>
    public struct JoinSessionDenied : INetMessage {
        /// <summary>Creates a refusal carrying its reason code.</summary>
        public JoinSessionDenied(SessionEndReason reason) {
            Reason = reason;
        }

        /// <summary>Why the attach was refused.</summary>
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
