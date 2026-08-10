namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// A validator's answer to "is this player who they say they are?".
    /// </summary>
    /// <remarks>
    /// Carries a resolved identity rather than just a yes, because a real provider corrects what the
    /// client claimed: a Steam ticket determines the display name and the account it belongs to, and
    /// the server must use those, not the client's assertion.
    /// </remarks>
    public readonly struct AuthVerdict {
        private readonly bool _isAccepted;
        private readonly PlayerIdentity _identity;
        private readonly string _reason;

        private AuthVerdict(bool isAccepted, PlayerIdentity identity, string reason) {
            _isAccepted = isAccepted;
            _identity = identity;
            _reason = reason;
        }

        /// <summary>True when the connection may proceed to session attach.</summary>
        public bool IsAccepted => _isAccepted;

        /// <summary>The identity the server will use from here on. Null when rejected.</summary>
        public PlayerIdentity Identity => _identity;

        /// <summary>Human-readable rejection reason, echoed in <c>AuthResponse</c>. Empty when accepted.</summary>
        public string Reason => _reason ?? string.Empty;

        /// <summary>Accepts the connection under the given resolved identity.</summary>
        public static AuthVerdict Accept(PlayerIdentity identity) {
            return new AuthVerdict(true, identity, string.Empty);
        }

        /// <summary>Refuses the connection.</summary>
        public static AuthVerdict Reject(string reason) {
            return new AuthVerdict(false, null, reason ?? string.Empty);
        }
    }
}
