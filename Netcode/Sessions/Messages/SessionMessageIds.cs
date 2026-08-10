namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The wire ids of every session-layer message, band 64-127 of the protocol id map.
    /// </summary>
    /// <remarks>
    /// These numbers are a compatibility contract with every shipped build: an id may be retired but
    /// never repurposed, because an old client that still speaks it would decode a different payload
    /// into the same handler. Ids 120 and 122-127 are deliberately left unused — they are reserved for
    /// listen-host process migration, which is designed for but not built in v1.
    /// </remarks>
    public static class SessionMessageIds {
        /// <summary>Client to server: the identity claim that opens the handshake.</summary>
        public const ushort AuthRequest = 64;

        /// <summary>Server to client: the validator's verdict and the assigned peer id.</summary>
        public const ushort AuthResponse = 65;

        /// <summary>Server to client: config, roster and phase for a session just attached to.</summary>
        public const ushort JoinAccepted = 66;

        /// <summary>Server to session: a member arrived, freshly or by rejoin.</summary>
        public const ushort MemberJoined = 67;

        /// <summary>Server to session: a member left the roster.</summary>
        public const ushort MemberLeft = 68;

        /// <summary>Server to session: the phase machine advanced.</summary>
        public const ushort PhaseChanged = 69;

        /// <summary>Client to server: the owner asks to launch a match.</summary>
        public const ushort LaunchMatchRequest = 70;

        /// <summary>Server to client: the launch was refused.</summary>
        public const ushort LaunchMatchDenied = 71;

        /// <summary>Server to session: load this match and report ready.</summary>
        public const ushort MatchLoad = 72;

        /// <summary>Client to server: this client finished loading the given match run.</summary>
        public const ushort ClientReady = 73;

        /// <summary>Server to session: the ready barrier cleared, simulation begins.</summary>
        public const ushort MatchStart = 74;

        /// <summary>Server to session: the match finished, with its result blob.</summary>
        public const ushort MatchEnd = 75;

        /// <summary>Server to session: leave the results screen and load the lobby again.</summary>
        public const ushort ReturnToLobby = 76;

        /// <summary>Server to client: you were removed by the owner or an admin.</summary>
        public const ushort Kick = 77;

        /// <summary>Server to session: the whole session is shutting down.</summary>
        public const ushort SessionClosing = 78;

        /// <summary>Client to server: a graceful leave, sent before the transport closes.</summary>
        public const ushort LeaveNotice = 79;

        /// <summary>Client to server: mint a new session at the front desk.</summary>
        public const ushort CreateSessionRequest = 80;

        /// <summary>Server to client: the session was minted, here is its join code.</summary>
        public const ushort SessionCreated = 81;

        /// <summary>Client to server: attach me to the session behind this join code.</summary>
        public const ushort JoinSessionRequest = 82;

        /// <summary>Server to client: the attach was refused, with the reason code.</summary>
        public const ushort JoinSessionDenied = 83;

        /// <summary>Server to session: ownership moved to another member under TransferToMember.</summary>
        public const ushort OwnerChanged = 121;
    }
}
