using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The match is over; here is how it went.
    /// </summary>
    /// <remarks>
    /// The session layer never opens the result — scoring belongs to the game, and the payload inside
    /// <see cref="MatchResultData"/> is an opaque blob written and read by the game's own codec. That is
    /// what keeps a new minigame from needing a protocol change.
    /// </remarks>
    public struct MatchEnd : INetMessage {
        /// <summary>Creates the end broadcast for one match run.</summary>
        public MatchEnd(MatchResultData result) {
            Result = result;
        }

        /// <summary>Which match ended, which run, and the game's result blob.</summary>
        public MatchResultData Result { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            (Result ?? new MatchResultData()).Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Result = new MatchResultData();
            Result.Deserialize(ref reader);
        }
    }
}
