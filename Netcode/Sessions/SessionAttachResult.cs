namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// What came of handing a peer to a <see cref="SessionHost"/>: the member it became, or the reason
    /// it was turned away.
    /// </summary>
    /// <remarks>
    /// The host performs the admission and sends the newcomer its <c>JoinAccepted</c>, but it does not
    /// send denials — a refused peer is still the front desk's guest, and only the front desk knows
    /// whether to answer with <c>JoinSessionDenied</c>, offer another session, or drop the connection.
    /// This result is how that decision gets the facts it needs.
    /// </remarks>
    public readonly struct SessionAttachResult {
        private readonly bool _isAccepted;
        private readonly SessionEndReason _denialReason;
        private readonly SessionMember _member;
        private readonly bool _isRejoin;

        private SessionAttachResult(bool isAccepted, SessionEndReason denialReason, SessionMember member, bool isRejoin) {
            _isAccepted = isAccepted;
            _denialReason = denialReason;
            _member = member;
            _isRejoin = isRejoin;
        }

        /// <summary>True when the peer is now a member of the session.</summary>
        public bool IsAccepted => _isAccepted;

        /// <summary>Why the peer was turned away. Meaningless when <see cref="IsAccepted"/> is true.</summary>
        public SessionEndReason DenialReason => _denialReason;

        /// <summary>The roster entry the peer now owns, or null on a denial.</summary>
        public SessionMember Member => _member;

        /// <summary>True when an existing disconnected reservation was reclaimed rather than a new seat taken.</summary>
        public bool IsRejoin => _isRejoin;

        /// <summary>The peer is in.</summary>
        public static SessionAttachResult Accepted(SessionMember member, bool isRejoin) {
            return new SessionAttachResult(true, SessionEndReason.HostClosed, member, isRejoin);
        }

        /// <summary>The peer is not in, for the given reason.</summary>
        public static SessionAttachResult Denied(SessionEndReason reason) {
            return new SessionAttachResult(false, reason, null, false);
        }
    }
}
