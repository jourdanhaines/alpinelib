using System.Collections.Generic;
using AlpineLib.Sessions;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Checks the authored settings a locally launched dedicated server cannot survive being given:
    /// an idle exit that never fires, a readiness budget of nothing, and no executable to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="AssetValidator"/>'s critical-field table because none of these is an
    /// empty reference — every field is filled in, with a value that is legal on its own and wrong for
    /// the one thing the asset exists to do. A <see cref="LocalServerConfig"/> is only ever read by
    /// <c>LocalServerLauncher</c>, so authoring one is opting into
    /// <see cref="SessionHostingMode.LocalServerProcess"/> and into the endings that mode has.
    /// </para>
    /// <para>
    /// The idle exit is the load-bearing one. A host who leaves a session other players are still in
    /// detaches the server rather than killing it, and nothing else ever reaps a detached one — so a
    /// zero idle exit, which the server reads as "run forever", leaks a server process per detach for
    /// the rest of the machine's uptime. That is invisible in play: the session the host left carries on
    /// working, which is the whole point of the detach.
    /// </para>
    /// </remarks>
    public static class LocalServerConfigValidator {
        private const string logPrefix = "[AlpineLib] LocalServerConfigValidator";

        /// <summary>
        /// Appends one entry per problem found to <paramref name="failures"/>. Adds nothing to a project
        /// with no local-server assets, which is every project that hosts somewhere else.
        /// </summary>
        public static void Validate(List<string> failures) {
            foreach (string assetPath in FindLocalServerConfigPaths()) {
                var localServer = AssetDatabase.LoadAssetAtPath<LocalServerConfig>(assetPath);

                if (localServer == null) continue;

                ValidateIdleExit(localServer, assetPath, failures);
                ValidateReadyTimeout(localServer, assetPath, failures);
                ValidateExecutableName(localServer, assetPath, failures);
            }
        }

        /// <summary>Runs the same pass interactively, for authoring rather than for a build gate.</summary>
        [MenuItem("AlpineLib/Networking/Validate Local Server Configs")]
        public static void ValidateFromMenu() {
            var failures = new List<string>();
            Validate(failures);

            foreach (string failure in failures) {
                Debug.LogError($"{logPrefix}: {failure}");
            }

            string summary = failures.Count == 0
                ? "No problems found."
                : $"{failures.Count} problem(s) found. See the console.";

            EditorUtility.DisplayDialog("Validate Local Server Configs", summary, "OK");
        }

        /// <remarks>
        /// Read through the clamp rather than off the field, because that is the value the launcher
        /// passes to <c>--idle-exit-seconds</c>: a negative authored number reaches the server as zero,
        /// which is the case being caught.
        /// </remarks>
        private static void ValidateIdleExit(LocalServerConfig localServer, string assetPath, List<string> failures) {
            if (localServer.ClampedIdleExitSeconds() > 0) return;

            failures.Add(
                $"{assetPath}: LocalServerConfig.idleExitSeconds is {localServer.idleExitSeconds}, which the server " +
                "reads as \"never exit\". A host who leaves a session other players are still in detaches the " +
                "server instead of stopping it, and nothing else reaps one, so every such leave leaks a process.");
        }

        /// <remarks>
        /// The floor makes a zero harmless at run time, which is why it is worth saying here: the value
        /// in the inspector is not the budget that gets waited out, and a designer shortening it to
        /// nothing to "fail faster" gets a second of waiting anyway.
        /// </remarks>
        private static void ValidateReadyTimeout(LocalServerConfig localServer, string assetPath, List<string> failures) {
            if (localServer.readyTimeoutSeconds > 0f) return;

            failures.Add(
                $"{assetPath}: LocalServerConfig.readyTimeoutSeconds is {localServer.readyTimeoutSeconds}; a host " +
                $"would wait the {LocalServerConfig.MinimumReadyTimeoutSeconds} s floor instead of the authored " +
                "budget. Author the budget the launch should actually get.");
        }

        /// <remarks>
        /// An empty name resolves to the server directory itself, so the launch fails with "no server
        /// executable at &lt;a folder&gt;" — true, and no help at all in finding what was left blank.
        /// </remarks>
        private static void ValidateExecutableName(LocalServerConfig localServer, string assetPath, List<string> failures) {
            if (!string.IsNullOrWhiteSpace(localServer.executableName)) return;

            failures.Add(
                $"{assetPath}: LocalServerConfig.executableName is empty, so hosting resolves the server directory " +
                "itself as the executable and every host attempt fails before it spawns anything.");
        }

        private static List<string> FindLocalServerConfigPaths() {
            var assetPaths = new List<string>();

            foreach (string assetGuid in AssetDatabase.FindAssets("t:LocalServerConfig")) {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);

                if (string.IsNullOrEmpty(assetPath)) continue;

                assetPaths.Add(assetPath);
            }

            return assetPaths;
        }
    }
}
