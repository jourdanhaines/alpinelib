using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Ownership of the session moved to another member.
    /// </summary>
    /// <remarks>
    /// This is lobby-owner reassignment under <see cref="HostPolicy.TransferToMember"/>, not host
    /// migration: the session keeps running on the same server, and only the right to launch matches
    /// and kick moves. Clients flip the owner bit on the named roster row and clear it everywhere else.
    /// </remarks>
    public struct OwnerChanged : INetMessage {
        /// <summary>Creates the reassignment broadcast.</summary>
        public OwnerChanged(PlayerId playerId) {
            PlayerId = playerId;
        }

        /// <summary>Identity of the member who is now the owner.</summary>
        public PlayerId PlayerId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            PlayerId.Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            PlayerId = PlayerId.Deserialize(ref reader);
        }
    }
}
