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
        [Header("Executable")]
        [Tooltip("File name of the published server, without an extension. '.exe' is appended on Windows.")]
        public string executableName = "Game.Server";
        [Tooltip("Project-relative directory the editor launches the server from, usually a publish output.")]
        public string editorServerDirectory = "Build/Server/linux-x64";
        [Tooltip("Folder name the build step copies the server into, beside the player executable.")]
        public string bundledServerFolderName = "Server";

        [Header("Launch")]
        [Tooltip("Port the server is asked to bind. Taken ports fall back to an ephemeral one automatically.")]
        public int preferredPort = 9050;
        [Tooltip("How long to wait for the server's readiness line before giving up and killing it.")]
        public float readyTimeoutSeconds = 15f;
        [Tooltip("Seconds with no players after which the server exits on its own, so a crashed client leaves nothing behind.")]
        public int idleExitSeconds = 30;
    }
}
