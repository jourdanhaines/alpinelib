using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Sent to the one member being removed, just before their connection is closed.
    /// </summary>
    /// <remarks>
    /// The rest of the session learns about it from <see cref="MemberLeft"/> with
    /// <see cref="LeaveReason.Kicked"/>; this message exists only so the removed player sees why rather
    /// than a bare disconnect. The reason is free text for exactly that reason — it is copy, not a code
    /// anything branches on.
    /// </remarks>
    public struct Kick : INetMessage {
        /// <summary>Longest kick reason accepted on the wire.</summary>
        public const int MaxReasonLength = 256;

        /// <summary>Creates a kick carrying a human-readable reason.</summary>
        public Kick(string reason) {
            Reason = reason ?? string.Empty;
        }

        /// <summary>Why the member was removed, for display.</summary>
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
                throw new NetProtocolException("Kick declared a reason of " + Reason.Length.ToString()
                    + " characters, which exceeds the cap of " + MaxReasonLength.ToString() + ".");
            }
        }
    }
}
