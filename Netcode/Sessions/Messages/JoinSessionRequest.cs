using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// An authenticated but unattached connection asking to be let into a session by its code.
    /// </summary>
    /// <remarks>
    /// The player id is repeated here, even though auth already established it, because this is the
    /// message the rejoin path runs through: the front desk matches it against the target session's
    /// disconnected reservations, and a hit reclaims the original slot instead of taking a new one. A
    /// server that disagrees with the value seen at auth refuses the attach.
    /// </remarks>
    public struct JoinSessionRequest : INetMessage {
        /// <summary>Longest join code accepted on the wire.</summary>
        public const int MaxJoinCodeLength = 16;

        /// <summary>Creates a request to attach to the session behind a code.</summary>
        public JoinSessionRequest(string joinCode, PlayerId playerId) {
            JoinCode = joinCode ?? string.Empty;
            PlayerId = playerId;
        }

        /// <summary>The code identifying which session on this server to attach to.</summary>
        public string JoinCode { get; set; }

        /// <summary>Stable identity of the joining player, matched against rejoin reservations.</summary>
        public PlayerId PlayerId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string joinCode = JoinCode ?? string.Empty;

            if (joinCode.Length > MaxJoinCodeLength) {
                throw new NetProtocolException("JoinSessionRequest join code of " + joinCode.Length.ToString()
                    + " characters exceeds the cap of " + MaxJoinCodeLength.ToString() + ".");
            }

            writer.WriteString(joinCode);
            PlayerId.Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            JoinCode = reader.ReadString();

            if (JoinCode.Length > MaxJoinCodeLength) {
                throw new NetProtocolException("JoinSessionRequest declared a join code of "
                    + JoinCode.Length.ToString() + " characters, which exceeds the cap of "
                    + MaxJoinCodeLength.ToString() + ".");
            }

            PlayerId = PlayerId.Deserialize(ref reader);
        }
    }
}
