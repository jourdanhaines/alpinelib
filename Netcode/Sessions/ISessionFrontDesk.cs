using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Whoever owns the authenticated-but-unattached connections and decides which
    /// <see cref="SessionHost"/> each of them belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="SessionHost"/> deliberately knows nothing about session lookup, join-code minting or
    /// the one-session-per-connection rule: several hosts share one socket and one
    /// <see cref="Protocol.MessageRouter"/>, so exactly one object must claim the create/join message ids
    /// and route what arrives on them. That object is the front desk — the dedicated server's session
    /// registry, or the listen-host adapter in Unity.
    /// </para>
    /// <para>
    /// Both methods are called on the tick thread with a peer whose identity has already been
    /// established, and both are responsible for their own replies: <c>SessionCreated</c> before handing
    /// the peer to a host, or <c>JoinSessionDenied</c> when no host will take it. The host itself sends
    /// only <c>JoinAccepted</c>, because only it knows the roster that message carries.
    /// </para>
    /// </remarks>
    public interface ISessionFrontDesk {
        /// <summary>Stands up a new session for this peer and attaches it as the owner.</summary>
        /// <param name="peer">The authenticated connection asking for a session of its own.</param>
        /// <param name="identity">Who the connection was proven to be.</param>
        /// <param name="profileId">Which session profile to build from; empty means the default.</param>
        void HandleCreateSession(PeerHandle peer, PlayerIdentity identity, string profileId);

        /// <summary>Finds the session a join code selects and attaches this peer to it.</summary>
        /// <param name="peer">The authenticated connection asking to join.</param>
        /// <param name="identity">Who the connection was proven to be; its player id also decides rejoins.</param>
        /// <param name="joinCode">The code the player typed, in whatever case they typed it.</param>
        void HandleJoinSession(PeerHandle peer, PlayerIdentity identity, string joinCode);
    }
}
