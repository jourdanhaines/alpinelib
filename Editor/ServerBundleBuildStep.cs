using System.IO;
using AlpineLib.Sessions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Copies the published dedicated server, and the config it reads, beside a finished standalone
    /// build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build that hosts by launching a server is only as good as the two agreeing about where that
    /// server is. The runtime half of that agreement is <see cref="LocalServerPaths"/>; this is the
    /// other half, and it deliberately derives its destination from the same asset the launcher will,
    /// so the two cannot drift into looking in different folders.
    /// </para>
    /// <para>
    /// The exported session config is written from <see cref="SessionConfigExporter"/> rather than
    /// copied from a checked-in file. The point of the export is that the server runs the same numbers
    /// the client was built with; a hand-maintained JSON beside the executable would be a second source
    /// of truth that nothing checks.
    /// </para>
    /// <para>
    /// Runs late (order 100) so any step that rearranges the player's own output has already finished
    /// and <c>outputPath</c>'s folder is final.
    /// </para>
    /// </remarks>
    public class ServerBundleBuildStep : IPostprocessBuildWithReport {
        private const string logPrefix = "[AlpineLib] ServerBundleBuildStep";
        private const string geometryFolderName = "geometry";
        private const string geometrySearchPattern = "*.geo";

        /// <summary>Runs after the player's own post-build steps, so the output folder is settled.</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPostprocessBuild(BuildReport report) {
            if (report == null) return;
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            ServerBundleConfig config = ResolveConfig();
            if (config == null) return;
            if (!config.enabled) return;

            string sourceDirectory = ResolveProjectPath(config.publishedServerDirectory);
            if (!Directory.Exists(sourceDirectory)) {
                throw new BuildFailedException(
                    $"{logPrefix}: '{config.name}' publishes from '{config.publishedServerDirectory}', which does not " +
                    "exist. Publish the server before building, or clear the asset's enabled flag.");
            }

            string destination = ResolveDestination(report, config);

            WarnOnExecutableMismatch(config);
            CopyDirectory(sourceDirectory, destination);

            string configDirectory = Path.Combine(destination, LocalServerPaths.ConfigFolderName);
            Directory.CreateDirectory(configDirectory);

            WriteSessionConfig(config, configDirectory);
            int geometryCount = CopyGeometry(config, configDirectory);

            Debug.Log(
                $"{logPrefix}: bundled '{config.publishedServerDirectory}' into '{destination}' with " +
                $"{geometryCount} geometry file(s).");
        }

        /// <summary>
        /// The project's single server bundle config, or null when the project ships no server.
        /// </summary>
        /// <remarks>
        /// More than one is an error rather than a choice: the destination folder is fixed by the
        /// launcher, so a second asset would only overwrite the first and the build would depend on
        /// which order the asset database happened to return them in.
        /// </remarks>
        private static ServerBundleConfig ResolveConfig() {
            string[] assetGuids = AssetDatabase.FindAssets("t:ServerBundleConfig");

            if (assetGuids.Length == 0) return null;

            if (assetGuids.Length > 1) {
                Debug.LogError(
                    $"{logPrefix}: the project has {assetGuids.Length} ServerBundleConfig assets and they would all " +
                    "copy into the same folder; no server was bundled.");
                return null;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[0]);
            return AssetDatabase.LoadAssetAtPath<ServerBundleConfig>(assetPath);
        }

        /// <summary>
        /// The folder the server is copied into: the launcher's folder name beside the player.
        /// </summary>
        /// <remarks>
        /// One rule covers every standalone platform. Windows and Linux name an executable file, macOS
        /// names the <c>.app</c> bundle, and in all three the directory holding it is what a player
        /// resolves the bundled server against.
        /// </remarks>
        private static string ResolveDestination(BuildReport report, ServerBundleConfig config) {
            string playerDirectory = Path.GetDirectoryName(report.summary.outputPath);

            return Path.GetFullPath(Path.Combine(playerDirectory ?? string.Empty, config.ResolveBundleFolderName()));
        }

        /// <summary>
        /// Warns when the bundle and the launcher disagree about the executable's name, which produces a
        /// build that copies a working server the launcher then cannot find.
        /// </summary>
        private static void WarnOnExecutableMismatch(ServerBundleConfig config) {
            if (config.localServer == null) return;
            if (config.localServer.executableName == config.executableName) return;

            Debug.LogWarning(
                $"{logPrefix}: '{config.name}' bundles '{config.executableName}' but LocalServerConfig " +
                $"'{config.localServer.name}' launches '{config.localServer.executableName}'; hosting will fail.");
        }

        private static void WriteSessionConfig(ServerBundleConfig config, string configDirectory) {
            if (config.sessionConfig == null) {
                Debug.LogWarning(
                    $"{logPrefix}: '{config.name}' names no session config; the bundled server will run on its own " +
                    "defaults rather than this build's settings.");
                return;
            }

            SessionConfigExporter.Export(
                config.sessionConfig, Path.Combine(configDirectory, SessionConfigExporter.DefaultFileName));
        }

        /// <summary>Copies every exported geometry file into the server's config folder.</summary>
        private static int CopyGeometry(ServerBundleConfig config, string configDirectory) {
            if (string.IsNullOrWhiteSpace(config.geometrySourceDirectory)) return 0;

            string sourceDirectory = ResolveProjectPath(config.geometrySourceDirectory);

            if (!Directory.Exists(sourceDirectory)) {
                Debug.LogWarning(
                    $"{logPrefix}: '{config.name}' takes geometry from '{config.geometrySourceDirectory}', which does " +
                    "not exist; the server will simulate flat ground in every scene.");
                return 0;
            }

            string geometryDirectory = Path.Combine(configDirectory, geometryFolderName);
            Directory.CreateDirectory(geometryDirectory);

            string[] geometryFiles = Directory.GetFiles(sourceDirectory, geometrySearchPattern);

            foreach (string geometryFile in geometryFiles) {
                File.Copy(geometryFile, Path.Combine(geometryDirectory, Path.GetFileName(geometryFile)), true);
            }

            return geometryFiles.Length;
        }

        /// <summary>Turns a project-relative path into an absolute one, leaving absolute paths alone.</summary>
        private static string ResolveProjectPath(string relativePath) {
            if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
            if (Path.IsPathRooted(relativePath)) return Path.GetFullPath(relativePath);

            string projectRoot = Path.Combine(Application.dataPath, "..");
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        /// <summary>Copies a directory tree, overwriting whatever a previous build left behind.</summary>
        private static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string filePath in Directory.GetFiles(sourceDirectory)) {
                File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), true);
            }

            foreach (string childPath in Directory.GetDirectories(sourceDirectory)) {
                CopyDirectory(childPath, Path.Combine(destinationDirectory, Path.GetFileName(childPath)));
            }
        }
    }
}
