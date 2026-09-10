using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
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
    /// <b>Nothing is replaced until a whole bundle exists.</b> The copy is assembled in a temporary
    /// sibling folder, checked for the file count the publish holds and the executable the launcher
    /// runs, and only then swapped in by renaming. Emptying the destination first and copying into it
    /// means any failure part-way — an unreadable file, a full disk, a lock — leaves the player beside a
    /// folder holding whatever happened to be copied before the throw, which looks exactly like a
    /// bundle and cannot host.
    /// </para>
    /// <para>
    /// <b>Failure policy.</b> Every state that would ship a player unable to host fails the build with a
    /// <see cref="BuildFailedException"/>, which is the only exception type Unity's build pipeline turns
    /// into a failed build rather than a logged one: two bundle configs, a bundle folder name that is
    /// not a single folder, a publish folder that is not there or not named for the runtime identifier
    /// being built, a publish that resolves to the same place as the destination, a missing session
    /// config, and a copy that did not land everything the publish holds. The two states that are a
    /// deliberate choice — no bundle config at all, and one whose <c>enabled</c> flag is off — log a line
    /// and return, because a build with no server in it is an ordinary thing to want and silence is what
    /// makes the missing <c>Server</c> folder a mystery afterwards.
    /// </para>
    /// <para>
    /// Runs late (order 100) so any step that rearranges the player's own output has already finished
    /// and <c>outputPath</c>'s folder is final.
    /// </para>
    /// </remarks>
    public class ServerBundleBuildStep : IPostprocessBuildWithReport {
        private const string logPrefix = "[AlpineLib] ServerBundleBuildStep";
        private const string geometryFolderName = "geometry";

        /// <summary>Name of the informational stamp written beside the exported session config.</summary>
        private const string stampFileName = "bundle.json";

        /// <summary>Suffix of the folder the bundle is assembled in, before it is swapped into place.</summary>
        private const string temporarySuffix = ".bundling";

        /// <summary>Suffix the previous bundle is renamed to while the new one is moved in.</summary>
        private const string staleSuffix = ".stale";

        private const string macIntelRuntimeIdentifier = "osx-x64";
        private const string macAppleSiliconRuntimeIdentifier = "osx-arm64";

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
            BuildTarget platform = report.summary.platform;
            string runtimeIdentifier = ResolveRuntimeIdentifier(platform, config);
            string relativeSource = config.ResolvePublishedDirectory(runtimeIdentifier);
            string sourceDirectory = RequirePublishedDirectory(config, relativeSource, platform);

            RequireRuntimeIdentifierFolder(config, sourceDirectory, runtimeIdentifier, relativeSource);
            RequireSessionConfig(config);
            WarnOnExecutableMismatch(config);

            string destination = ResolveDestination(report, config);
            int geometryCount = SwapInFreshBundle(
                config, sourceDirectory, destination, runtimeIdentifier, platform, relativeSource);

            Debug.Log(
                $"{logPrefix}: bundled '{relativeSource}' ('{runtimeIdentifier}' for '{platform}') into " +
                $"'{destination}' with {geometryCount} geometry file(s).");
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

            if (string.IsNullOrWhiteSpace(runtimeIdentifier)) {
                throw new BuildFailedException(
                    $"{logPrefix}: '{config.name}' names no published runtime identifier for build target " +
                    $"'{platform}'; fill the matching field in, or clear the asset's enabled flag.");
            }

            string trimmed = runtimeIdentifier.Trim();
            if (platform == BuildTarget.StandaloneOSX) RequireMacArchitectureMatch(config, trimmed);

            return trimmed;
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

        /// <summary>
        /// Refuses a macOS bundle whose runtime identifier is not the architecture the player is built
        /// for.
        /// </summary>
        /// <remarks>
        /// macOS is the one standalone target whose architecture is not part of
        /// <see cref="BuildTarget"/>: it is an editor setting, so an Apple-silicon player and an Intel
        /// player are the same target. The two are not interchangeable — an <c>osx-arm64</c> server
        /// cannot run on an Intel Mac at all, and an <c>osx-x64</c> server needs Rosetta 2, which a Mac
        /// running an Apple-silicon player has never been prompted to install.
        /// </remarks>
        private static void RequireMacArchitectureMatch(ServerBundleConfig config, string runtimeIdentifier) {
            string architecture = ReadMacArchitecture();

            if (architecture == null) {
                Debug.LogWarning(
                    $"{logPrefix}: this editor has no macOS build extension to read the player's architecture from, " +
                    $"so '{config.name}' bundling '{runtimeIdentifier}' is taken on trust.");
                return;
            }

            string expected = ResolveMacRuntimeIdentifier(architecture);

            if (expected == null) {
                Debug.LogWarning(
                    $"{logPrefix}: the macOS player is built for '{architecture}' but a bundle ships one server " +
                    $"('{runtimeIdentifier}'); Macs of the other architecture need Rosetta 2 to host.");
                return;
            }

            if (string.Equals(expected, runtimeIdentifier, StringComparison.Ordinal)) return;

            throw new BuildFailedException(
                $"{logPrefix}: the macOS player is built for '{architecture}' but '{config.name}' bundles " +
                $"'{runtimeIdentifier}'; that server cannot run beside it. Publish and name '{expected}'.");
        }

        /// <summary>
        /// The macOS player's architecture as the editor's own setting names it, or null when this
        /// editor has no macOS build support installed.
        /// </summary>
        /// <remarks>
        /// Reached by reflection because the type lives in the macOS build extension, which is a
        /// separate install: referencing it directly would stop the editor assembly compiling on every
        /// machine that has not installed it, which is every Linux and Windows machine by default.
        /// </remarks>
        private static string ReadMacArchitecture() {
            Type settingsType = Type.GetType(
                "UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            PropertyInfo property = settingsType?.GetProperty(
                "architecture", BindingFlags.Public | BindingFlags.Static);

            return property?.GetValue(null)?.ToString();
        }

        /// <summary>The runtime identifier an architecture needs, or null for a universal player.</summary>
        private static string ResolveMacRuntimeIdentifier(string architecture) {
            if (string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase)) {
                return macIntelRuntimeIdentifier;
            }

            if (string.Equals(architecture, "ARM64", StringComparison.OrdinalIgnoreCase)) {
                return macAppleSiliconRuntimeIdentifier;
            }

            return null;
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
        /// Refuses a publish folder that is not named for the runtime identifier being bundled.
        /// </summary>
        /// <remarks>
        /// The per-RID layout is the only thing standing between a mac player and a folder full of ELF
        /// binaries: on every platform but Windows the published executable has the same name whatever
        /// it was built for, so <see cref="RequireBundledExecutable"/> cannot tell them apart. The folder
        /// name can, and it is the one part of the path an author writes on purpose.
        /// </remarks>
        private static void RequireRuntimeIdentifierFolder(
            ServerBundleConfig config, string sourceDirectory, string runtimeIdentifier, string relativeSource) {
            string folderName = new DirectoryInfo(sourceDirectory).Name;

            if (string.Equals(folderName, runtimeIdentifier, StringComparison.Ordinal)) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' takes its '{runtimeIdentifier}' server from '{relativeSource}', whose " +
                $"folder is named '{folderName}'. The publish root holds one folder per runtime identifier, so a " +
                "folder named anything else is another platform's output or a hand-made copy.");
        }

        /// <summary>
        /// Builds the bundle beside its destination and swaps it in, failing the build on any fault.
        /// </summary>
        /// <remarks>
        /// The catch-all is the point. Unity logs a non-<see cref="BuildFailedException"/> thrown from a
        /// post-process callback and lets the build finish <c>Succeeded</c>, so an <c>IOException</c>
        /// half-way through a copy would otherwise ship a torn server folder beside a player the build
        /// called good.
        /// </remarks>
        private static int SwapInFreshBundle(
            ServerBundleConfig config,
            string sourceDirectory,
            string destination,
            string runtimeIdentifier,
            BuildTarget platform,
            string relativeSource) {
            string temporary = destination + temporarySuffix;
            string stale = destination + staleSuffix;

            try {
                return AssembleAndSwap(
                    config, sourceDirectory, destination, temporary, stale, runtimeIdentifier, platform,
                    relativeSource);
            } catch (BuildFailedException) {
                TryDeleteDirectory(temporary);
                throw;
            } catch (Exception exception) {
                TryDeleteDirectory(temporary);
                throw new BuildFailedException(
                    $"{logPrefix}: bundling '{relativeSource}' into '{destination}' failed part-way and no server " +
                    $"was copied: {exception.GetType().Name}: {exception.Message}");
            }
        }

        /// <summary>Assembles the whole bundle in a temporary folder, verifies it, then moves it in.</summary>
        private static int AssembleAndSwap(
            ServerBundleConfig config,
            string sourceDirectory,
            string destination,
            string temporary,
            string stale,
            string runtimeIdentifier,
            BuildTarget platform,
            string relativeSource) {
            RequireSeparatePaths(config, sourceDirectory, destination);

            DeleteDirectory(temporary);
            DeleteDirectory(stale);
            RequireSourceIntact(config, sourceDirectory, relativeSource, destination);

            int copiedFileCount = CopyDirectory(sourceDirectory, temporary);
            int geometryCount = WriteBundledConfig(config, temporary, runtimeIdentifier, platform);

            VerifyBundle(config, sourceDirectory, temporary, copiedFileCount, platform, relativeSource);
            SwapDirectories(destination, temporary, stale);
            RequireSourceIntact(config, sourceDirectory, relativeSource, destination);

            return geometryCount;
        }

        /// <summary>
        /// Puts the finished bundle in place with renames, restoring the previous one if the move fails.
        /// </summary>
        /// <remarks>
        /// Renames rather than a delete and a copy, so the window in which the destination holds neither
        /// the old bundle nor the new one is one filesystem operation wide instead of a whole tree copy.
        /// The temporary and stale folders are siblings of the destination on purpose: a rename across
        /// volumes is not a rename.
        /// </remarks>
        private static void SwapDirectories(string destination, string temporary, string stale) {
            bool hadPrevious = Directory.Exists(destination);

            if (hadPrevious) Directory.Move(destination, stale);

            try {
                Directory.Move(temporary, destination);
            } catch (Exception) {
                if (hadPrevious) Directory.Move(stale, destination);
                throw;
            }

            if (!hadPrevious) return;

            DeleteDirectory(stale);
        }

        /// <summary>
        /// Checks the assembled bundle holds everything the publish did, plus what this step wrote.
        /// </summary>
        /// <remarks>
        /// The file count is what catches a copy that stopped early; the executable and the stamp are
        /// what catch a publish that never held a server in the first place. Checking only for the
        /// executable is not enough — it sorts first in most publishes, so it is the one file a torn copy
        /// is most likely to have landed.
        /// </remarks>
        private static void VerifyBundle(
            ServerBundleConfig config,
            string sourceDirectory,
            string bundleDirectory,
            int copiedFileCount,
            BuildTarget platform,
            string relativeSource) {
            int sourceFileCount = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories).Length;

            if (copiedFileCount != sourceFileCount) {
                throw new BuildFailedException(
                    $"{logPrefix}: copying '{relativeSource}' landed {copiedFileCount} of its {sourceFileCount} " +
                    "file(s); the publish changed while it was being read. Nothing was replaced.");
            }

            RequireBundledExecutable(config, bundleDirectory, platform, relativeSource);
            RequireBundleStamp(bundleDirectory, relativeSource);
        }

        /// <summary>
        /// Checks that the copy landed the file the launcher will try to run.
        /// </summary>
        /// <remarks>
        /// This covers the ways the publish itself can be wrong: a folder holding something else
        /// entirely, and an executable name that no longer matches what was published. A publish holding
        /// another platform's output is caught earlier, by the runtime identifier folder name, because
        /// on every platform but Windows the file names are identical.
        /// </remarks>
        private static void RequireBundledExecutable(
            ServerBundleConfig config, string bundleDirectory, BuildTarget platform, string relativeSource) {
            string fileName = ResolveExecutableFileName(config, platform);

            if (File.Exists(Path.Combine(bundleDirectory, fileName))) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{relativeSource}', copied into '{bundleDirectory}', holds no '{fileName}' for build " +
                $"target '{platform}'; what was copied is not a server the launcher could run. Check the publish's " +
                $"runtime identifier and the asset's executable name ('{config.executableName}').");
        }

        private static void RequireBundleStamp(string bundleDirectory, string relativeSource) {
            string stampPath = Path.Combine(bundleDirectory, LocalServerPaths.ConfigFolderName, stampFileName);

            if (File.Exists(stampPath)) return;

            throw new BuildFailedException(
                $"{logPrefix}: bundling '{relativeSource}' wrote no '{stampFileName}', so the config folder the " +
                "server reads did not land. Nothing was replaced.");
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
        /// resolves the bundled server against. The name has to be one folder and nothing else — this
        /// step replaces whatever it points at, and <c>.</c> points at the player that was just built.
        /// </remarks>
        private static string ResolveDestination(BuildReport report, ServerBundleConfig config) {
            string folderName = config.ResolveBundleFolderName();

            if (string.IsNullOrEmpty(folderName)) {
                throw new BuildFailedException(
                    $"{logPrefix}: LocalServerConfig '{config.localServer.name}' names its bundled server folder " +
                    $"'{config.localServer.bundledServerFolderName}', which is not a single folder name. This step " +
                    "replaces that folder beside the player, so a path, '.' or '..' would replace something else — " +
                    "the player itself, at worst.");
            }

            string playerDirectory = Path.GetDirectoryName(report.summary.outputPath);

            return Path.GetFullPath(Path.Combine(playerDirectory ?? string.Empty, folderName));
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

        /// <summary>
        /// Refuses a bundle with no session config to export.
        /// </summary>
        /// <remarks>
        /// A server running on library defaults binds a different port from the client standing next to
        /// it, which is a player that cannot host — the same class of fault as a missing executable.
        /// <see cref="SessionConfigValidator"/> already fails on it, and one state cannot have two
        /// answers depending on who looked.
        /// </remarks>
        private static void RequireSessionConfig(ServerBundleConfig config) {
            if (config.sessionConfig != null) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' names no session config, so the bundled server would run on library " +
                "defaults rather than this build's settings. Name one, or clear the asset's enabled flag.");
        }

        /// <summary>Writes everything the copied server reads: session config, stamp and geometry.</summary>
        private static int WriteBundledConfig(
            ServerBundleConfig config, string bundleDirectory, string runtimeIdentifier, BuildTarget platform) {
            string configDirectory = Path.Combine(bundleDirectory, LocalServerPaths.ConfigFolderName);
            Directory.CreateDirectory(configDirectory);

            SessionConfigExporter.Export(
                config.sessionConfig, Path.Combine(configDirectory, SessionConfigExporter.DefaultFileName));
            WriteBundleStamp(config, configDirectory, runtimeIdentifier, platform);

            return CopyGeometry(config, configDirectory);
        }

        /// <summary>
        /// Records which publish this bundle came from, for whoever is holding the build later.
        /// </summary>
        /// <remarks>
        /// Informational only, and deliberately so: nothing reads it back.
        /// <see cref="LocalServerPaths"/> and <see cref="LocalServerLauncher"/> resolve the server from
        /// the launcher asset alone, so a stamp that is missing, stale or hand-edited cannot change how
        /// a player hosts. What it answers is the question a bug report otherwise cannot — which runtime
        /// identifier a shipped folder actually holds.
        /// </remarks>
        private static void WriteBundleStamp(
            ServerBundleConfig config, string configDirectory, string runtimeIdentifier, BuildTarget platform) {
            var stamp = new StringBuilder();
            stamp.Append("{\n");
            stamp.Append("  \"rid\": \"").Append(Escape(runtimeIdentifier)).Append("\",\n");
            stamp.Append("  \"exe\": \"").Append(Escape(ResolveExecutableFileName(config, platform))).Append("\",\n");
            stamp.Append("  \"builtAt\": \"").Append(UtcTimestamp()).Append("\"\n");
            stamp.Append("}\n");

            File.WriteAllText(Path.Combine(configDirectory, stampFileName), stamp.ToString());
        }

        private static string UtcTimestamp() {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        /// <summary>Escapes the two characters an authored name could use to break out of a JSON string.</summary>
        private static string Escape(string value) {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
        /// Refuses a source and destination where either contains the other, symbolic links resolved.
        /// </summary>
        /// <remarks>
        /// Building the player into the publish root, or publishing into the build folder, makes one a
        /// child of the other — which would copy a folder into itself, or delete the publish as the old
        /// bundle. <see cref="Path.GetFullPath"/> alone cannot see that: it normalises <c>.</c> and
        /// <c>..</c> but follows no links, so a publish symlinked next to the build looks like a
        /// perfectly separate path right up until the swap removes it.
        /// </remarks>
        private static void RequireSeparatePaths(
            ServerBundleConfig config, string sourceDirectory, string destination) {
            string source = WithTrailingSeparator(RequireResolvedPath(config, sourceDirectory, "publish folder"));
            string target = WithTrailingSeparator(RequireResolvedPath(config, destination, "bundle folder"));

            if (!target.StartsWith(source, StringComparison.Ordinal)
                && !source.StartsWith(target, StringComparison.Ordinal)) {
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' would copy '{sourceDirectory}' into '{destination}', which resolves " +
                "to the same place (or to one inside the other). Build the player outside the publish folder, or " +
                "publish outside the build folder.");
        }

        /// <summary>The path with its symbolic link followed, failing the build when it cannot be.</summary>
        private static string RequireResolvedPath(ServerBundleConfig config, string path, string description) {
            string resolved = ResolveLinkedPath(path);

            if (resolved != null) return resolved;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' names a {description}, '{path}', which is a symbolic link this editor " +
                "cannot resolve, so there is no telling whether it points inside the other. Name the real folder " +
                "instead.");
        }

        /// <summary>
        /// A directory's real path, or null when it is a link whose target cannot be read.
        /// </summary>
        /// <remarks>
        /// <c>ResolveLinkTarget</c> and <c>LinkTarget</c> arrived in .NET 6, which the editor's scripting
        /// runtime may predate, so both are reached by reflection. A runtime with neither can still tell
        /// that a directory <em>is</em> a link, and that is what this returns nothing for: refusing an
        /// unresolvable link is the only safe reading when the step is about to replace a folder.
        /// </remarks>
        private static string ResolveLinkedPath(string path) {
            string fullPath = Path.GetFullPath(path);
            var directory = new DirectoryInfo(fullPath);

            if (!directory.Exists) return fullPath;
            if ((directory.Attributes & FileAttributes.ReparsePoint) == 0) return fullPath;

            string target = ReadLinkTarget(directory);
            if (string.IsNullOrEmpty(target)) return null;
            if (Path.IsPathRooted(target)) return Path.GetFullPath(target);

            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, target));
        }

        /// <summary>The target of a symbolic link, read through whichever .NET 6 API this runtime has.</summary>
        private static string ReadLinkTarget(DirectoryInfo directory) {
            MethodInfo resolve = typeof(FileSystemInfo).GetMethod(
                "ResolveLinkTarget", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);

            if (resolve?.Invoke(directory, new object[] { true }) is FileSystemInfo resolved) {
                return resolved.FullName;
            }

            PropertyInfo linkTarget = typeof(FileSystemInfo).GetProperty(
                "LinkTarget", BindingFlags.Public | BindingFlags.Instance);

            return linkTarget?.GetValue(directory) as string;
        }

        /// <summary>A full path that always ends in a separator, so one folder cannot prefix another.</summary>
        private static string WithTrailingSeparator(string path) {
            string fullPath = Path.GetFullPath(path);
            string separator = Path.DirectorySeparatorChar.ToString();

            if (fullPath.EndsWith(separator, StringComparison.Ordinal)) return fullPath;

            return fullPath + separator;
        }

        /// <summary>
        /// Checks the publish is still there and still has something in it.
        /// </summary>
        /// <remarks>
        /// Run after every delete this step performs. The lexical and link checks cover the shapes that
        /// can be reasoned about; a bind mount, a hard-linked tree and a case-only difference on Windows
        /// cannot be, and the fact worth knowing is the same in all of them — emptying something emptied
        /// the publish too.
        /// </remarks>
        private static void RequireSourceIntact(
            ServerBundleConfig config, string sourceDirectory, string relativeSource, string destination) {
            if (Directory.Exists(sourceDirectory)
                && Directory.GetFileSystemEntries(sourceDirectory).Length > 0) {
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' emptied a folder beside '{destination}' and that emptied the publish " +
                $"at '{relativeSource}' as well — the two resolve to the same place. Publish outside the build " +
                "folder.");
        }

        /// <summary>Copies a directory tree, returning how many files landed.</summary>
        private static int CopyDirectory(string sourceDirectory, string destinationDirectory) {
            Directory.CreateDirectory(destinationDirectory);
            int copied = 0;

            foreach (string filePath in Directory.GetFiles(sourceDirectory)) {
                File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), true);
                copied++;
            }

            foreach (string childPath in Directory.GetDirectories(sourceDirectory)) {
                copied += CopyDirectory(childPath, Path.Combine(destinationDirectory, Path.GetFileName(childPath)));
            }

            return copied;
        }

        /// <summary>
        /// Deletes a directory tree, clearing the read-only flags that would otherwise refuse.
        /// </summary>
        /// <remarks>
        /// A previous bundle can hold read-only content — a publish restored from an archive, or a file
        /// Windows marked while it was locked — and a delete that throws on it would abandon a build for
        /// a reason nobody caused.
        /// </remarks>
        private static void DeleteDirectory(string directory) {
            if (!Directory.Exists(directory)) return;

            foreach (string filePath in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) {
                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) == 0) continue;

                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(directory, true);
        }

        /// <summary>Clears a half-made bundle without hiding the failure that produced it.</summary>
        private static void TryDeleteDirectory(string directory) {
            try {
                DeleteDirectory(directory);
            } catch (Exception exception) {
                Debug.LogWarning(
                    $"{logPrefix}: could not remove the half-made bundle at '{directory}': {exception.Message}");
            }
        }
    }
}
