using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Engine-free mirror of the <c>MatchDefinition</c> authoring asset: one minigame a session can
    /// launch.
    /// </summary>
    /// <remarks>
    /// <see cref="MatchId"/> is wire identity — it is what <c>LaunchMatchRequest</c> and
    /// <c>MatchResultData</c> carry, so renaming it breaks every client that already shipped.
    /// </remarks>
    public sealed class MatchDefinitionData {
        /// <summary>Creates a match definition carrying the shipped defaults.</summary>
        public MatchDefinitionData() {
            MatchId = string.Empty;
            DisplayName = string.Empty;
            SceneName = string.Empty;
            MinPlayers = 1;
            MaxPlayers = 8;
            MaxDurationSeconds = 0f;
        }

        /// <summary>Stable wire identity. Never rename.</summary>
        public string MatchId { get; set; }

        /// <summary>Human-readable name for menus.</summary>
        public string DisplayName { get; set; }

        /// <summary>Scene loaded for this match.</summary>
        public string SceneName { get; set; }

        /// <summary>Fewest participants the match will start with.</summary>
        public int MinPlayers { get; set; }

        /// <summary>Most participants the match accepts.</summary>
        public int MaxPlayers { get; set; }

        /// <summary>Hard time limit in seconds; zero means untimed.</summary>
        public float MaxDurationSeconds { get; set; }

        /// <summary>Writes the definition to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(MatchId ?? string.Empty);
            writer.WriteString(DisplayName ?? string.Empty);
            writer.WriteString(SceneName ?? string.Empty);
            writer.WriteInt(MinPlayers);
            writer.WriteInt(MaxPlayers);
            writer.WriteFloat(MaxDurationSeconds);
        }

        /// <summary>Reads a definition written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            MatchId = reader.ReadString();
            DisplayName = reader.ReadString();
            SceneName = reader.ReadString();
            MinPlayers = reader.ReadInt();
            MaxPlayers = reader.ReadInt();
            MaxDurationSeconds = reader.ReadFloat();
        }

        /// <summary>True when the match imposes a time limit.</summary>
        public bool HasTimeLimit() {
            return MaxDurationSeconds > 0f;
        }
    }
}
