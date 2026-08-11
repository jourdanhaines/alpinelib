using System.Collections.Generic;
using System.IO;
using System.Text;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Sessions;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Exports an authored <see cref="SessionConfig"/> to the JSON a dedicated server loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server cannot read a ScriptableObject and the shared assemblies carry no JSON serializer, so
    /// this is the one bridge between the two: the same asset that configures the Unity client is
    /// flattened here into the two objects the server binds — the transport tuning and the session
    /// rules. Writing it from <c>ToNetConfig</c> and <c>ToData</c> rather than from the asset's fields
    /// keeps a single mapping: whatever the game runs with is exactly what the server is handed.
    /// </para>
    /// <para>
    /// <b>Shape contract.</b> Two top-level objects, <c>net</c> and <c>session</c>, whose members are the
    /// camel-cased property names of <see cref="NetConfig"/> and <see cref="SessionConfigData"/> — so the
    /// server binds them with a camel-case naming policy and nothing else. Enumerations are written as
    /// their numeric values, which every reader accepts whether or not it registers a string-enum
    /// converter. Computed properties (intervals, max speeds) are deliberately absent: they are derived
    /// on both sides from what is written here.
    /// </para>
    /// </remarks>
    public static class SessionConfigExporter {
        /// <summary>File name offered when the exporter cannot infer a better one.</summary>
        public const string DefaultFileName = "session-config.json";

        /// <summary>
        /// Exports the selected session config — or the only one in the project — to a file the user
        /// picks.
        /// </summary>
        [MenuItem("AlpineLib/Editor/Export Session Config")]
        public static void ExportSelected() {
            SessionConfig config = ResolveConfig();

            if (config == null) {
                EditorUtility.DisplayDialog(
                    "Export Session Config",
                    "Select a SessionConfig asset, or create one, before exporting.",
                    "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Export Session Config", string.Empty, DefaultFileName, "json"
            );

            if (string.IsNullOrEmpty(path)) return;

            Export(config, path);
        }

        /// <summary>
        /// Writes a session config to a path. Split from the menu entry so a build script can call it
        /// without a file dialog.
        /// </summary>
        public static void Export(SessionConfig config, string path) {
            if (config == null) {
                Debug.LogError("SessionConfigExporter::Export->No session config to export.");
                return;
            }

            File.WriteAllText(path, BuildJson(config));
            Debug.Log($"SessionConfigExporter::Export->Wrote {path}");
        }

        /// <summary>Builds the JSON document for a config, without touching the file system.</summary>
        public static string BuildJson(SessionConfig config) {
            NetConfig netConfig = config.ToNetConfig();
            SessionConfigData sessionData = config.ToData();

            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"net\": ");
            AppendNetConfig(builder, netConfig);
            builder.Append(",\n");
            builder.Append("  \"session\": ");
            AppendSessionConfig(builder, sessionData);
            builder.Append("\n}\n");

            return builder.ToString();
        }

        private static void AppendNetConfig(StringBuilder builder, NetConfig netConfig) {
            builder.Append("{\n");
            AppendString(builder, "gameProtocolName", netConfig.GameProtocolName, 4);
            AppendNumber(builder, "port", netConfig.Port, 4);
            AppendNumber(builder, "maxPeers", netConfig.MaxPeers, 4);
            AppendNumber(builder, "serverTickRate", netConfig.ServerTickRate, 4);
            AppendNumber(builder, "snapshotRate", netConfig.SnapshotRate, 4);
            AppendNumber(builder, "clientSendRate", netConfig.ClientSendRate, 4);
            AppendNumber(builder, "interpolationDelayMs", netConfig.InterpolationDelayMs, 4);
            AppendNumber(builder, "interpolationDelayMinMs", netConfig.InterpolationDelayMinMs, 4);
            AppendNumber(builder, "interpolationDelayMaxMs", netConfig.InterpolationDelayMaxMs, 4);
            AppendNumber(builder, "disconnectTimeoutMs", netConfig.DisconnectTimeoutMs, 4);
            AppendNumber(builder, "movementToleranceMultiplier", netConfig.MovementToleranceMultiplier, 4);
            AppendIndent(builder, 4);
            builder.Append("\"movementProfiles\": ");
            AppendMovementProfiles(builder, netConfig.MovementProfiles);
            builder.Append("\n  }");
        }

        private static void AppendMovementProfiles(StringBuilder builder, IReadOnlyList<MovementProfile> profiles) {
            if (profiles == null || profiles.Count == 0) {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++) {
                AppendMovementProfile(builder, profiles[profileIndex]);
                builder.Append(profileIndex < profiles.Count - 1 ? ",\n" : "\n");
            }

            AppendIndent(builder, 4);
            builder.Append(']');
        }

        private static void AppendMovementProfile(StringBuilder builder, MovementProfile profile) {
            MovementProfile resolved = profile ?? new MovementProfile();

            AppendIndent(builder, 6);
            builder.Append("{\n");
            AppendString(builder, "displayName", resolved.DisplayName, 8);
            AppendNumber(builder, "walkSlowSpeed", resolved.WalkSlowSpeed, 8);
            AppendNumber(builder, "walkSpeed", resolved.WalkSpeed, 8);
            AppendNumber(builder, "jogSpeed", resolved.JogSpeed, 8);
            AppendNumber(builder, "sprintSpeed", resolved.SprintSpeed, 8);
            AppendNumber(builder, "crouchSpeed", resolved.CrouchSpeed, 8);
            AppendNumber(builder, "crouchFastSpeed", resolved.CrouchFastSpeed, 8);
            AppendNumber(builder, "gravity", resolved.Gravity, 8);
            AppendNumber(builder, "jumpVelocity", resolved.JumpVelocity, 8);
            AppendNumber(builder, "airAcceleration", resolved.AirAcceleration, 8);
            AppendLastNumber(builder, "airDrag", resolved.AirDrag, 8);
            AppendIndent(builder, 6);
            builder.Append('}');
        }

        private static void AppendSessionConfig(StringBuilder builder, SessionConfigData sessionData) {
            builder.Append("{\n");
            AppendIndent(builder, 4);
            builder.Append("\"profile\": ");
            AppendProfile(builder, sessionData.Profile);
            builder.Append(",\n");
            AppendIndent(builder, 4);
            builder.Append("\"lobby\": ");
            AppendLobby(builder, sessionData.Lobby);
            builder.Append(",\n");
            AppendIndent(builder, 4);
            builder.Append("\"matches\": ");
            AppendMatches(builder, sessionData.Matches);
            builder.Append(",\n");
            AppendString(builder, "defaultDisplayName", sessionData.DefaultDisplayName, 4);
            AppendLastNumber(builder, "authMode", (int)sessionData.AuthMode, 4);
            builder.Append("  }");
        }

        private static void AppendProfile(StringBuilder builder, SessionProfileData profile) {
            SessionProfileData resolved = profile ?? new SessionProfileData();

            builder.Append("{\n");
            AppendString(builder, "profileId", resolved.ProfileId, 6);
            AppendNumber(builder, "lifetimeMode", (int)resolved.LifetimeMode, 6);
            AppendNumber(builder, "hostPolicy", (int)resolved.HostPolicy, 6);
            AppendNumber(builder, "rejoinPolicy", (int)resolved.RejoinPolicy, 6);
            AppendNumber(builder, "rejoinWindowSeconds", resolved.RejoinWindowSeconds, 6);
            AppendNumber(builder, "maxPlayers", resolved.MaxPlayers, 6);
            AppendNumber(builder, "readyTimeoutSeconds", resolved.ReadyTimeoutSeconds, 6);
            AppendNumber(builder, "lateLoadPolicy", (int)resolved.LateLoadPolicy, 6);
            AppendBool(builder, "allowJoinDuringMatch", resolved.AllowJoinDuringMatch, 6);
            AppendNumber(builder, "resultsHoldSeconds", resolved.ResultsHoldSeconds, 6);
            AppendLastNumber(builder, "emptyShutdownSeconds", resolved.EmptyShutdownSeconds, 6);
            AppendIndent(builder, 4);
            builder.Append('}');
        }

        private static void AppendLobby(StringBuilder builder, LobbyConfigData lobby) {
            LobbyConfigData resolved = lobby ?? new LobbyConfigData();

            builder.Append("{\n");
            AppendString(builder, "displayName", resolved.DisplayName, 6);
            AppendString(builder, "lobbySceneName", resolved.LobbySceneName, 6);
            AppendNumber(builder, "lobbyCapacity", resolved.LobbyCapacity, 6);
            AppendBool(builder, "ownerCanKick", resolved.OwnerCanKick, 6);
            AppendLastBool(builder, "ownerLaunchesMatches", resolved.OwnerLaunchesMatches, 6);
            AppendIndent(builder, 4);
            builder.Append('}');
        }

        private static void AppendMatches(StringBuilder builder, IReadOnlyList<MatchDefinitionData> matches) {
            if (matches == null || matches.Count == 0) {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");

            for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++) {
                AppendMatch(builder, matches[matchIndex]);
                builder.Append(matchIndex < matches.Count - 1 ? ",\n" : "\n");
            }

            AppendIndent(builder, 4);
            builder.Append(']');
        }

        private static void AppendMatch(StringBuilder builder, MatchDefinitionData match) {
            MatchDefinitionData resolved = match ?? new MatchDefinitionData();

            AppendIndent(builder, 6);
            builder.Append("{\n");
            AppendString(builder, "matchId", resolved.MatchId, 8);
            AppendString(builder, "displayName", resolved.DisplayName, 8);
            AppendString(builder, "sceneName", resolved.SceneName, 8);
            AppendNumber(builder, "minPlayers", resolved.MinPlayers, 8);
            AppendNumber(builder, "maxPlayers", resolved.MaxPlayers, 8);
            AppendLastNumber(builder, "maxDurationSeconds", resolved.MaxDurationSeconds, 8);
            AppendIndent(builder, 6);
            builder.Append('}');
        }

        private static void AppendString(StringBuilder builder, string name, string value, int indent) {
            AppendIndent(builder, indent);
            builder.Append('"').Append(name).Append("\": \"").Append(Escape(value)).Append("\",\n");
        }

        private static void AppendNumber(StringBuilder builder, string name, float value, int indent) {
            AppendIndent(builder, indent);
            builder.Append('"').Append(name).Append("\": ").Append(Format(value)).Append(",\n");
        }

        private static void AppendLastNumber(StringBuilder builder, string name, float value, int indent) {
            AppendIndent(builder, indent);
            builder.Append('"').Append(name).Append("\": ").Append(Format(value)).Append('\n');
        }

        private static void AppendBool(StringBuilder builder, string name, bool value, int indent) {
            AppendIndent(builder, indent);
            builder.Append('"').Append(name).Append("\": ").Append(value ? "true" : "false").Append(",\n");
        }

        private static void AppendLastBool(StringBuilder builder, string name, bool value, int indent) {
            AppendIndent(builder, indent);
            builder.Append('"').Append(name).Append("\": ").Append(value ? "true" : "false").Append('\n');
        }

        private static void AppendIndent(StringBuilder builder, int spaces) {
            builder.Append(' ', spaces);
        }

        /// <summary>
        /// Formats a number invariantly, so a machine with a comma decimal separator does not write JSON
        /// no parser will accept.
        /// </summary>
        private static string Format(float value) {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Escape(string value) {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Picks the config to export: the selected asset when one is selected, otherwise the project's
        /// only session config.
        /// </summary>
        private static SessionConfig ResolveConfig() {
            if (Selection.activeObject is SessionConfig selected) return selected;

            string[] assetGuids = AssetDatabase.FindAssets("t:SessionConfig");

            if (assetGuids.Length != 1) return null;

            string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[0]);
            return AssetDatabase.LoadAssetAtPath<SessionConfig>(assetPath);
        }
    }
}
