using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Configuration {
    /// <summary>JSON mirror of the Unity <c>LobbyConfig</c> asset: the room players return to.</summary>
    public sealed class LobbyConfigDocument {
        public string DisplayName { get; set; } = "Lobby";

        public string LobbySceneName { get; set; } = string.Empty;

        public int LobbyCapacity { get; set; } = 8;

        public bool OwnerCanKick { get; set; } = true;

        public bool OwnerLaunchesMatches { get; set; } = true;

        /// <summary>Maps this document onto the shared lobby config.</summary>
        public LobbyConfigData ToData() {
            return new LobbyConfigData {
                DisplayName = DisplayName,
                LobbySceneName = LobbySceneName,
                LobbyCapacity = LobbyCapacity,
                OwnerCanKick = OwnerCanKick,
                OwnerLaunchesMatches = OwnerLaunchesMatches
            };
        }
    }
}
