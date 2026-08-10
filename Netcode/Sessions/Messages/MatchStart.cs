using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The starting gun: the ready barrier cleared and the match is now simulating.
    /// </summary>
    /// <remarks>
    /// Deliberately empty. Which match is starting was already settled by the <see cref="MatchLoad"/>
    /// every participant acknowledged, so repeating it here would only create a second source of truth
    /// that could disagree. The message exists purely as the moment.
    /// </remarks>
    public struct MatchStart : INetMessage {
        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
        }
    }
}
