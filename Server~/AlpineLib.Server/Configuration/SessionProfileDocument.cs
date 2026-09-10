using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of the Unity <c>SessionProfile</c> asset: the rules every session this server hosts
    /// runs by.
    /// </summary>
    /// <remarks>
    /// Enums are written as their names by the exporter and read back by name here, so a reordered enum
    /// cannot silently turn <c>TransferToMember</c> into <c>EndSession</c> in a deployed config.
    /// </remarks>
    public sealed class SessionProfileDocument {
        public string ProfileId { get; set; } = string.Empty;

        public SessionLifetimeMode LifetimeMode { get; set; } = SessionLifetimeMode.LobbyScoped;

        public HostPolicy HostPolicy { get; set; } = HostPolicy.TransferToMember;

        public RejoinPolicy RejoinPolicy { get; set; } = RejoinPolicy.AnyTime;

        public float RejoinWindowSeconds { get; set; } = 120f;

        public int MaxPlayers { get; set; } = 8;

        public float ReadyTimeoutSeconds { get; set; } = 30f;

        public LateLoadPolicy LateLoadPolicy { get; set; } = LateLoadPolicy.DropToLobby;

        /// <summary>Reserved; keep false. Mid-match arrivals come back through rejoin, not fresh joins.</summary>
        public bool AllowJoinDuringMatch { get; set; }

        public float ResultsHoldSeconds { get; set; } = 8f;

        public float EmptyShutdownSeconds { get; set; } = 300f;

        /// <summary>Maps this document onto the shared profile the session host reads.</summary>
        public SessionProfileData ToData() {
            return new SessionProfileData {
                ProfileId = ProfileId,
                LifetimeMode = LifetimeMode,
                HostPolicy = HostPolicy,
                RejoinPolicy = RejoinPolicy,
                RejoinWindowSeconds = RejoinWindowSeconds,
                MaxPlayers = MaxPlayers,
                ReadyTimeoutSeconds = ReadyTimeoutSeconds,
                LateLoadPolicy = LateLoadPolicy,
                AllowJoinDuringMatch = AllowJoinDuringMatch,
                ResultsHoldSeconds = ResultsHoldSeconds,
                EmptyShutdownSeconds = EmptyShutdownSeconds
            };
        }
    }
}
