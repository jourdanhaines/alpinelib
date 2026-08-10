using AlpineLib.Netcode.Sessions;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// One thing a party can go and do together: a scene, a player count and a time limit, addressed by
    /// a stable id.
    /// </summary>
    /// <remarks>
    /// <see cref="matchId"/> is a wire identity — a launch request names it, and the server matches it
    /// against its own config — so renaming one after a build has shipped breaks every client that still
    /// asks for the old name. The display name is the one that may change freely.
    /// </remarks>
    [CreateAssetMenu(fileName = "MatchDefinition", menuName = "AlpineLib/Networking/Match Definition")]
    public class MatchDefinition : ScriptableObject {
        [Header("Identity")]
        [Tooltip("Stable id used on the wire to request this match. Never rename once shipped.")]
        public string matchId;
        [Tooltip("Name shown to players. Safe to change at any time.")]
        public string displayName;

        [Header("Scene")]
        [Tooltip("Scene every participant loads for this match. Must be in the build settings.")]
        public string sceneName;

        [Header("Participants")]
        [Tooltip("Fewest members that may start this match.")]
        public int minPlayers = 1;
        [Tooltip("Most members that may take part.")]
        public int maxPlayers = 8;

        [Header("Duration")]
        [Tooltip("Seconds before the match ends itself. Zero or less means no limit.")]
        public float maxDurationSeconds;

        /// <summary>Builds the shared match definition this asset describes.</summary>
        public MatchDefinitionData ToData() {
            return new MatchDefinitionData {
                MatchId = matchId,
                DisplayName = displayName,
                SceneName = sceneName,
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                MaxDurationSeconds = maxDurationSeconds
            };
        }
    }
}
