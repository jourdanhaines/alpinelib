using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
    /// not a single folder, a publish folder that is not there or holds nothing, a publish built for a
    /// different runtime identifier than the one being bundled, a publish that resolves to the same
    /// place as the destination, a missing session config, a launcher that names a different server
    /// from the bundle (or no launcher at all), and a copy that did not land everything the publish
    /// holds. The whole step runs inside one guard in <see cref="OnPostprocessBuild"/> that turns
    /// anything else thrown into the same failure — an unreadable manifest, a publish folder the build
    /// user cannot list, a full disk — because Unity logs any other exception and finishes the build
    /// <c>Succeeded</c>, which ships the previous build's server beside the new player. The two states
    /// that are a deliberate choice — no bundle config at all, and one whose <c>enabled</c> flag is off
    /// — log a line and return, because a build with no server in it is an ordinary thing to want and
    /// silence is what makes the missing <c>Server</c> folder a mystery afterwards.
    /// </para>
    /// <para>
    /// <b>Nothing after the swap fails the build.</b> Every check is a check on the publish or on the
    /// assembled copy, so all of them run before the rename that puts the bundle in place; once it is
    /// there the build has produced what it set out to produce. The one step left — removing the
    /// previous bundle's <c>.stale</c> folder — warns rather than fails, and leaves the folder alone
    /// altogether when the publish turns out to have been living inside it, because it is then the only
    /// copy. A build interrupted mid-swap leaves the previous bundle in <c>.stale</c> with nothing at
    /// the destination, and the next build puts it back before it clears anything.
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

        /// <summary>
        /// Suffix the previous bundle is renamed to while the new one is moved in.
        /// </summary>
        /// <remarks>
        /// Both suffixes are appended to the destination, so <c>&lt;bundle folder&gt;.stale</c> and
        /// <c>&lt;bundle folder&gt;.bundling</c> beside the player belong to this step and are cleared
        /// without asking. A project that ships a hand-made folder by either of those names beside its
        /// player would lose it; name it something else.
        /// </remarks>
        private const string staleSuffix = ".stale";

        /// <summary>Suffix of the manifest a .NET publish writes beside its executable.</summary>
        private const string dependencyManifestSuffix = ".deps.json";

        /// <summary>COFF machine value of a 32-bit Intel PE file.</summary>
        private const int peMachineX86 = 0x014C;

        /// <summary>COFF machine value of a 64-bit Intel PE file.</summary>
        private const int peMachineX64 = 0x8664;

        /// <summary>COFF machine value of a 64-bit ARM PE file.</summary>
        private const int peMachineArm64 = 0xAA64;

        private const string macIntelRuntimeIdentifier = "osx-x64";
        private const string macAppleSiliconRuntimeIdentifier = "osx-arm64";

        /// <summary><c>UnixFileMode</c>'s owner read, write and execute bits together, as 0700.</summary>
        private const int ownerReadWriteExecute = 448;

        private static readonly Type unixFileModeType = Type.GetType("System.IO.UnixFileMode, System.Runtime");
        private static readonly MethodInfo getUnixFileMode = ResolveFileModeAccessor("GetUnixFileMode", false);
        private static readonly MethodInfo setUnixFileMode = ResolveFileModeAccessor("SetUnixFileMode", true);

        /// <summary>Runs after the player's own post-build steps, so the output folder is settled.</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPostprocessBuild(BuildReport report) {
            if (report == null) return;
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            try {
                BundleConfiguredServer(report);
            } catch (BuildFailedException) {
                throw;
            } catch (Exception exception) {
                throw new BuildFailedException(
                    $"{logPrefix}: bundling the server failed before anything beside the player was replaced, so " +
                    $"this build ships whatever server was there already: {exception.GetType().Name}: " +
                    exception.Message);
            }
        }

        /// <summary>
        /// Copies the platform's publish, or says why this build ships no server.
        /// </summary>
        /// <remarks>
        /// Split out so that <see cref="OnPostprocessBuild"/> is the two target guards and nothing but
        /// the catch-all. Every file this step reads has to be inside that catch-all, and a body with
        /// statements on either side of a <c>try</c> is how one of them gets left out.
        /// </remarks>
        private static void BundleConfiguredServer(BuildReport report) {
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

            RequirePublishedRuntimeIdentifier(config, sourceDirectory, runtimeIdentifier, relativeSource, platform);
            RequireSessionConfig(config);
            RequireLauncherAgreement(config);

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

            if (!ServerBundleConfig.IsSingleFolderName(trimmed)) {
                throw new BuildFailedException(
                    $"{logPrefix}: '{config.name}' names '{trimmed}' as the runtime identifier for build target " +
                    $"'{platform}'. That is one folder beneath the publish root, so a path, a rooted value or a " +
                    "relative step names a folder the publish layout does not have.");
            }

            RequireRuntimeIdentifierPlatform(config, platform, trimmed);

            if (platform == BuildTarget.StandaloneOSX) RequireMacArchitectureMatch(config, trimmed);

            return trimmed;
        }

        /// <summary>
        /// Refuses a runtime identifier that names an operating system the player is not built for.
        /// </summary>
        /// <remarks>
        /// The publish path is composed from this field, so the folder's own name can never disagree
        /// with it and cannot catch the commonest way of getting it wrong: another platform's value
        /// typed into this platform's field. The identifier says which operating system it is for, the
        /// build target says which the player is for, and those two are authored independently.
        /// </remarks>
        private static void RequireRuntimeIdentifierPlatform(
            ServerBundleConfig config, BuildTarget platform, string runtimeIdentifier) {
            string named = ServerBundleConfig.ResolveRuntimeIdentifierPlatform(runtimeIdentifier);

            if (named == null) {
                Debug.LogWarning(
                    $"{logPrefix}: '{config.name}' names the runtime identifier '{runtimeIdentifier}' for build " +
                    $"target '{platform}'; this step does not recognise which operating system that is for, so it " +
                    "is taken on trust.");
                return;
            }

            string expected = ResolvePlatformName(platform);
            if (string.Equals(named, expected, StringComparison.Ordinal)) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{runtimeIdentifier}' for build target '{platform}', but that " +
                $"runtime identifier is {named} and the player is {expected}. A {named} server cannot run beside a " +
                $"{expected} player; the field for this platform is holding another platform's value.");
        }

        /// <summary>The operating system a standalone build target is for, or null for anything else.</summary>
        private static string ResolvePlatformName(BuildTarget platform) {
            switch (platform) {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ServerBundleConfig.WindowsPlatformName;
                case BuildTarget.StandaloneLinux64:
                    return ServerBundleConfig.LinuxPlatformName;
                case BuildTarget.StandaloneOSX:
                    return ServerBundleConfig.MacPlatformName;
                default:
                    return null;
            }
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
                    $"{logPrefix}: the macOS player is built for '{architecture}', which runs on Macs of both " +
                    $"architectures, but a bundle ships one server ('{runtimeIdentifier}'). An " +
                    $"'{macIntelRuntimeIdentifier}' server needs Rosetta 2 on an Apple-silicon Mac; an " +
                    $"'{macAppleSiliconRuntimeIdentifier}' one cannot run on an Intel Mac at all.");
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

        /// <summary>
        /// The absolute publish directory, failing the build when the platform has nothing published.
        /// </summary>
        /// <remarks>
        /// An empty folder is the same fault as a missing one and is the commoner of the two — a
        /// <c>dotnet publish</c> that failed, a fresh clone, a CI step that ran before the publish
        /// rather than after it all leave the directory behind. Both are named in one message so the
        /// author is never sent to look at paths that are perfectly correct.
        /// </remarks>
        private static string RequirePublishedDirectory(
            ServerBundleConfig config, string relativeSource, BuildTarget platform) {
            string sourceDirectory = ResolveProjectPath(relativeSource);

            if (HasContent(sourceDirectory)) return sourceDirectory;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' publishes '{platform}' from '{relativeSource}', which does not exist " +
                "or holds nothing. Publish the server for that runtime identifier before building, or clear the " +
                "asset's enabled flag.");
        }

        /// <summary>
        /// Refuses a publish that was not built for the runtime identifier being bundled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The folder's own name proves nothing: the publish path is composed from the runtime
        /// identifier, so comparing the last segment back to it compares a string to itself. What does
        /// prove it is what the publish says about itself, and it says it twice — in the
        /// <c>.deps.json</c> a framework-dependent publish writes, whose <c>runtimeTarget.name</c> ends
        /// in <c>/&lt;rid&gt;</c>, and in the header of the file the launcher starts.
        /// </para>
        /// <para>
        /// Either reading on its own is enough, because a real publish can be missing either: a
        /// single-file publish embeds its manifest in the executable, and the file the launcher starts
        /// can be a wrapper script rather than the apphost. A reading that <em>contradicts</em> the
        /// identifier fails the build whatever the other one said, and a folder where neither reading
        /// says anything is refused as well — this step is about to ship it as the server a player
        /// launches.
        /// </para>
        /// </remarks>
        private static void RequirePublishedRuntimeIdentifier(
            ServerBundleConfig config,
            string sourceDirectory,
            string runtimeIdentifier,
            string relativeSource,
            BuildTarget platform) {
            string manifestPath = ResolveDependencyManifest(config, sourceDirectory, relativeSource);
            string published = manifestPath == null ? null : ReadPublishedRuntimeIdentifier(manifestPath);
            bool manifestAgrees = published != null
                && string.Equals(published, runtimeIdentifier, StringComparison.OrdinalIgnoreCase);

            if (published != null && !manifestAgrees) {
                throw new BuildFailedException(
                    $"{logPrefix}: '{config.name}' bundles '{relativeSource}' as its '{runtimeIdentifier}' server, " +
                    $"but '{Path.GetFileName(manifestPath)}' in that folder says it was published for " +
                    $"'{published}'. That server cannot run beside this player. Publish the runtime identifier the " +
                    "asset names, or correct the asset.");
            }

            RequirePublishedExecutableIdentity(
                config, sourceDirectory, runtimeIdentifier, relativeSource, platform, manifestAgrees);
        }

        /// <summary>
        /// The one dependency manifest a publish root holds, or null when it holds none.
        /// </summary>
        /// <remarks>
        /// Found by its extension rather than by name, because the manifest is named after the assembly
        /// and not after the file the launcher starts: a Windows publish writes
        /// <c>&lt;assembly&gt;.deps.json</c> beside <c>&lt;assembly&gt;.exe</c>, so looking it up by the
        /// launched file's name would never find it — and Windows, whose two runtime identifiers share a
        /// magic number and a file name, is the platform with least else to go on. A publish root holds
        /// exactly one; two mean two applications were published into one folder, which is not a publish
        /// of anything in particular.
        /// </remarks>
        private static string ResolveDependencyManifest(
            ServerBundleConfig config, string sourceDirectory, string relativeSource) {
            List<string> manifests = FindDependencyManifests(sourceDirectory);

            if (manifests.Count == 0) return null;
            if (manifests.Count == 1) return Path.Combine(sourceDirectory, manifests[0]);

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{relativeSource}', which holds {manifests.Count} " +
                $"'{dependencyManifestSuffix}' manifests ({string.Join(", ", manifests)}); two applications were " +
                "published into one folder, so there is no telling which runtime identifier it holds. Publish each " +
                "server into a folder of its own.");
        }

        /// <summary>The names of every dependency manifest directly inside a publish root, in order.</summary>
        private static List<string> FindDependencyManifests(string sourceDirectory) {
            var manifests = new List<string>();

            foreach (string filePath in Directory.GetFiles(sourceDirectory)) {
                string fileName = Path.GetFileName(filePath);
                if (!fileName.EndsWith(dependencyManifestSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                manifests.Add(fileName);
            }

            manifests.Sort(StringComparer.Ordinal);
            return manifests;
        }

        /// <summary>
        /// The runtime identifier a dependency manifest records, or null when it records none.
        /// </summary>
        /// <remarks>
        /// Null covers two states that mean the same thing to the caller: a manifest this cannot parse,
        /// and a portable publish whose <c>runtimeTarget.name</c> carries a framework but no runtime
        /// identifier.
        /// </remarks>
        private static string ReadPublishedRuntimeIdentifier(string manifestPath) {
            Match match = Regex.Match(
                File.ReadAllText(manifestPath),
                "\"runtimeTarget\"\\s*:\\s*\\{[^}]*\"name\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.Singleline);

            if (!match.Success) return null;

            string targetName = match.Groups[1].Value;
            int separatorIndex = targetName.LastIndexOf('/');

            if (separatorIndex < 0 || separatorIndex == targetName.Length - 1) return null;

            return targetName.Substring(separatorIndex + 1);
        }

        /// <summary>
        /// Checks the launched file's own header against the runtime identifier, as far as it can say.
        /// </summary>
        /// <remarks>
        /// A header naming another operating system fails the build whatever the manifest said: the two
        /// disagreeing is a folder somebody assembled by hand. A header that says nothing is a wrapper
        /// script or a hand-written launcher, which is a legal thing to aim the launcher at, so it is
        /// refused only when no manifest confirmed the identifier either. A publish holding no such file
        /// at all is left to <see cref="RequireBundledExecutable"/>, which names that state.
        /// </remarks>
        private static void RequirePublishedExecutableIdentity(
            ServerBundleConfig config,
            string sourceDirectory,
            string runtimeIdentifier,
            string relativeSource,
            BuildTarget platform,
            bool manifestAgrees) {
            string executableFileName = ResolveExecutableFileName(config, platform);
            string executablePath = Path.Combine(sourceDirectory, executableFileName);

            if (!File.Exists(executablePath)) return;

            string expected = ServerBundleConfig.ResolveRuntimeIdentifierPlatform(runtimeIdentifier);

            if (expected == null) {
                Debug.LogWarning(
                    $"{logPrefix}: '{config.name}' names the runtime identifier '{runtimeIdentifier}', which this " +
                    "step does not recognise, so what the publish holds is taken on trust.");
                return;
            }

            string format = ReadExecutableFormat(executablePath);

            if (format == null) {
                RequireManifestWhereBytesAreSilent(config, executableFileName, relativeSource, manifestAgrees);
                return;
            }

            if (string.Equals(format, expected, StringComparison.Ordinal)) {
                RequireWindowsMachineMatch(
                    config, executablePath, executableFileName, runtimeIdentifier, relativeSource, format);
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{relativeSource}' as its '{runtimeIdentifier}' server, but " +
                $"'{executableFileName}' there is a {format} executable rather than a {expected} one. That server " +
                "cannot run beside this player. Publish the runtime identifier the asset names, or correct the asset.");
        }

        /// <summary>
        /// Accepts a launched file whose first bytes identify nothing, if a manifest identified the
        /// folder.
        /// </summary>
        /// <remarks>
        /// A wrapper script that sets a library path before starting the apphost is an ordinary server
        /// layout, and <see cref="LocalServerLauncher"/> starts whatever the launcher config names
        /// without caring what it is. The manifest beside it is what says the folder is the publish it
        /// claims to be; with no manifest either, nothing in the folder does, and shipping it would be a
        /// guess.
        /// </remarks>
        private static void RequireManifestWhereBytesAreSilent(
            ServerBundleConfig config, string executableFileName, string relativeSource, bool manifestAgrees) {
            if (manifestAgrees) {
                Debug.LogWarning(
                    $"{logPrefix}: '{config.name}' launches '{executableFileName}' from '{relativeSource}', whose " +
                    "first bytes are neither ELF, PE nor Mach-O — a wrapper script, most likely. The publish's " +
                    $"'{dependencyManifestSuffix}' manifest confirms the runtime identifier, so it is bundled as it " +
                    "stands.");
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{relativeSource}', which carries no " +
                $"'{dependencyManifestSuffix}' manifest naming a runtime identifier and whose " +
                $"'{executableFileName}' is not an executable of any standalone platform (its first bytes are " +
                "neither ELF, PE nor Mach-O), so nothing in the folder says what it was published for. Publish the " +
                "server with the .NET SDK rather than assembling the folder by hand.");
        }

        /// <summary>
        /// Refuses a Windows publish built for another processor.
        /// </summary>
        /// <remarks>
        /// <c>win-x64</c> and <c>win-arm64</c> are both PE files with the same file name, so the magic
        /// number says exactly the same thing about a server that cannot start. The COFF header's
        /// machine field is what tells them apart, and for a single-file publish — no manifest on disk —
        /// it is the only thing that can.
        /// </remarks>
        private static void RequireWindowsMachineMatch(
            ServerBundleConfig config,
            string executablePath,
            string executableFileName,
            string runtimeIdentifier,
            string relativeSource,
            string format) {
            if (!string.Equals(format, ServerBundleConfig.WindowsPlatformName, StringComparison.Ordinal)) return;

            string expected = ResolveRuntimeIdentifierArchitecture(runtimeIdentifier);
            if (expected == null) return;

            string machine = ReadWindowsMachine(executablePath);
            if (machine == null) return;
            if (string.Equals(machine, expected, StringComparison.Ordinal)) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{relativeSource}' as its '{runtimeIdentifier}' server, but " +
                $"'{executableFileName}' there is built for {machine} rather than {expected}. Windows will not run " +
                "it beside this player. Publish the runtime identifier the asset names, or correct the asset.");
        }

        /// <summary>The processor a runtime identifier names, or null when its last part is not one.</summary>
        private static string ResolveRuntimeIdentifierArchitecture(string runtimeIdentifier) {
            int separatorIndex = runtimeIdentifier.LastIndexOf('-');
            if (separatorIndex < 0) return null;

            string architecture = runtimeIdentifier.Substring(separatorIndex + 1).ToLowerInvariant();

            switch (architecture) {
                case "x86":
                case "x64":
                case "arm64":
                    return architecture;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The processor a PE file's COFF header names, or null when there is no header to read.
        /// </summary>
        /// <remarks>
        /// The DOS stub's last field, at <c>0x3C</c>, holds the offset of the <c>PE\0\0</c> signature,
        /// and the two bytes after that signature are the machine. Anything that does not line up — a
        /// truncated file, an <c>MZ</c> that is a real DOS binary rather than a PE — answers nothing
        /// rather than guessing, and the caller then trusts the magic number alone.
        /// </remarks>
        private static string ReadWindowsMachine(string executablePath) {
            using (FileStream stream = File.OpenRead(executablePath)) {
                return ResolveMachineName(ReadMachineField(stream, ReadPeHeaderOffset(stream)));
            }
        }

        /// <summary>The offset the DOS stub gives for the PE signature, or -1 when there is no stub.</summary>
        private static int ReadPeHeaderOffset(FileStream stream) {
            var stub = new byte[0x40];

            if (stream.Read(stub, 0, stub.Length) < stub.Length) return -1;

            return stub[0x3C] | (stub[0x3D] << 8) | (stub[0x3E] << 16) | (stub[0x3F] << 24);
        }

        /// <summary>The COFF machine value at a PE signature, or -1 when the signature is not there.</summary>
        private static int ReadMachineField(FileStream stream, int headerOffset) {
            if (headerOffset < 0 || headerOffset > stream.Length - 6) return -1;

            stream.Seek(headerOffset, SeekOrigin.Begin);
            var header = new byte[6];

            if (stream.Read(header, 0, header.Length) < header.Length) return -1;
            if (header[0] != 0x50 || header[1] != 0x45 || header[2] != 0x00 || header[3] != 0x00) return -1;

            return header[4] | (header[5] << 8);
        }

        /// <summary>The processor a COFF machine value names, or null for one .NET does not publish.</summary>
        private static string ResolveMachineName(int machine) {
            switch (machine) {
                case peMachineX86: return "x86";
                case peMachineX64: return "x64";
                case peMachineArm64: return "arm64";
                default: return null;
            }
        }

        /// <summary>The operating system an executable's first four bytes name, or null for neither.</summary>
        private static string ReadExecutableFormat(string executablePath) {
            var header = new byte[4];

            using (FileStream stream = File.OpenRead(executablePath)) {
                if (stream.Read(header, 0, header.Length) < header.Length) return null;
            }

            if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46) {
                return ServerBundleConfig.LinuxPlatformName;
            }

            if (header[0] == 0x4D && header[1] == 0x5A) return ServerBundleConfig.WindowsPlatformName;
            if (IsMachOHeader(header)) return ServerBundleConfig.MacPlatformName;

            return null;
        }

        /// <summary>
        /// True for every Mach-O magic number, thin or fat, in either byte order.
        /// </summary>
        /// <remarks>
        /// A macOS publish can be a thin binary for one architecture or a fat one holding both, and the
        /// magic is written in the target's byte order rather than the reader's — so all four values
        /// are checked in both directions rather than assuming the build machine's endianness.
        /// </remarks>
        private static bool IsMachOHeader(byte[] header) {
            uint magic = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);

            return magic == 0xFEEDFACF || magic == 0xFEEDFACE || magic == 0xCAFEBABE || magic == 0xCAFEBABF
                || magic == 0xCFFAEDFE || magic == 0xCEFAEDFE || magic == 0xBEBAFECA || magic == 0xBFBAFECA;
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
            bool swapped = false;

            try {
                return AssembleAndSwap(
                    config, sourceDirectory, destination, temporary, stale, runtimeIdentifier, platform,
                    relativeSource, ref swapped);
            } catch (BuildFailedException) {
                CleanUpAfterFailure(temporary, stale, swapped);
                throw;
            } catch (Exception exception) {
                CleanUpAfterFailure(temporary, stale, swapped);
                throw new BuildFailedException(ComposeFailureMessage(
                    relativeSource, destination, stale, swapped, exception));
            }
        }

        /// <summary>
        /// Clears the half-made bundle, and says where the previous one was left if the swap had run.
        /// </summary>
        /// <remarks>
        /// The stale folder is deliberately not deleted after a swap: the failures that can happen there
        /// include the publish having been the previous bundle's own child, in which case the stale
        /// folder is the only surviving copy of it.
        /// </remarks>
        private static void CleanUpAfterFailure(string temporary, string stale, bool swapped) {
            TryDeleteDirectory(temporary, "the half-made bundle");

            if (!swapped) return;
            if (!Directory.Exists(stale)) return;

            Debug.LogWarning(
                $"{logPrefix}: the new bundle is in place but the build failed after the swap, so the previous one " +
                $"is still at '{stale}'. Check it, then delete it.");
        }

        /// <summary>The message for a fault, which reads differently once the swap has happened.</summary>
        private static string ComposeFailureMessage(
            string relativeSource, string destination, string stale, bool swapped, Exception exception) {
            string fault = $"{exception.GetType().Name}: {exception.Message}";

            if (!swapped) {
                return $"{logPrefix}: bundling '{relativeSource}' into '{destination}' failed part-way and no server " +
                    $"was copied: {fault}";
            }

            return $"{logPrefix}: '{destination}' holds the new bundle, but finishing off after the swap failed and " +
                $"the previous bundle may still be at '{stale}': {fault}";
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
            string relativeSource,
            ref bool swapped) {
            RequireSeparatePaths(config, sourceDirectory, destination);
            RestoreInterruptedSwap(destination, stale);

            DeleteDirectory(temporary);
            DeleteDirectory(stale);
            RequireSourceIntact(config, sourceDirectory, relativeSource, destination);

            int copiedFileCount = CopyDirectory(sourceDirectory, temporary);
            int geometryCount = WriteBundledConfig(config, temporary, runtimeIdentifier, platform);

            VerifyBundle(config, sourceDirectory, temporary, copiedFileCount, platform, relativeSource);
            SwapDirectories(destination, temporary, stale);
            swapped = true;

            ClearPreviousBundle(stale, sourceDirectory, relativeSource);

            return geometryCount;
        }

        /// <summary>
        /// Removes the previous bundle, unless the swap carried the publish into it.
        /// </summary>
        /// <remarks>
        /// The rename that put the new bundle in place has already made the build correct, so nothing
        /// here fails it. What it does refuse to do is delete: a publish that was living inside the
        /// previous bundle moved with it, and the stale folder is then the only copy there is. The
        /// overlap checks catch that shape before anything is touched wherever the two paths can be
        /// resolved; a bind mount is where they cannot.
        /// </remarks>
        private static void ClearPreviousBundle(string stale, string sourceDirectory, string relativeSource) {
            if (!Directory.Exists(stale)) return;

            if (HasContent(sourceDirectory)) {
                TryDeleteDirectory(stale, "the previous bundle");
                return;
            }

            Debug.LogWarning(
                $"{logPrefix}: the new bundle is in place, but the publish at '{relativeSource}' went with the " +
                $"previous one, so '{stale}' is the only copy of it left and has not been removed. Publish outside " +
                "the build folder, then delete it.");
        }

        /// <summary>
        /// Puts a previous bundle back when a build died between the swap's two renames.
        /// </summary>
        /// <remarks>
        /// A destination that is missing while its stale folder exists is that crash and nothing else: a
        /// completed swap always leaves the destination there. Treating it as an ordinary leftover would
        /// delete the only surviving copy of the previous bundle, and the build that did so can still
        /// fail afterwards and leave the player with no server at all.
        /// </remarks>
        private static void RestoreInterruptedSwap(string destination, string stale) {
            if (Directory.Exists(destination)) return;
            if (!Directory.Exists(stale)) return;

            Directory.Move(stale, destination);

            Debug.LogWarning(
                $"{logPrefix}: '{destination}' was missing and '{stale}' was not, which is a build interrupted " +
                "mid-swap; the previous bundle has been put back before this build replaces it.");
        }

        /// <summary>
        /// Puts the finished bundle in place with renames, restoring the previous one if the move fails.
        /// </summary>
        /// <remarks>
        /// Renames rather than a delete and a copy, so the window in which the destination holds neither
        /// the old bundle nor the new one is one filesystem operation wide instead of a whole tree copy.
        /// The temporary and stale folders are siblings of the destination on purpose: a rename across
        /// volumes is not a rename. Removing the stale folder is the caller's job, so that the last
        /// fallible thing this does is the rename that makes the build correct.
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
        /// another platform's output is caught earlier, by
        /// <see cref="RequirePublishedRuntimeIdentifier"/>, because on every platform but Windows the
        /// file names are identical.
        /// </remarks>
        private static void RequireBundledExecutable(
            ServerBundleConfig config, string bundleDirectory, BuildTarget platform, string relativeSource) {
            string fileName = ResolveExecutableFileName(config, platform);

            if (File.Exists(Path.Combine(bundleDirectory, fileName))) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{relativeSource}', copied into '{bundleDirectory}', holds no '{fileName}' for build " +
                $"target '{platform}'; what was copied is not a server the launcher could run. Check the publish's " +
                $"runtime identifier and the asset's executable name ('{config.executableName}') — and note that a " +
                "publish made with the apphost turned off holds only the managed assembly, with no file of that " +
                "name to launch at all.");
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
        /// The name rules are checked, and then the path they produced is checked as well: name rules
        /// are per-platform guesses about what normalises away, and the resolved path is the fact.
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

            string playerDirectory = Path.GetFullPath(Path.GetDirectoryName(report.summary.outputPath) ?? ".");
            string destination = Path.GetFullPath(Path.Combine(playerDirectory, folderName));

            RequireChildOfPlayerDirectory(config, destination, playerDirectory, folderName);

            return destination;
        }

        /// <summary>Refuses a destination that is not a folder directly inside the player's own.</summary>
        private static void RequireChildOfPlayerDirectory(
            ServerBundleConfig config, string destination, string playerDirectory, string folderName) {
            bool isChild = !string.Equals(destination, playerDirectory, StringComparison.Ordinal)
                && string.Equals(
                    Path.GetDirectoryName(destination), playerDirectory, StringComparison.Ordinal);

            if (isChild) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' would replace '{destination}', which is not a folder inside the " +
                $"player's own '{playerDirectory}'. The bundled server folder name '{folderName}' normalises to " +
                "somewhere this step has no business replacing.");
        }

        /// <summary>
        /// Refuses a bundle the launcher beside it would not find.
        /// </summary>
        /// <remarks>
        /// Both halves are the same fault at different ends. Without a launcher asset the destination
        /// folder is a library default nothing confirmed; with one that names a different executable the
        /// build copies a working server into a folder the player then searches for a file that is not
        /// there. Either ships a player that cannot host, and <see cref="SessionConfigValidator"/> fails
        /// on both — one state cannot have two answers depending on who looked.
        /// </remarks>
        private static void RequireLauncherAgreement(ServerBundleConfig config) {
            if (config.localServer == null) {
                throw new BuildFailedException(
                    $"{logPrefix}: '{config.name}' names no LocalServerConfig, so it would copy the server into " +
                    $"'{ServerBundleConfig.DefaultBundleFolderName}' with nothing confirming that is where the player " +
                    "looks. Name the launcher config the player resolves the server with.");
            }

            if (config.localServer.executableName == config.executableName) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' bundles '{config.executableName}' but LocalServerConfig " +
                $"'{config.localServer.name}' launches '{config.localServer.executableName}'; the copied server " +
                "would never be found. Make the two names agree.");
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

        /// <summary>
        /// Writes everything the copied server reads: session config, stamp and geometry.
        /// </summary>
        /// <remarks>
        /// The bundle's <c>config</c> folder belongs to this step, not to the publish. A publish that
        /// carries one of its own has it written over, because the point of the export is that the
        /// server runs the numbers this build was made with.
        /// </remarks>
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

        /// <summary>
        /// Escapes everything an authored name could carry that a JSON string cannot hold literally.
        /// </summary>
        /// <remarks>
        /// Control characters are escaped as well as the two obvious ones: a runtime identifier or
        /// executable name with a stray tab or newline in it is an authoring slip, and writing it raw
        /// turns the stamp into JSON nothing can parse.
        /// </remarks>
        private static string Escape(string value) {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var escaped = new StringBuilder(value.Length);

            foreach (char character in value) {
                escaped.Append(EscapeCharacter(character));
            }

            return escaped.ToString();
        }

        private static string EscapeCharacter(char character) {
            switch (character) {
                case '\\': return "\\\\";
                case '"': return "\\\"";
                case '\n': return "\\n";
                case '\r': return "\\r";
                case '\t': return "\\t";
                default: return EscapeControlCharacter(character);
            }
        }

        private static string EscapeControlCharacter(char character) {
            if (character >= ' ') return character.ToString();

            return "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture);
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
        /// <c>..</c> but follows no links, so the two paths are compared twice, as they are written and
        /// as the filesystem lays them out. A link is only ever a question about overlap here: a player
        /// built into a symlinked output folder, or a project living under a symlinked home, is an
        /// ordinary thing to do and is not refused for it.
        /// </remarks>
        private static void RequireSeparatePaths(
            ServerBundleConfig config, string sourceDirectory, string destination) {
            RequireNoOverlap(config, sourceDirectory, destination, sourceDirectory, destination);

            string resolvedSource = ResolvePhysicalPath(sourceDirectory);
            string resolvedDestination = ResolvePhysicalPath(destination);

            if (resolvedSource == null || resolvedDestination == null) {
                Debug.LogWarning(
                    $"{logPrefix}: '{sourceDirectory}' or '{destination}' goes through a link this editor cannot " +
                    "follow, so the two were compared only as they are written. A link that puts one inside the " +
                    "other is caught when the publish is looked for again after the old bundle is cleared.");
                return;
            }

            RequireNoOverlap(config, resolvedSource, resolvedDestination, sourceDirectory, destination);
        }

        /// <summary>Refuses two paths where either one holds the other, comparing them as given.</summary>
        private static void RequireNoOverlap(
            ServerBundleConfig config,
            string first,
            string second,
            string sourceDirectory,
            string destination) {
            string source = WithTrailingSeparator(first);
            string target = WithTrailingSeparator(second);

            if (!target.StartsWith(source, StringComparison.Ordinal)
                && !source.StartsWith(target, StringComparison.Ordinal)) {
                return;
            }

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' would copy '{sourceDirectory}' into '{destination}', which resolves " +
                "to the same place (or to one inside the other). Build the player outside the publish folder, or " +
                "publish outside the build folder.");
        }

        /// <summary>
        /// A path as the filesystem lays it out, every link followed, or null when it cannot be asked.
        /// </summary>
        /// <remarks>
        /// The path need not exist — the destination does not on a first build — so the walk climbs to
        /// the deepest folder that does exist, resolves that, and puts the rest back on the end. A link
        /// anywhere above the leaf moves the leaf just as surely as one at it, which is why the whole
        /// path is resolved rather than its last component.
        /// </remarks>
        private static string ResolvePhysicalPath(string path) {
            string fullPath = Path.GetFullPath(path);

            if (Directory.Exists(fullPath)) return ResolvePhysicalDirectory(fullPath);

            string parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent)) return fullPath;

            string resolvedParent = ResolvePhysicalPath(parent);
            if (resolvedParent == null) return null;

            return Path.Combine(resolvedParent, Path.GetFileName(fullPath));
        }

        /// <summary>
        /// An existing directory's physical location, or null when this runtime cannot report one.
        /// </summary>
        /// <remarks>
        /// Entering the directory and asking where that is resolves every link in the path at once,
        /// ancestors included, because that is what a working directory is. The .NET 6 link API would
        /// answer for the last component only and the editor's scripting runtime does not have it at
        /// all. Windows reports the path as it was set rather than the physical one, so a junction there
        /// falls back to the comparison of the paths as written rather than failing anything.
        /// </remarks>
        private static string ResolvePhysicalDirectory(string directory) {
            string previous = Directory.GetCurrentDirectory();

            try {
                Directory.SetCurrentDirectory(directory);
                return Directory.GetCurrentDirectory();
            } catch (Exception) {
                return null;
            } finally {
                RestoreWorkingDirectory(previous);
            }
        }

        /// <summary>Puts the process's working directory back after a path has been resolved.</summary>
        private static void RestoreWorkingDirectory(string directory) {
            try {
                Directory.SetCurrentDirectory(directory);
            } catch (Exception) {
                // Nothing here can put it back, and every path this step works with is absolute anyway.
            }
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
        /// Run after the deletes this step performs and before it copies anything. The lexical and link
        /// checks cover the shapes that can be reasoned about; a bind mount, a hard-linked tree and a
        /// case-only difference on Windows cannot be, and the fact worth knowing is the same in all of
        /// them — emptying something emptied the publish too.
        /// </remarks>
        private static void RequireSourceIntact(
            ServerBundleConfig config, string sourceDirectory, string relativeSource, string destination) {
            if (HasContent(sourceDirectory)) return;

            throw new BuildFailedException(
                $"{logPrefix}: '{config.name}' emptied a folder beside '{destination}' and that emptied the publish " +
                $"at '{relativeSource}' as well — the two resolve to the same place. Publish outside the build " +
                "folder.");
        }

        /// <summary>True when a directory is there and holds something.</summary>
        private static bool HasContent(string directory) {
            return Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0;
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
        /// Deletes a directory tree, clearing whatever would otherwise refuse to be written.
        /// </summary>
        /// <remarks>
        /// A previous bundle can hold content nobody meant to protect — a publish restored from an
        /// archive, a file Windows marked while it was locked, a data folder a server wrote under a
        /// tight umask — and a delete that throws on it would abandon a build for a reason nobody
        /// caused. Directories matter more than files here: a directory without its write and execute
        /// bits refuses to give up its children whatever the children's own flags say.
        /// </remarks>
        private static void DeleteDirectory(string directory) {
            if (!Directory.Exists(directory)) return;

            foreach (string childPath in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)) {
                MakeWritable(childPath);
            }

            MakeWritable(directory);

            foreach (string filePath in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) {
                MakeWritable(filePath);
            }

            Directory.Delete(directory, true);
        }

        /// <summary>
        /// Clears the flags and permission bits that stop a path being deleted, as far as it can.
        /// </summary>
        /// <remarks>
        /// Both mechanisms are tried because the platforms do not share one: Windows refuses on the
        /// read-only attribute, Unix on the owner's write and execute bits, and only the second is what
        /// a <c>chmod 500</c> directory is holding out with. Failure is swallowed — this is best-effort
        /// preparation, and the delete that follows is where a genuine refusal should be reported from.
        /// </remarks>
        private static void MakeWritable(string path) {
            TryClearReadOnlyAttribute(path);
            TryAddOwnerUnixMode(path);
        }

        /// <summary>One of <c>File</c>'s Unix mode accessors, or null on a runtime that predates them.</summary>
        private static MethodInfo ResolveFileModeAccessor(string methodName, bool takesMode) {
            if (unixFileModeType == null) return null;

            Type[] parameterTypes = takesMode
                ? new[] { typeof(string), unixFileModeType }
                : new[] { typeof(string) };

            return typeof(File).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
        }

        private static void TryClearReadOnlyAttribute(string path) {
            try {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) == 0) return;

                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            } catch (Exception) {
                // Reported by the delete if it matters; a guess here would only hide it.
            }
        }

        /// <summary>
        /// Adds the owner's read, write and execute bits on a runtime and platform that have them.
        /// </summary>
        /// <remarks>
        /// <c>File.GetUnixFileMode</c> and <c>File.SetUnixFileMode</c> arrived in .NET 7, which the
        /// editor's scripting runtime may predate, so both are reached by reflection; on Windows they
        /// exist and throw, which is the same nothing.
        /// </remarks>
        private static void TryAddOwnerUnixMode(string path) {
            if (getUnixFileMode == null || setUnixFileMode == null) return;

            try {
                int mode = Convert.ToInt32(getUnixFileMode.Invoke(null, new object[] { path }));
                object widened = Enum.ToObject(unixFileModeType, mode | ownerReadWriteExecute);

                setUnixFileMode.Invoke(null, new[] { path, widened });
            } catch (Exception) {
                // Windows has no file mode and throws; the read-only attribute is that platform's answer.
            }
        }

        /// <summary>Clears a folder this step owns without hiding the failure that produced it.</summary>
        private static void TryDeleteDirectory(string directory, string description) {
            try {
                DeleteDirectory(directory);
            } catch (Exception exception) {
                Debug.LogWarning(
                    $"{logPrefix}: could not remove {description} at '{directory}': {exception.Message}");
            }
        }
    }
}
