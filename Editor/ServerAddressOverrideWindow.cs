using AlpineLib.Sessions;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Sets and clears the editor's server-address override, so play mode can dial a local server
    /// while the committed matchmaking asset keeps pointing at the one builds ship with.
    /// </summary>
    /// <remarks>
    /// The override lives in editor preferences — per user, per machine, per project — precisely so
    /// that "I test against localhost" never becomes a diff. The window shows what will actually be
    /// dialled, because an override that was set weeks ago and forgotten is otherwise a mystifying
    /// "why am I not on the live server" bug.
    /// </remarks>
    public class ServerAddressOverrideWindow : EditorWindow {
        private const string LocalhostAddress = "127.0.0.1:9050";

        private string _pendingAddress;

        [MenuItem("AlpineLib/Networking/Server Address Override")]
        public static void Open() {
            var window = GetWindow<ServerAddressOverrideWindow>("Server Override");
            window.minSize = new Vector2(340f, 120f);
        }

        private void OnEnable() {
            _pendingAddress = ServerAddressOverride.EditorOverride;
        }

        private void OnGUI() {
            string activeOverride = ServerAddressOverride.EditorOverride;

            EditorGUILayout.LabelField(
                "Play mode dials",
                string.IsNullOrEmpty(activeOverride) ? "the matchmaking asset's address" : activeOverride,
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4);
            _pendingAddress = EditorGUILayout.TextField("host:port", _pendingAddress);

            EditorGUILayout.BeginHorizontal();
            DrawSetButton();
            DrawLocalhostButton();
            DrawClearButton(activeOverride);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Editor only; builds never see this. Builds are repointed at launch with "
                + $"'{ServerAddressOverride.CommandLineFlag} host:port' or the "
                + $"{ServerAddressOverride.EnvironmentVariable} environment variable.",
                MessageType.Info);
        }

        private void DrawSetButton() {
            if (!GUILayout.Button("Set")) return;

            ServerAddressOverride.EditorOverride = _pendingAddress;
            _pendingAddress = ServerAddressOverride.EditorOverride;
        }

        private void DrawLocalhostButton() {
            if (!GUILayout.Button("Localhost")) return;

            ServerAddressOverride.EditorOverride = LocalhostAddress;
            _pendingAddress = LocalhostAddress;
        }

        private void DrawClearButton(string activeOverride) {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(activeOverride))) {
                if (!GUILayout.Button("Clear")) return;

                ServerAddressOverride.EditorOverride = string.Empty;
                _pendingAddress = string.Empty;
            }
        }
    }
}
