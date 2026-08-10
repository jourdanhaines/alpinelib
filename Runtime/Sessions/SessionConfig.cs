using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Networking;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// The single asset that says how this build networks: its transport tuning, its session rules, its
    /// lobby, the matches it can run, how it finds a server and what it may spawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One root asset rather than a service inspector full of fields, because the same configuration has
    /// to reach three places: the Unity client that loads it, the dedicated server that reads it as
    /// exported JSON, and every joining client that receives the session half of it in
    /// <c>JoinAccepted</c>. A single asset is the only way those three can be guaranteed to agree.
    /// </para>
    /// <para>
    /// There is deliberately no version field. <c>NetProtocol.Version</c> is the one constant that gates
    /// compatibility, and it is folded into the transport connect key — a build speaking a different
    /// protocol is rejected before any of this configuration is exchanged.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "SessionConfig", menuName = "AlpineLib/Networking/Session Config")]
    public class SessionConfig : ScriptableObject {
        [Header("Transport")]
        [Tooltip("Ports, tick rates and interpolation delay.")]
        public NetworkConfig network;

        [Header("Session Rules")]
        [Tooltip("Lifetime, host policy, rejoin policy and capacity.")]
        public SessionProfile profile;
        [Tooltip("The room the session rests in between matches.")]
        public LobbyConfig lobby;
        [Tooltip("Everything this session may launch. A launch request names one of these by match id.")]
        public MatchDefinition[] matches = Array.Empty<MatchDefinition>();

        [Header("Discovery")]
        [Tooltip("Which server this build connects to, and which join paths are enabled.")]
        public MatchmakingConfig matchmaking;

        [Header("Spawning")]
        [Tooltip("Append-only prefab table; a row's index is its prefab id on the wire.")]
        public NetPrefabRegistry prefabRegistry;

        [Header("Fallbacks")]
        [Tooltip("Scene loaded when a session ends and there is nowhere else to be — usually the main menu.")]
        public string offlineFallbackSceneName;
        [Tooltip("Name given to a player who has never chosen one.")]
        public string defaultDisplayName = "Penguin";
        [Tooltip("How players prove who they are. Anonymous in v1; Steam is a reserved seam.")]
        public AuthMethod authMode = AuthMethod.Anonymous;

        /// <summary>
        /// Builds the session half of this configuration — the part a server owns and hands to every
        /// client that attaches.
        /// </summary>
        /// <remarks>
        /// Transport tuning, matchmaking and the prefab registry are deliberately absent: the first two
        /// are local to a build and the third is authored content that could never fit in a join
        /// message. What travels is only what the server must impose on everyone.
        /// </remarks>
        public SessionConfigData ToData() {
            return new SessionConfigData {
                Profile = profile != null ? profile.ToData() : new SessionProfileData(),
                Lobby = lobby != null ? lobby.ToData() : new LobbyConfigData(),
                Matches = BuildMatchData(),
                DefaultDisplayName = defaultDisplayName,
                AuthMode = authMode
            };
        }

        /// <summary>Builds the transport tuning, carrying the prefab registry's movement profiles.</summary>
        public NetConfig ToNetConfig() {
            if (network == null) {
                Debug.LogError($"SessionConfig::ToNetConfig->{name} has no network config; using defaults.");
                return new NetConfig();
            }

            return network.ToConfig(prefabRegistry);
        }

        /// <summary>Finds an authored match by its wire id, or null when this build has no such match.</summary>
        public MatchDefinition FindMatch(string matchId) {
            if (matches == null || string.IsNullOrEmpty(matchId)) return null;

            foreach (MatchDefinition match in matches) {
                if (match == null) continue;
                if (match.matchId != matchId) continue;

                return match;
            }

            return null;
        }

        private List<MatchDefinitionData> BuildMatchData() {
            var data = new List<MatchDefinitionData>();

            if (matches == null) return data;

            foreach (MatchDefinition match in matches) {
                if (match == null) continue;

                data.Add(match.ToData());
            }

            return data;
        }
    }
}
