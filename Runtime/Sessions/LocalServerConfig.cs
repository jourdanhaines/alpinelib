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

        /// <summary>The readiness budget actually waited out, floor applied.</summary>
        public float ClampedReadyTimeoutSeconds() {
            return readyTimeoutSeconds < MinimumReadyTimeoutSeconds ? MinimumReadyTimeoutSeconds : readyTimeoutSeconds;
        }
    }
}
