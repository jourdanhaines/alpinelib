using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Broadcast to the session when someone arrives.
    /// </summary>
    /// <remarks>
    /// <see cref="IsRejoin"/> is what stops the arrival reading as a stranger: a returning player
    /// reclaims a roster slot that was already on screen, so clients update the existing row and
    /// suppress the "player joined" flourish instead of appending a duplicate.
    /// </remarks>
    public struct MemberJoined : INetMessage {
        /// <summary>Creates the broadcast for one arrival.</summary>
        public MemberJoined(SessionMember member, bool isRejoin) {
            Member = member;
            IsRejoin = isRejoin;
        }

        /// <summary>The roster row, as it now stands.</summary>
        public SessionMember Member { get; set; }

        /// <summary>True when this arrival reclaimed a held reservation rather than taking a new slot.</summary>
        public bool IsRejoin { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            (Member ?? new SessionMember()).Serialize(ref writer);
            writer.WriteBool(IsRejoin);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Member = new SessionMember();
            Member.Deserialize(ref reader);
            IsRejoin = reader.ReadBool();
        }
    }
}
