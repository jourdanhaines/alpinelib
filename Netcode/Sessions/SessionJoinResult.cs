namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// How a <see cref="SessionClient"/> request to reach the server ended — the value every one of its
    /// asynchronous entry points resolves to.
    /// </summary>
    /// <remarks>
    /// One result type covers connecting, creating and joining on purpose. From the caller's side those
    /// are three steps of one journey, and a menu that has to branch over three different failure shapes
    /// to say "that did not work, here is why" is three chances to get the message wrong.
    /// </remarks>
    public readonly struct SessionJoinResult {
        private readonly bool _isSuccess;
        private readonly SessionEndReason _reason;
        private readonly string _message;
        private readonly string _sessionId;
        private readonly string _joinCode;
        private readonly SessionPhase _phase;
        private readonly bool _isRejoin;

        private SessionJoinResult(
            bool isSuccess,
            SessionEndReason reason,
            string message,
            string sessionId,
            string joinCode,
            SessionPhase phase,
            bool isRejoin) {
            _isSuccess = isSuccess;
            _reason = reason;
            _message = message;
            _sessionId = sessionId;
            _joinCode = joinCode;
            _phase = phase;
            _isRejoin = isRejoin;
        }

        /// <summary>True when the request did what it was asked to.</summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>Why the request failed. Meaningless when <see cref="IsSuccess"/> is true.</summary>
        public SessionEndReason Reason => _reason;

        /// <summary>Human-readable detail from the server, when it sent any.</summary>
        public string Message => _message ?? string.Empty;

        /// <summary>Identifier of the session that was joined, or empty when none was.</summary>
        public string SessionId => _sessionId ?? string.Empty;

        /// <summary>Code friends use to join this session, or empty when none was.</summary>
        public string JoinCode => _joinCode ?? string.Empty;

        /// <summary>Phase the session was in at the moment of the join.</summary>
        public SessionPhase Phase => _phase;

        /// <summary>True when the server matched this player to a reservation left by an earlier drop.</summary>
        public bool IsRejoin => _isRejoin;

        /// <summary>Connected and authenticated, but attached to no session yet.</summary>
        public static SessionJoinResult Connected() {
            return new SessionJoinResult(true, SessionEndReason.HostClosed, string.Empty, string.Empty, string.Empty, SessionPhase.Lobby, false);
        }

        /// <summary>Attached to a session, whether freshly or by reclaiming a reservation.</summary>
        public static SessionJoinResult Joined(string sessionId, string joinCode, SessionPhase phase, bool isRejoin) {
            return new SessionJoinResult(true, SessionEndReason.HostClosed, string.Empty, sessionId, joinCode, phase, isRejoin);
        }

        /// <summary>The server said no.</summary>
        public static SessionJoinResult Denied(SessionEndReason reason, string message) {
            return new SessionJoinResult(false, reason, message, string.Empty, string.Empty, SessionPhase.Lobby, false);
        }
    }
}
