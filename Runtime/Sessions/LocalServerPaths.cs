using System.IO;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// Turns a <see cref="LocalServerConfig"/> into the absolute paths a launcher needs: the directory
    /// the server was published to, the executable inside it, and the config folder it reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from the launcher because the answer differs per platform and per editor-or-player,
    /// while the launching itself does not. A build step that copies the server has to agree with this
    /// exactly, and it can only do that against something it can call.
    /// </para>
    /// <para>
    /// Everything is derived from <see cref="Application.dataPath"/> rather than the working directory:
    /// a player launched from a shortcut, from Steam or from a terminal has three different working
    /// directories and only one data path.
    /// </para>
    /// </remarks>
    public static class LocalServerPaths {
        /// <summary>Folder the server reads its exported session and scene configuration from.</summary>
        public const string ConfigFolderName = "config";

        /// <summary>Extension a published server carries on Windows and on no other platform.</summary>
        /// <remarks>
        /// Public because the build step has to check for the same file the launcher will later run, and
        /// a second copy of ".exe" in the editor assembly is a second thing to keep in step.
        /// </remarks>
        public const string WindowsExecutableExtension = ".exe";

        /// <summary>
        /// Levels between a macOS player's data path and the folder the <c>.app</c> bundle sits in.
        /// </summary>
        /// <remarks>
        /// A macOS player's data path <em>is</em> <c>&lt;Game&gt;.app/Contents</c>, so one step up reaches
        /// the bundle itself and a second reaches the folder the bundle sits in. Windows and Linux need
        /// only one step, their data folder being a sibling of the executable.
        /// </remarks>
        private const int MacBundleDepth = 2;

        /// <summary>
        /// The directory the server executable lives in: the project-relative publish output in the
        /// editor, the folder copied beside the build in a player.
        /// </summary>
        public static string ResolveServerDirectory(LocalServerConfig config) {
            if (config == null) return string.Empty;

            if (Application.isEditor) {
                return ResolveFromDataPath(1, config.editorServerDirectory);
            }

            int depth = Application.platform == RuntimePlatform.OSXPlayer ? MacBundleDepth : 1;
            return ResolveFromDataPath(depth, config.bundledServerFolderName);
        }

        /// <summary>The server executable itself, with the platform's extension applied.</summary>
        public static string ResolveExecutablePath(LocalServerConfig config) {
            string directory = ResolveServerDirectory(config);

            if (string.IsNullOrEmpty(directory)) return string.Empty;

            return Path.Combine(directory, ResolveExecutableFileName(config));
        }

        /// <summary>The <c>config</c> folder beside the executable, passed to the server on its command line.</summary>
        public static string ResolveConfigDirectory(LocalServerConfig config) {
            string directory = ResolveServerDirectory(config);

            if (string.IsNullOrEmpty(directory)) return string.Empty;

            return Path.Combine(directory, ConfigFolderName);
        }

        /// <summary>The executable's file name, <c>.exe</c> included on Windows.</summary>
        public static string ResolveExecutableFileName(LocalServerConfig config) {
            if (config == null || string.IsNullOrEmpty(config.executableName)) return string.Empty;

            if (!IsWindows()) return config.executableName;

            return config.executableName + WindowsExecutableExtension;
        }

        /// <summary>Walks <paramref name="levels"/> up from the data path and appends a relative folder.</summary>
        private static string ResolveFromDataPath(int levels, string relativePath) {
            string basePath = Application.dataPath;

            for (int level = 0; level < levels; level++) {
                basePath = Path.Combine(basePath, "..");
            }

            if (string.IsNullOrEmpty(relativePath)) return Path.GetFullPath(basePath);

            return Path.GetFullPath(Path.Combine(basePath, relativePath));
        }

        /// <summary>True when this process runs on Windows.</summary>
        /// <remarks>
        /// Two things hang off it and neither is about paths alone: the executable gains a
        /// <c>.exe</c> suffix, and Windows has no POSIX process group for a launcher to reap.
        /// </remarks>
        public static bool IsWindows() {
            RuntimePlatform platform = Application.platform;

            return platform == RuntimePlatform.WindowsPlayer
                || platform == RuntimePlatform.WindowsEditor
                || platform == RuntimePlatform.WindowsServer;
        }
    }
}
