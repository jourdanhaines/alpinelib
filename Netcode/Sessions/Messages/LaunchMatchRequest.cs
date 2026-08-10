using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The owner asking the server to start a match.
    /// </summary>
    /// <remarks>
    /// A request, never a command: the server checks the sender really is the owner, that the phase is
    /// Lobby, and that the match id exists in the session config. Anything else comes back as
    /// <see cref="LaunchMatchDenied"/>.
    /// </remarks>
    public struct LaunchMatchRequest : INetMessage {
        /// <summary>Longest match id accepted on the wire.</summary>
        public const int MaxMatchIdLength = 64;

        /// <summary>Creates a request for one match.</summary>
        public LaunchMatchRequest(string matchId) {
            MatchId = matchId ?? string.Empty;
        }

        /// <summary>Wire id of the match to launch, as authored in the match definition.</summary>
        public string MatchId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string matchId = MatchId ?? string.Empty;

            if (matchId.Length > MaxMatchIdLength) {
                throw new NetProtocolException("LaunchMatchRequest match id of " + matchId.Length.ToString()
                    + " characters exceeds the cap of " + MaxMatchIdLength.ToString() + ".");
            }

            writer.WriteString(matchId);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            MatchId = reader.ReadString();

            if (MatchId.Length > MaxMatchIdLength) {
                throw new NetProtocolException("LaunchMatchRequest declared a match id of "
                    + MatchId.Length.ToString() + " characters, which exceeds the cap of "
                    + MaxMatchIdLength.ToString() + ".");
            }
        }
    }
}
