using System;
using System.IO;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// What a standalone build ships beside itself so it can host by launching a real server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="LocalServerConfig"/> because the two answer different questions at
    /// different times. This one is a build-time instruction — which publish output to copy, which
    /// session config to export beside it — and is read once, by the post-build step. The other is a
    /// runtime question the player asks every time it hosts. They meet at exactly one value, the folder
    /// the server is copied into, which is why the launcher's asset is referenced here rather than
    /// having its name spelled out a second time.
    /// </para>
    /// <para>
    /// The published directory is deliberately not produced by this asset. Publishing a .NET server is a
    /// command-line step with its own runtime identifier and trimming options; asking Unity to drive it
    /// would put a toolchain the editor cannot check inside a build callback.
    /// </para>
    /// <para>
    /// What the asset does own is <em>which</em> publish belongs to which player. A .NET publish is per
    /// runtime identifier, so the source is a root plus one folder per RID rather than a single path —
    /// a single path would copy whichever platform was published last beside every player built, and a
    /// Windows build shipping Linux binaries is a green build that cannot host.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "ServerBundleConfig", menuName = "AlpineLib/Networking/Server Bundle Config")]
    public class ServerBundleConfig : ScriptableObject {
        /// <summary>Folder the server is copied into when no launcher config names one.</summary>
        public const string DefaultBundleFolderName = "Server";

        [Header("Source")]
        [Tooltip("Project-relative root the server publishes into. Each platform's publish is a runtime-identifier folder beneath it.")]
        public string publishedServerRoot = "Build/Server";
        [Tooltip("Runtime identifier folder copied beside a Windows player, e.g. win-x64.")]
        public string windowsRuntimeIdentifier = "win-x64";
        [Tooltip("Runtime identifier folder copied beside a Linux player, e.g. linux-x64.")]
        public string linuxRuntimeIdentifier = "linux-x64";
        [Tooltip("Runtime identifier folder copied beside a macOS player. Must match the player's own architecture: osx-x64 for Intel, osx-arm64 for Apple silicon.")]
        public string macRuntimeIdentifier = "osx-arm64";
        [Tooltip("File name of the published server, without an extension. Checked against the launcher's.")]
        public string executableName = "Game.Server";
        [Tooltip("Project-relative directory holding exported .geo files. Empty ships no geometry.")]
        public string geometrySourceDirectory = string.Empty;

        [Header("Destination")]
        [Tooltip("The launcher config the player will resolve the copied server with. Supplies the folder name.")]
        public LocalServerConfig localServer;

        [Header("Exported Config")]
        [Tooltip("Session config exported to the bundled server's config folder as JSON.")]
        public SessionConfig sessionConfig;

        [Header("Build")]
        [Tooltip("Turns the post-build copy off without deleting the asset, for a build that will not host.")]
        public bool enabled = true;

        /// <summary>
        /// The project-relative publish folder for one runtime identifier.
        /// </summary>
        /// <remarks>
        /// Composed rather than authored per platform so the three RID fields stay the only thing that
        /// differs between platforms, and a project that moves its publish output edits one field. The
        /// separator is a forward slash on every platform on purpose: the authored value is
        /// project-relative and platform-neutral, and Windows accepts it wherever the result is used.
        /// </remarks>
        public string ResolvePublishedDirectory(string runtimeIdentifier) {
            if (string.IsNullOrWhiteSpace(publishedServerRoot)) return string.Empty;
            if (string.IsNullOrWhiteSpace(runtimeIdentifier)) return publishedServerRoot.Trim();

            return publishedServerRoot.Trim() + "/" + runtimeIdentifier.Trim();
        }

        /// <summary>
        /// The folder name the server is copied into, which has to be the one
        /// <see cref="LocalServerPaths.ResolveServerDirectory"/> will look in at runtime, or an empty
        /// string when the launcher names something that is not a folder name at all.
        /// </summary>
        /// <remarks>
        /// The empty string is a refusal, not a default. The build step replaces the folder this names,
        /// so a value that walks out of the player's own directory — a path, a rooted path, <c>.</c> or
        /// <c>..</c> — names something the build has no business replacing, and the player it just built
        /// is the likeliest candidate.
        /// </remarks>
        public string ResolveBundleFolderName() {
            if (localServer == null) return DefaultBundleFolderName;
            if (string.IsNullOrWhiteSpace(localServer.bundledServerFolderName)) return DefaultBundleFolderName;
            if (!IsSingleFolderName(localServer.bundledServerFolderName)) return string.Empty;

            return localServer.bundledServerFolderName.Trim();
        }

        /// <summary>
        /// True when a name is one path segment: no separators, not rooted, and not a relative step.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every rule here is applied the same way on every platform, because the answer has to be. A
        /// backslash is an ordinary filename character on Linux and a colon is one too, so a name
        /// authored on Windows and validated on a Linux build machine would otherwise pass there and
        /// traverse — or fail to resolve at all — when the same project is built on Windows.
        /// </para>
        /// <para>
        /// A trailing dot is refused for the same reason: Windows strips trailing dots off a path's last
        /// component, so <c>'...'</c> would resolve to the player's own directory there and to a folder
        /// literally named <c>...</c> here. <c>'.hidden'</c> is a perfectly ordinary folder and stays
        /// allowed — it is the trailing dot, not the leading one, that normalises away.
        /// </para>
        /// </remarks>
        public static bool IsSingleFolderName(string folderName) {
            if (string.IsNullOrWhiteSpace(folderName)) return false;

            string trimmed = folderName.Trim();
            if (trimmed.EndsWith(".", StringComparison.Ordinal)) return false;
            if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0) return false;
            if (trimmed.IndexOf(':') >= 0) return false;
            if (Path.IsPathRooted(trimmed)) return false;

            return Path.GetFileName(trimmed) == trimmed;
        }
    }
}
