using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Sessions {
    /// <summary>One roster row, copied out of the game thread for a caller on another one.</summary>
    /// <remarks>
    /// A copy rather than the live <see cref="SessionMember"/>: that object is mutated by the tick, and
    /// handing it to a request handler would mean serialising a roster that is being rewritten as it is
    /// read.
    /// </remarks>
    public sealed class SessionMemberSnapshot {
        public SessionMemberSnapshot(PlayerId playerId, string displayName, bool isOwner, bool isConnected) {
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
            IsOwner = isOwner;
            IsConnected = isConnected;
        }

        /// <summary>Stable identity of the player holding this seat.</summary>
        public PlayerId PlayerId { get; }

        /// <summary>Name other members see.</summary>
        public string DisplayName { get; }

        /// <summary>True for the member who may launch matches and kick.</summary>
        public bool IsOwner { get; }

        /// <summary>False while the seat is only a rejoin reservation.</summary>
        public bool IsConnected { get; }
    }
}
