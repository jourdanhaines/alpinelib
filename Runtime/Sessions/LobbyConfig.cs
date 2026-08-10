using AlpineLib.Netcode.Sessions;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// The room a session lives in between matches: which scene it is, how many it holds, and what the
    /// owner is allowed to do there.
    /// </summary>
    /// <remarks>
    /// A lobby is not a separate server or a separate connection — it is the session's resting phase.
    /// Launching a match changes the phase and the scene; the peers, the roster and the chat scope all
    /// carry straight through, and returning drops everyone back into this scene.
    /// </remarks>
    [CreateAssetMenu(fileName = "LobbyConfig", menuName = "AlpineLib/Networking/Lobby Config")]
    public class LobbyConfig : ScriptableObject {
        [Header("Presentation")]
        [Tooltip("Name shown for this lobby in menus and session listings.")]
        public string displayName = "Lobby";

        [Header("Scene")]
        [Tooltip("Scene loaded when the session is in its lobby phase. Must be in the build settings.")]
        public string lobbySceneName;

        [Header("Capacity")]
        [Tooltip("How many members the lobby holds. Must not exceed the session profile's maxPlayers.")]
        public int lobbyCapacity = 8;

        [Header("Owner Powers")]
        [Tooltip("The owner may remove members from the session.")]
        public bool ownerCanKick = true;
        [Tooltip("Only the owner may start a match. Off lets any member launch.")]
        public bool ownerLaunchesMatches = true;

        /// <summary>Builds the shared lobby config this asset describes.</summary>
        public LobbyConfigData ToData() {
            return new LobbyConfigData {
                DisplayName = displayName,
                LobbySceneName = lobbySceneName,
                LobbyCapacity = lobbyCapacity,
                OwnerCanKick = ownerCanKick,
                OwnerLaunchesMatches = ownerLaunchesMatches
            };
        }
    }
}
