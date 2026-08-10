using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The complete rule set a session runs by, as it travels between the server and its clients.
    /// </summary>
    /// <remarks>
    /// Authored in Unity as the <c>SessionConfig</c> asset, exported to JSON for the .NET server, and
    /// pushed to every client verbatim inside <c>JoinAccepted</c> so nobody plays by stale local
    /// rules. Serialisation here is binary only: these sources compile into Unity, where a JSON
    /// dependency would not be welcome, and the wire form must be identical on both runtimes.
    /// There is no protocol version field — <c>NetProtocol.Version</c> is the single constant.
    /// </remarks>
    public sealed class SessionConfigData {
        private const int MaxMatchCount = 1024;

        /// <summary>Creates an empty config with default profile and lobby sections.</summary>
        public SessionConfigData() {
            Profile = new SessionProfileData();
            Lobby = new LobbyConfigData();
            Matches = new List<MatchDefinitionData>();
            DefaultDisplayName = "Penguin";
            AuthMode = AuthMethod.Anonymous;
        }

        /// <summary>Session lifetime, rejoin and host rules.</summary>
        public SessionProfileData Profile { get; set; }

        /// <summary>The lobby players return to between matches.</summary>
        public LobbyConfigData Lobby { get; set; }

        /// <summary>Every match this session may launch.</summary>
        public List<MatchDefinitionData> Matches { get; set; }

        /// <summary>Name given to players who never picked one.</summary>
        public string DefaultDisplayName { get; set; }

        /// <summary>Authentication method the server expects.</summary>
        public AuthMethod AuthMode { get; set; }

        /// <summary>Finds a match by its wire id, or null when the id is unknown.</summary>
        public MatchDefinitionData FindMatch(string matchId) {
            if (string.IsNullOrEmpty(matchId) || Matches == null) {
                return null;
            }

            for (int matchIndex = 0; matchIndex < Matches.Count; matchIndex++) {
                MatchDefinitionData candidate = Matches[matchIndex];

                if (candidate != null && string.Equals(candidate.MatchId, matchId, StringComparison.Ordinal)) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Writes the whole config to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            (Profile ?? new SessionProfileData()).Serialize(ref writer);
            (Lobby ?? new LobbyConfigData()).Serialize(ref writer);

            int matchCount = Matches == null ? 0 : Matches.Count;
            writer.WriteUShort((ushort)matchCount);

            for (int matchIndex = 0; matchIndex < matchCount; matchIndex++) {
                (Matches[matchIndex] ?? new MatchDefinitionData()).Serialize(ref writer);
            }

            writer.WriteString(DefaultDisplayName ?? string.Empty);
            writer.WriteByte((byte)AuthMode);
        }

        /// <summary>Reads a config written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            Profile = new SessionProfileData();
            Profile.Deserialize(ref reader);

            Lobby = new LobbyConfigData();
            Lobby.Deserialize(ref reader);

            int matchCount = reader.ReadUShort();

            if (matchCount > MaxMatchCount) {
                throw new NetProtocolException("SessionConfigData declared " + matchCount.ToString()
                    + " matches, which exceeds the sanity cap of " + MaxMatchCount.ToString() + ".");
            }

            Matches = new List<MatchDefinitionData>(matchCount);

            for (int matchIndex = 0; matchIndex < matchCount; matchIndex++) {
                MatchDefinitionData match = new MatchDefinitionData();
                match.Deserialize(ref reader);
                Matches.Add(match);
            }

            DefaultDisplayName = reader.ReadString();
            AuthMode = (AuthMethod)reader.ReadByte();
        }
    }
}
