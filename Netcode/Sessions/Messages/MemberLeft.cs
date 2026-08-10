using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Broadcast to the session when someone leaves the roster.
    /// </summary>
    /// <remarks>
    /// This message means the slot is gone. A transport drop under an active rejoin policy does NOT
    /// send it — the reservation is kept and the member simply flips to disconnected — so a client that
    /// receives it may safely destroy the pawn and drop the row.
    /// </remarks>
    public struct MemberLeft : INetMessage {
        /// <summary>Creates the broadcast for one departure.</summary>
        public MemberLeft(PlayerId playerId, LeaveReason leaveReason) {
            PlayerId = playerId;
            LeaveReason = leaveReason;
        }

        /// <summary>Identity of the member who left.</summary>
        public PlayerId PlayerId { get; set; }

        /// <summary>Why they left.</summary>
        public LeaveReason LeaveReason { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            PlayerId.Serialize(ref writer);
            writer.WriteByte((byte)LeaveReason);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            PlayerId = PlayerId.Deserialize(ref reader);
            LeaveReason = (LeaveReason)reader.ReadByte();
        }
    }
}
