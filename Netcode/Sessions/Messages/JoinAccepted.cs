using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Everything a client needs the instant it becomes a member: the rules, the roster, the phase, and
    /// — when it arrives mid-match — the match it has to load.
    /// </summary>
    /// <remarks>
    /// The config travels here, verbatim from the server, so nobody ever plays by stale local rules.
    /// The match context is optional because a client joining a lobby has no match to load; a rejoining
    /// client landing in MatchLoading or MatchActive does, and that single message is what lets it skip
    /// straight back into the running match rather than replay the launch sequence it missed.
    /// </remarks>
    public struct JoinAccepted : INetMessage {
        private const byte RejoinFlag = 1 << 0;
        private const byte MatchContextFlag = 1 << 1;

        /// <summary>The rule set this session runs by.</summary>
        public SessionConfigData Config { get; set; }

        /// <summary>The full roster at the moment of the join, rejoin reservations included.</summary>
        public LobbySnapshot Lobby { get; set; }

        /// <summary>True when this join reclaimed an existing rejoin reservation.</summary>
        public bool IsRejoin { get; set; }

        /// <summary>Phase the session is in right now.</summary>
        public SessionPhase Phase { get; set; }

        /// <summary>The match to load, or null when the client is landing in the lobby.</summary>
        public MatchContextData MatchContext { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            (Config ?? new SessionConfigData()).Serialize(ref writer);
            (Lobby ?? new LobbySnapshot()).Serialize(ref writer);
            writer.WriteByte(PackFlags());
            writer.WriteByte((byte)Phase);

            if (MatchContext == null) {
                return;
            }

            MatchContext.Serialize(ref writer);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Config = new SessionConfigData();
            Config.Deserialize(ref reader);

            Lobby = new LobbySnapshot();
            Lobby.Deserialize(ref reader);

            byte flags = reader.ReadByte();
            IsRejoin = (flags & RejoinFlag) != 0;
            Phase = (SessionPhase)reader.ReadByte();

            if ((flags & MatchContextFlag) == 0) {
                MatchContext = null;
                return;
            }

            MatchContext = new MatchContextData();
            MatchContext.Deserialize(ref reader);
        }

        private byte PackFlags() {
            byte flags = 0;

            if (IsRejoin) {
                flags |= RejoinFlag;
            }

            if (MatchContext != null) {
                flags |= MatchContextFlag;
            }

            return flags;
        }
    }
}
