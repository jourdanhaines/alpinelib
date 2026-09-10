using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of <see cref="SessionConfigData"/>, matching the <c>session</c> object the Unity
    /// exporter writes: the rule set every client is handed verbatim on join.
    /// </summary>
    /// <remarks>
    /// There is no protocol version here, deliberately. <c>NetProtocol.Version</c> is the single constant
    /// that decides whether a client and this server may speak at all, and a second one in a config file
    /// could only ever disagree with it.
    /// </remarks>
    public sealed class SessionDataDocument {
        public SessionProfileDocument Profile { get; set; } = new SessionProfileDocument();

        public LobbyConfigDocument Lobby { get; set; } = new LobbyConfigDocument();

        public List<MatchDefinitionDocument> Matches { get; set; } = new List<MatchDefinitionDocument>();

        public string DefaultDisplayName { get; set; } = SessionConfigData.FallbackDisplayName;

        public AuthMethod AuthMode { get; set; } = AuthMethod.Anonymous;

        /// <summary>Maps this document onto the shared config the session host runs by.</summary>
        public SessionConfigData ToData() {
            return new SessionConfigData {
                Profile = (Profile ?? new SessionProfileDocument()).ToData(),
                Lobby = (Lobby ?? new LobbyConfigDocument()).ToData(),
                Matches = BuildMatches(),
                DefaultDisplayName = DefaultDisplayName,
                AuthMode = AuthMode
            };
        }

        private List<MatchDefinitionData> BuildMatches() {
            List<MatchDefinitionData> matches = new List<MatchDefinitionData>();

            if (Matches == null) {
                return matches;
            }

            for (int matchIndex = 0; matchIndex < Matches.Count; matchIndex++) {
                MatchDefinitionDocument document = Matches[matchIndex];

                if (document != null) {
                    matches.Add(document.ToData());
                }
            }

            return matches;
        }
    }
}
