using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Tells every participant which match to load and who is in it.
    /// </summary>
    /// <remarks>
    /// Sent as the session enters MatchLoading. Receiving it opens the ready barrier: each client loads
    /// the scene and answers with <see cref="ClientReady"/> stamped with the same match sequence, so a
    /// slow loader from the previous run cannot satisfy this one.
    /// </remarks>
    public struct MatchLoad : INetMessage {
        /// <summary>Creates the load order for one match run.</summary>
        public MatchLoad(MatchContextData matchContext) {
            MatchContext = matchContext;
        }

        /// <summary>Which match, which scene, which participants, which run.</summary>
        public MatchContextData MatchContext { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            (MatchContext ?? new MatchContextData()).Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            MatchContext = new MatchContextData();
            MatchContext.Deserialize(ref reader);
        }
    }
}
