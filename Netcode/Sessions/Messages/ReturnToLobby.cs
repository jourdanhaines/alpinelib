using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The results hold is over; everyone loads the lobby scene again.
    /// </summary>
    /// <remarks>
    /// Empty, and paired with a <see cref="PhaseChanged"/> back to Lobby. The lobby scene name lives in
    /// the session config every client already holds, so there is nothing to carry: the session, its
    /// peers and its replication scope never went anywhere, only the scene did.
    /// </remarks>
    public struct ReturnToLobby : INetMessage {
        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
        }
    }
}
