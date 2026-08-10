using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The front desk confirming a freshly minted session, with the code friends will type to reach it.
    /// </summary>
    /// <remarks>
    /// The join code selects a session on this server; it never encodes an address. Clients already
    /// know where the server is from their matchmaking config, which is what lets a code stay six
    /// characters long and safe to read aloud.
    /// </remarks>
    public struct SessionCreated : INetMessage {
        /// <summary>Longest session id accepted on the wire.</summary>
        public const int MaxSessionIdLength = 64;

        /// <summary>Longest join code accepted on the wire.</summary>
        public const int MaxJoinCodeLength = 16;

        /// <summary>Creates the confirmation for one session.</summary>
        public SessionCreated(string sessionId, string joinCode) {
            SessionId = sessionId ?? string.Empty;
            JoinCode = joinCode ?? string.Empty;
        }

        /// <summary>Server-assigned session identity, unique within the process.</summary>
        public string SessionId { get; set; }

        /// <summary>The code friends type to join. Empty when join codes are disabled.</summary>
        public string JoinCode { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string sessionId = SessionId ?? string.Empty;
            string joinCode = JoinCode ?? string.Empty;

            if (sessionId.Length > MaxSessionIdLength || joinCode.Length > MaxJoinCodeLength) {
                throw new NetProtocolException("SessionCreated exceeds its identifier caps: session id "
                    + sessionId.Length.ToString() + "/" + MaxSessionIdLength.ToString() + ", join code "
                    + joinCode.Length.ToString() + "/" + MaxJoinCodeLength.ToString() + ".");
            }

            writer.WriteString(sessionId);
            writer.WriteString(joinCode);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            SessionId = reader.ReadString();
            JoinCode = reader.ReadString();

            if (SessionId.Length > MaxSessionIdLength || JoinCode.Length > MaxJoinCodeLength) {
                throw new NetProtocolException("SessionCreated declared identifiers beyond their caps: "
                    + "session id " + SessionId.Length.ToString() + "/" + MaxSessionIdLength.ToString()
                    + ", join code " + JoinCode.Length.ToString() + "/" + MaxJoinCodeLength.ToString() + ".");
            }
        }
    }
}
