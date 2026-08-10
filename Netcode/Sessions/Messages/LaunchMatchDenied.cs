using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The server refusing a <see cref="LaunchMatchRequest"/>.
    /// </summary>
    /// <remarks>
    /// The reason is free text rather than an enum because every refusal here is a rule the owner can
    /// act on immediately — too few players, unknown match id, wrong phase — and those read better as
    /// a sentence than as a code the client has to translate. Nothing in the protocol branches on it.
    /// </remarks>
    public struct LaunchMatchDenied : INetMessage {
        /// <summary>Longest refusal reason accepted on the wire.</summary>
        public const int MaxReasonLength = 256;

        /// <summary>Creates a refusal carrying a human-readable reason.</summary>
        public LaunchMatchDenied(string reason) {
            Reason = reason ?? string.Empty;
        }

        /// <summary>Why the launch was refused, for display to the owner.</summary>
        public string Reason { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string reason = Reason ?? string.Empty;

            if (reason.Length > MaxReasonLength) {
                reason = reason.Substring(0, MaxReasonLength);
            }

            writer.WriteString(reason);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Reason = reader.ReadString();

            if (Reason.Length > MaxReasonLength) {
                throw new NetProtocolException("LaunchMatchDenied declared a reason of "
                    + Reason.Length.ToString() + " characters, which exceeds the cap of "
                    + MaxReasonLength.ToString() + ".");
            }
        }
    }
}
