using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// How a client finds the server to talk to, and which ways of reaching a friend's session are
    /// switched on.
    /// </summary>
    /// <remarks>
    /// The address here answers one question only — which server this build connects to. It never
    /// selects a session: a join code is typed after the connection is up and is resolved by the server
    /// against the sessions it is hosting. That separation is what lets a code be six friendly
    /// characters instead of an address, and what makes a future directory service a change to
    /// <see cref="CreateLocator"/> rather than to the join flow.
    /// </remarks>
    [CreateAssetMenu(fileName = "MatchmakingConfig", menuName = "AlpineLib/Networking/Matchmaking Config")]
    public class MatchmakingConfig : ScriptableObject {
        [Header("Server")]
        [Tooltip("host:port of the game server every client of this build connects to.")]
        public string serverAddress = "127.0.0.1:9050";

        [Header("Discovery")]
        [Tooltip("Players may create and enter six-character join codes.")]
        public bool enableJoinCodes = true;
        [Tooltip("Seam, off in v1: resolve the server through a backend directory instead of the address above.")]
        public bool enableBackendDirectory;
        [Tooltip("host:port of the backend directory, used only when the directory is enabled.")]
        public string backendDirectoryAddress;
        [Tooltip("Seam, off in v1: accept Steam friend invites carrying a server address and join code.")]
        public bool enableSteamInvites;

        /// <summary>
        /// Builds the locator that resolves the server endpoint for this build: an override from
        /// <see cref="ServerAddressOverride"/> when one is set, the asset's address otherwise.
        /// </summary>
        /// <returns>A configured locator, or null when the address cannot be parsed.</returns>
        public ISessionLocator CreateLocator() {
            string address = serverAddress;

            if (ServerAddressOverride.TryResolve(out string overrideAddress, out string overrideSource)) {
                address = overrideAddress;
                Debug.Log($"MatchmakingConfig::CreateLocator->Dialing {address} from {overrideSource} instead of the configured {serverAddress}.");
            }

            if (!ConfiguredServerLocator.TryParseAddress(address, out NetEndpoint endpoint)) {
                Debug.LogError($"MatchmakingConfig::CreateLocator->'{address}' is not a host:port address.");
                return null;
            }

            return new ConfiguredServerLocator(endpoint);
        }
    }
}
