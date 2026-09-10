using System.IO;
using AlpineLib.Server.Configuration;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// Host-level configuration: where the exported gameplay data lives and how this process should
    /// behave while serving it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gameplay configuration does NOT live here. It is authored in Unity, exported to JSON and loaded
    /// into the shared config mirrors at startup; this type only carries the knobs whoever launched the
    /// process owns.
    /// </para>
    /// <para>
    /// Two sources fill it, in order: the <c>AlpineServer</c> section of <c>appsettings.json</c>, then the
    /// command line. The command line wins because it is the more specific instruction — a launcher that
    /// asked for an ephemeral port means it, whatever the deployed file says.
    /// </para>
    /// </remarks>
    public sealed class ServerRuntimeOptions {
        /// <summary>Configuration section these options bind from.</summary>
        public const string SectionName = "AlpineServer";

        /// <summary>Directory holding the exported <c>.geo</c> files, relative to a config directory.</summary>
        public const string GeometryFolderName = "geometry";

        /// <summary>
        /// Path to the editor-exported session configuration JSON, relative to the content root.
        /// </summary>
        public string SessionConfigPath { get; set; } = Path.Combine("config", SessionConfigLoader.DefaultFileName);

        /// <summary>
        /// Directory holding the editor-exported <c>.geo</c> collision geometry, one file per scene,
        /// relative to the content root.
        /// </summary>
        /// <remarks>
        /// Geometry is deployment data in exactly the way the session config is: authored in Unity,
        /// exported, and shipped alongside the binary. A missing directory is a warning rather than a
        /// refusal to start, because a server with no geometry still runs — every scene falls back to flat
        /// ground at y = 0, which is what a server did before the exporter existed.
        /// </remarks>
        public string GeometryDirectory { get; set; } = Path.Combine("config", GeometryFolderName);

        /// <summary>Maximum number of concurrent sessions this process will host.</summary>
        public int MaxSessions { get; set; } = 8;

        /// <summary>
        /// How long the process may sit with no connections before it stops itself. Zero — the default —
        /// leaves it running, which is what a dedicated deployment wants.
        /// </summary>
        public int IdleExitSeconds { get; set; }

        /// <summary>
        /// Port to bind instead of the one the exported config names, or null to use the config's.
        /// Zero binds an ephemeral port, which the readiness line then announces.
        /// </summary>
        public int? PortOverride { get; set; }

        /// <summary>
        /// Folds a parsed command line over whatever the configuration file said. Absent flags change
        /// nothing.
        /// </summary>
        /// <remarks>
        /// <c>--config</c> names a directory rather than a file, because the two exports travel together:
        /// the session config sits directly in it and the geometry in a <c>geometry/</c> beneath it. That
        /// is one path for a launcher to pass instead of two it could get out of step.
        /// </remarks>
        public void ApplyArguments(ServerArguments arguments) {
            if (arguments == null) {
                return;
            }

            ApplyConfigDirectory(arguments.ConfigDirectory);

            if (arguments.Port.HasValue) {
                PortOverride = arguments.Port.Value;
            }

            if (arguments.IdleExitSeconds.HasValue) {
                IdleExitSeconds = arguments.IdleExitSeconds.Value;
            }

            if (arguments.MaxSessions.HasValue) {
                MaxSessions = arguments.MaxSessions.Value;
            }
        }

        /// <summary>Resolves the session config path against the content root, leaving rooted paths alone.</summary>
        public string ResolveSessionConfigPath(string contentRootPath) {
            return ResolvePath(contentRootPath, SessionConfigPath);
        }

        /// <summary>Resolves the geometry directory against the content root, leaving rooted paths alone.</summary>
        public string ResolveGeometryDirectory(string contentRootPath) {
            return ResolvePath(contentRootPath, GeometryDirectory);
        }

        /// <summary>
        /// Resolves a configured path against the content root and normalises it. Empty stays empty.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The result is absolute and free of <c>.</c> and <c>..</c> segments, because these paths end
        /// up in operator-facing log lines and in "no session config at X" messages — and
        /// <c>/opt/server/config/../../etc/session-config.json</c> is a sentence nobody can act on. It
        /// also means the same deployment reported from two different working directories reads the
        /// same. A configured path so malformed that the platform refuses to normalise it throws here,
        /// at startup, rather than becoming a file-not-found later.
        /// </para>
        /// <para>
        /// A relative path is only ever joined to the content root; it is never resolved against the
        /// process's working directory. With no content root to join it to there is nothing to
        /// normalise against, so it is handed back exactly as configured rather than being anchored
        /// wherever the launcher happened to be standing — the one way a deployment could name a
        /// different file from one launch to the next.
        /// </para>
        /// </remarks>
        public static string ResolvePath(string contentRootPath, string configuredPath) {
            if (string.IsNullOrWhiteSpace(configuredPath)) {
                return string.Empty;
            }

            if (Path.IsPathRooted(configuredPath)) {
                return Path.GetFullPath(configuredPath);
            }

            if (string.IsNullOrWhiteSpace(contentRootPath)) {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
        }

        private void ApplyConfigDirectory(string configDirectory) {
            if (string.IsNullOrWhiteSpace(configDirectory)) {
                return;
            }

            SessionConfigPath = Path.Combine(configDirectory, SessionConfigLoader.DefaultFileName);
            GeometryDirectory = Path.Combine(configDirectory, GeometryFolderName);
        }
    }
}
