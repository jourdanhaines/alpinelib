using System;
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
    /// <b>Failure policy.</b> Every state that would ship a player unable to host fails the build: two
    /// bundle configs, a publish folder that is not there, and a copy that did not land the executable
    /// the launcher will run. The two states that are a deliberate choice — no bundle config at all, and
    /// one whose <c>enabled</c> flag is off — log a line and return, because a build with no server in
    /// it is an ordinary thing to want and silence is what makes the missing <c>Server</c> folder a
    /// mystery afterwards.
    /// </para>
    /// <para>
    /// Runs late (order 100) so any step that rearranges the player's own output has already finished
    /// and <c>outputPath</c>'s folder is final.
    /// </para>
    /// </remarks>
    public class ServerBundleBuildStep : IPostprocessBuildWithReport {
        private const string logPrefix = "[AlpineLib] ServerBundleBuildStep";
        private const string geometryFolderName = "geometry";

        /// <summary>Runs after the player's own post-build steps, so the output folder is settled.</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPostprocessBuild(BuildReport report) {
            if (report == null) return;
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            ServerBundleConfig config = ResolveConfig();
            if (config == null) return;

            if (!config.enabled) {
                Debug.Log(
                    $"{logPrefix}: '{config.name}' is disabled; this build ships no server and cannot host on its own.");
                return;
            }

            BundleServer(report, config);
        }

        /// <summary>Copies the platform's publish, then writes the config the copied server reads.</summary>
        private static void BundleServer(BuildReport report, ServerBundleConfig config) {
            string runtimeIdentifier = ResolveRuntimeIdentifier(report.summary.platform, config);
            string relativeSource = config.ResolvePublishedDirectory(runtimeIdentifier);
            string sourceDirectory = RequirePublishedDirectory(config, relativeSource, report.summary.platform);
            string destination = ResolveDestination(report, config);

            WarnOnExecutableMismatch(config);
            ReplaceDirectory(config, sourceDirectory, destination);
            RequireBundledExecutable(config, destination, report.summary.platform, relativeSource);

            string configDirectory = Path.Combine(destination, LocalServerPaths.ConfigFolderName);
            Directory.CreateDirectory(configDirectory);

            WriteSessionConfig(config, configDirectory);
            int geometryCount = CopyGeometry(config, configDirectory);

            Debug.Log(
                $"{logPrefix}: bundled '{relativeSource}' into '{destination}' with {geometryCount} geometry file(s).");
        }

        /// <summary>
        /// The project's single server bundle config, or null when the project ships no server.
        /// </summary>
        /// <remarks>
        /// More than one fails the build rather than picking one: the destination folder is fixed by the
        /// launcher, so a second asset would only overwrite the first and which one won would depend on
        /// the order the asset database happened to return them in.
        /// </remarks>
        private static ServerBundleConfig ResolveConfig() {
            string[] assetGuids = AssetDatabase.FindAssets("t:ServerBundleConfig");

            if (assetGuids.Length == 0) {
                Debug.Log($"{logPrefix}: the project has no ServerBundleConfig; no server was bundled.");
                return null;
            }

            if (assetGuids.Length > 1) {
                throw new BuildFailedException(
                    $"{logPrefix}: the project has {assetGuids.Length} ServerBundleConfig assets and they would all " +
                    "copy into the same folder. Delete or disable all but one.");
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[0]);
            return AssetDatabase.LoadAssetAtPath<ServerBundleConfig>(assetPath);
        }

        /// <summary>
        /// The runtime identifier folder published for the platform being built.
        /// </summary>
        /// <remarks>
        /// A .NET publish is per runtime identifier, so this is what stops a Windows player being handed
        /// the Linux publish. An unmapped standalone target fails rather than guessing: guessing means
        /// shipping binaries for another operating system with nothing in the build output saying so.
        /// </remarks>
        private static string ResolveRuntimeIdentifier(BuildTarget platform, ServerBundleConfig config) {
            string runtimeIdentifier = ReadRuntimeIdentifier(platform, config);

            if (!string.IsNullOrWhiteSpace(runtimeIdentifier)) return runtimeIdentifier.Trim();

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' names no published runtime identifier for build target " +
                $"'{platform}'; fill the matching field in, or clear the asset's enabled flag.");
        }

        private static string ReadRuntimeIdentifier(BuildTarget platform, ServerBundleConfig config) {
            switch (platform) {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return config.windowsRuntimeIdentifier;
                case BuildTarget.StandaloneLinux64:
                    return config.linuxRuntimeIdentifier;
                case BuildTarget.StandaloneOSX:
                    return config.macRuntimeIdentifier;
                default:
                    return null;
            }
        }

        /// <summary>The absolute publish directory, failing the build when the platform has none.</summary>
        private static string RequirePublishedDirectory(
            ServerBundleConfig config, string relativeSource, BuildTarget platform) {
            string sourceDirectory = ResolveProjectPath(relativeSource);

            if (Directory.Exists(sourceDirectory)) return sourceDirectory;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' publishes '{platform}' from '{relativeSource}', which does not exist. " +
                "Publish the server for that runtime identifier before building, or clear the asset's enabled flag.");
        }

        /// <summary>
        /// Checks that the copy landed the file the launcher will try to run.
        /// </summary>
        /// <remarks>
        /// The one check that covers every way this step can otherwise succeed and still ship a build
        /// that cannot host: a publish folder holding some other platform's output, a publish holding
        /// something else entirely, and an executable name that no longer matches what is published.
        /// </remarks>
        private static void RequireBundledExecutable(
            ServerBundleConfig config, string destination, BuildTarget platform, string relativeSource) {
            string fileName = ResolveExecutableFileName(config, platform);

            if (File.Exists(Path.Combine(destination, fileName))) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{relativeSource}' holds no '{fileName}' for build target '{platform}'; what was " +
                "copied is not a server the launcher could run. Check the publish's runtime identifier and the " +
                $"asset's executable name ('{config.executableName}').");
        }

        /// <summary>The published server's file name, <c>.exe</c> included for a Windows target.</summary>
        private static string ResolveExecutableFileName(ServerBundleConfig config, BuildTarget platform) {
            bool isWindows = platform == BuildTarget.StandaloneWindows || platform == BuildTarget.StandaloneWindows64;

            if (!isWindows) return config.executableName;

            return config.executableName + LocalServerPaths.WindowsExecutableExtension;
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

            return CopyGeometryFiles(sourceDirectory, geometryDirectory);
        }

        /// <summary>
        /// Copies the exported geometry, matching the extension exactly.
        /// </summary>
        /// <remarks>
        /// The files are filtered rather than searched for by pattern: a three-character extension in a
        /// search pattern also matches longer ones on Windows, so a stray <c>.geometry</c> file would be
        /// shipped as geometry the server cannot read.
        /// </remarks>
        private static int CopyGeometryFiles(string sourceDirectory, string geometryDirectory) {
            int copied = 0;

            foreach (string geometryFile in Directory.GetFiles(sourceDirectory)) {
                if (!IsGeometryFile(geometryFile)) continue;

                File.Copy(geometryFile, Path.Combine(geometryDirectory, Path.GetFileName(geometryFile)), true);
                copied++;
            }

            return copied;
        }

        private static bool IsGeometryFile(string filePath) {
            return string.Equals(
                Path.GetExtension(filePath),
                SceneGeometryExporter.GeometryFileExtension,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Turns a project-relative path into an absolute one, leaving absolute paths alone.</summary>
        private static string ResolveProjectPath(string relativePath) {
            if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
            if (Path.IsPathRooted(relativePath)) return Path.GetFullPath(relativePath);

            string projectRoot = Path.Combine(Application.dataPath, "..");
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        /// <summary>
        /// Replaces the bundle folder with a fresh copy of the publish.
        /// </summary>
        /// <remarks>
        /// The bundle folder is emptied first and nothing outside it is ever touched. Merging into it
        /// instead leaves a previous build's renamed executable, another runtime identifier's native
        /// libraries and geometry for scenes that no longer exist sitting exactly where the server's
        /// probe will find them.
        /// </remarks>
        private static void ReplaceDirectory(ServerBundleConfig config, string sourceDirectory, string destination) {
            RequireSeparatePaths(config, sourceDirectory, destination);

            if (Directory.Exists(destination)) {
                Directory.Delete(destination, true);
            }

            CopyDirectory(sourceDirectory, destination);
        }

        /// <summary>
        /// Refuses a source and destination where either contains the other.
        /// </summary>
        /// <remarks>
        /// Building the player into the publish root, or publishing into the build folder, makes one a
        /// child of the other — which would either delete the source before copying it or copy a folder
        /// into itself until the path length gives out. Neither is recoverable by carrying on.
        /// </remarks>
        private static void RequireSeparatePaths(
            ServerBundleConfig config, string sourceDirectory, string destination) {
            string source = WithTrailingSeparator(sourceDirectory);
            string target = WithTrailingSeparator(destination);

            if (!target.StartsWith(source, StringComparison.Ordinal)
                && !source.StartsWith(target, StringComparison.Ordinal)) {
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' would copy '{sourceDirectory}' into '{destination}', which is inside " +
                "it (or contains it). Build the player outside the publish folder, or publish outside the build " +
                "folder.");
        }

        /// <summary>A full path that always ends in a separator, so one folder cannot prefix another.</summary>
        private static string WithTrailingSeparator(string path) {
            string fullPath = Path.GetFullPath(path);
            string separator = Path.DirectorySeparatorChar.ToString();

            if (fullPath.EndsWith(separator, StringComparison.Ordinal)) return fullPath;

            return fullPath + separator;
        }

        /// <summary>Copies a directory tree into an emptied destination.</summary>
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
