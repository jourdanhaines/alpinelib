using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// Where the dedicated server executable is, and how it should be launched, when a build hosts by
    /// starting one beside itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two directories rather than one because the editor and a player look for the server in different
    /// places, and neither location can be derived from the other. In the editor it is wherever the
    /// publish step drops it inside the project; in a player it is a folder copied next to the build by
    /// the post-build step. A single field would force one of the two to be wrong in the inspector.
    /// </para>
    /// <para>
    /// The port here is only a preference. The server reports the port it actually bound to on its
    /// readiness line, and a launcher that cannot have this one retries on an ephemeral port, so the
    /// value never has to be reserved or kept unique across two editors on one machine.
    /// </para>
    /// <para>
    /// Stopping is two-stage, and <see cref="idleExitSeconds"/> is the third. A stop asks the server to
    /// shut itself down and waits <see cref="stopGraceSeconds"/> before killing it, so the guests still
    /// on it are told the host closed the session instead of timing out. A host who leaves a session
    /// other players are in does not stop the server at all — it is detached and left to its own idle
    /// exit — so a build that offers hosting must keep <see cref="idleExitSeconds"/> non-zero or a
    /// detached server has nothing left to reap it.
    /// </para>
    /// <para>
    /// The asking half is POSIX-only. A Windows child with redirected pipes and no shared console has
    /// no portable signal, so <see cref="stopGraceSeconds"/> is inert there and a stop kills outright.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "LocalServerConfig", menuName = "AlpineLib/Networking/Local Server Config")]
    public class LocalServerConfig : ScriptableObject {
        /// <summary>Lowest port that can be asked for; zero means "any free port".</summary>
        public const int MinimumPort = 0;

        /// <summary>Highest port a UDP socket can bind.</summary>
        public const int MaximumPort = 65535;

        /// <summary>
        /// Floor on the readiness wait, in seconds.
        /// </summary>
        /// <remarks>
        /// A shorter budget cannot be honoured anyway: process spawn plus a socket bind costs a good
        /// fraction of a second on a cold cache, so anything below this only ever reports a false
        /// timeout for a server that was about to answer.
        /// </remarks>
        public const float MinimumReadyTimeoutSeconds = 1f;

        /// <summary>Ceiling on the stop grace, in seconds.</summary>
        /// <remarks>
        /// A stop happens while the player watches a menu that has already said goodbye, so a budget
        /// longer than this reads as the game having hung rather than as a server being polite.
        /// </remarks>
        public const float MaximumStopGraceSeconds = 10f;

        [Header("Executable")]
        // Name the server binary itself, not a script that forks it: on Windows only the launched
        // process is killed, so a wrapper's child would outlive the game and keep holding the port.
        [Tooltip("File name of the published server, without an extension. '.exe' is appended on Windows.")]
        public string executableName = "Game.Server";
        [Tooltip("Project-relative directory the editor launches the server from, usually a publish output.")]
        public string editorServerDirectory = "Build/Server/linux-x64";
        [Tooltip("Folder name the build step copies the server into, beside the player executable.")]
        public string bundledServerFolderName = "Server";

        [Header("Launch")]
        // Not a Range: a 0..65535 slider is unusable for picking a port. The upper end is clamped in
        // code instead.
        [Min(MinimumPort)]
        [Tooltip("Port the server is asked to bind. Taken ports fall back to an ephemeral one automatically. 0 asks for any free port.")]
        public int preferredPort = 9050;
        [Min(MinimumReadyTimeoutSeconds)]
        [Tooltip("How long to wait for the server's readiness line before giving up and killing it.")]
        public float readyTimeoutSeconds = 15f;
        [Min(0)]
        [Tooltip("Seconds with no players after which the server exits on its own, so a crashed client leaves nothing behind.")]
        public int idleExitSeconds = 30;
        [Min(0)]
        [Tooltip("Seconds the server is given to close its sessions after a stop request before it is killed outright. Ignored on Windows, where a child with redirected pipes has no portable way to be asked.")]
        public float stopGraceSeconds = 2f;

        /// <summary>The preferred port, forced into the range a socket can actually bind.</summary>
        public int ClampedPreferredPort() {
            if (preferredPort < MinimumPort) return MinimumPort;
            if (preferredPort > MaximumPort) return MaximumPort;

            return preferredPort;
        }

        /// <summary>The idle-exit budget, never negative.</summary>
        public int ClampedIdleExitSeconds() {
            return idleExitSeconds < 0 ? 0 : idleExitSeconds;
        }

        /// <summary>The grace given to a stop request before the kill, in milliseconds.</summary>
        /// <remarks>
        /// Zero is a legitimate answer and means "kill it now": a project whose server has no shutdown
        /// work to do should not pay a wait on every leave. Anything above the cap is a stop the player
        /// reads as a hang, so it is clamped rather than honoured.
        /// </remarks>
        public int ClampedStopGraceMilliseconds() {
            if (stopGraceSeconds <= 0f) return 0;
            if (stopGraceSeconds > MaximumStopGraceSeconds) return (int)(MaximumStopGraceSeconds * 1000f);

            return (int)(stopGraceSeconds * 1000f);
        }

        /// <summary>The readiness budget actually waited out, floor applied.</summary>
        public float ClampedReadyTimeoutSeconds() {
            return readyTimeoutSeconds < MinimumReadyTimeoutSeconds ? MinimumReadyTimeoutSeconds : readyTimeoutSeconds;
        }
    }
}
