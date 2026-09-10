using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Configuration {
    /// <summary>JSON mirror of one Unity <c>MatchDefinition</c> asset.</summary>
    /// <remarks>
    /// <c>matchId</c> is the wire identity a client asks to launch by. Renaming one in the editor and
    /// re-exporting breaks every client that still asks for the old id, which is why the authoring asset
    /// carries the same warning.
    /// </remarks>
    public sealed class MatchDefinitionDocument {
        public string MatchId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string SceneName { get; set; } = string.Empty;

        public int MinPlayers { get; set; } = 1;

        public int MaxPlayers { get; set; } = 8;

        public float MaxDurationSeconds { get; set; }

        /// <summary>Maps this document onto the shared match definition.</summary>
        public MatchDefinitionData ToData() {
            return new MatchDefinitionData {
                MatchId = MatchId,
                DisplayName = DisplayName,
                SceneName = SceneName,
                MinPlayers = MinPlayers,
                MaxPlayers = MaxPlayers,
                MaxDurationSeconds = MaxDurationSeconds
            };
        }
    }
}
