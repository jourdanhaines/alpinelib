using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// A client reporting that it has finished loading and is standing by.
    /// </summary>
    /// <remarks>
    /// Stamped with the match sequence it answers. Without that stamp a ready arriving late from the
    /// previous run would count towards the current barrier and start a match under someone who is
    /// still on a loading screen; the server drops any stamp that is not the running one.
    /// </remarks>
    public struct ClientReady : INetMessage {
        /// <summary>Creates a ready report for one match run.</summary>
        public ClientReady(int matchSequence) {
            MatchSequence = matchSequence;
        }

        /// <summary>Which run of the match this report answers, from the match context.</summary>
        public int MatchSequence { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteInt(MatchSequence);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            MatchSequence = reader.ReadInt();
        }
    }
}
