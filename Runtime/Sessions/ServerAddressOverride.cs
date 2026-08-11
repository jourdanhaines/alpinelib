using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AlpineLib.Sessions {
    /// <summary>
    /// Ways of repointing a build at a different server without editing the matchmaking asset or
    /// rebuilding the player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asset's address is the shipped default — what a player who just runs the executable gets.
    /// Everything else about where a client connects is a deployment question, not a content question,
    /// and deployment questions should be answerable at launch: a tester points a build at a staging
    /// server with a flag, a CI job with an environment variable, and a developer keeps the editor on
    /// localhost while the committed asset points at the live server.
    /// </para>
    /// <para>
    /// Precedence, most explicit first: the <c>-server host:port</c> command line flag (also accepted
    /// as <c>-server=host:port</c>), the <c>ALPINE_SERVER_ADDRESS</c> environment variable, and — in
    /// the editor only — a per-project preference set from
    /// <c>AlpineLib &#8594; Networking &#8594; Server Address Override</c>. The winning source is
    /// reported alongside the address so a connection to a surprising server is one log line from its
    /// explanation.
    /// </para>
    /// </remarks>
    public static class ServerAddressOverride {
        /// <summary>The launch flag: <c>-server host:port</c> or <c>-server=host:port</c>.</summary>
        public const string CommandLineFlag = "-server";

        /// <summary>The environment variable consulted when no flag is present.</summary>
        public const string EnvironmentVariable = "ALPINE_SERVER_ADDRESS";

#if UNITY_EDITOR
        /// <summary>
        /// The editor-only override, kept in per-user editor preferences so it never travels with the
        /// repository or into a build. Empty means none.
        /// </summary>
        /// <remarks>
        /// Keyed by the project's product GUID rather than a fixed string, because editor preferences
        /// are shared machine-wide and two projects using this library must not steal each other's
        /// override.
        /// </remarks>
        public static string EditorOverride {
            get => EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            set {
                if (string.IsNullOrWhiteSpace(value)) {
                    EditorPrefs.DeleteKey(EditorPrefsKey);
                    return;
                }

                EditorPrefs.SetString(EditorPrefsKey, value.Trim());
            }
        }

        private static string EditorPrefsKey => $"AlpineLib.ServerAddressOverride.{PlayerSettings.productGUID}";
#endif

        /// <summary>
        /// Answers the address the build should dial instead of the asset's, when any override is set.
        /// </summary>
        /// <param name="address">The overriding <c>host:port</c>.</param>
        /// <param name="source">Which override won, for the connection log.</param>
        /// <returns>True when an override is present.</returns>
        public static bool TryResolve(out string address, out string source) {
            if (TryReadCommandLine(out address)) {
                source = $"the {CommandLineFlag} launch flag";
                return true;
            }

            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment)) {
                address = fromEnvironment.Trim();
                source = $"the {EnvironmentVariable} environment variable";
                return true;
            }

#if UNITY_EDITOR
            string fromEditor = EditorOverride;

            if (!string.IsNullOrWhiteSpace(fromEditor)) {
                address = fromEditor;
                source = "the editor override (AlpineLib > Networking > Server Address Override)";
                return true;
            }
#endif

            address = null;
            source = null;
            return false;
        }

        /// <summary>Finds the flag among the process arguments, in either its spaced or its = form.</summary>
        private static bool TryReadCommandLine(out string address) {
            address = null;
            string[] arguments = Environment.GetCommandLineArgs();

            for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++) {
                string argument = arguments[argumentIndex];

                if (argument.StartsWith(CommandLineFlag + "=", StringComparison.OrdinalIgnoreCase)) {
                    address = argument.Substring(CommandLineFlag.Length + 1).Trim();
                    return !string.IsNullOrEmpty(address);
                }

                if (!string.Equals(argument, CommandLineFlag, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (argumentIndex + 1 >= arguments.Length) {
                    Debug.LogWarning($"ServerAddressOverride::TryReadCommandLine->{CommandLineFlag} was passed with no address after it; ignoring it.");
                    return false;
                }

                address = arguments[argumentIndex + 1].Trim();
                return !string.IsNullOrEmpty(address);
            }

            return false;
        }
    }
}
