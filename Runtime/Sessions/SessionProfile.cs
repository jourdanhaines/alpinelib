using AlpineLib.Netcode.Sessions;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// The authored rules a session runs by: how long it lives, what happens when its owner leaves,
    /// whether a dropped player may come back, and how patient it is with slow loaders.
    /// </summary>
    /// <remarks>
    /// Converted to <see cref="SessionProfileData"/>, which the server sends to every client in
    /// <c>JoinAccepted</c> — so a client never has to ship the same asset as the server it joined, and a
    /// server run from exported JSON behaves identically to a listen host reading this asset.
    /// </remarks>
    [CreateAssetMenu(fileName = "SessionProfile", menuName = "AlpineLib/Networking/Session Profile")]
    public class SessionProfile : ScriptableObject {
        [Header("Identity")]
        [Tooltip("Id a create-session request names to pick this profile. Travels on the wire; never rename once shipped.")]
        public string profileId = "default";

        [Header("Lifetime")]
        [Tooltip("Whether the session ends with a match, ends with its lobby, or outlives both.")]
        public SessionLifetimeMode lifetimeMode = SessionLifetimeMode.LobbyScoped;
        [Tooltip("What happens when the owner leaves: end the session outright, or hand ownership to the next member.")]
        public HostPolicy hostPolicy = HostPolicy.TransferToMember;
        [Tooltip("Seconds an empty session lingers before shutting itself down.")]
        public float emptyShutdownSeconds = 300f;

        [Header("Rejoin")]
        [Tooltip("Whether a disconnected member keeps their roster slot, and for how long.")]
        public RejoinPolicy rejoinPolicy = RejoinPolicy.AnyTime;
        [Tooltip("Seconds a slot is held under TimedWindow. Ignored by the other policies.")]
        public float rejoinWindowSeconds = 120f;

        [Header("Capacity")]
        [Tooltip("Maximum members on the roster, connected or held for rejoin.")]
        public int maxPlayers = 8;

        [Header("Match Flow")]
        [Tooltip("Seconds the ready barrier waits for every client to report a loaded match.")]
        public float readyTimeoutSeconds = 30f;
        [Tooltip("What happens to a client that misses the ready barrier.")]
        public LateLoadPolicy lateLoadPolicy = LateLoadPolicy.DropToLobby;
        [Tooltip("Reserved, and false in v1: joining straight into a running match is not supported.")]
        public bool allowJoinDuringMatch;
        [Tooltip("Seconds the results screen is held before the session returns to its lobby.")]
        public float resultsHoldSeconds = 8f;

        /// <summary>Builds the shared profile this asset describes.</summary>
        public SessionProfileData ToData() {
            return new SessionProfileData {
                ProfileId = profileId,
                LifetimeMode = lifetimeMode,
                HostPolicy = hostPolicy,
                RejoinPolicy = rejoinPolicy,
                RejoinWindowSeconds = rejoinWindowSeconds,
                MaxPlayers = maxPlayers,
                ReadyTimeoutSeconds = readyTimeoutSeconds,
                LateLoadPolicy = lateLoadPolicy,
                AllowJoinDuringMatch = allowJoinDuringMatch,
                ResultsHoldSeconds = resultsHoldSeconds,
                EmptyShutdownSeconds = emptyShutdownSeconds
            };
        }
    }
}
