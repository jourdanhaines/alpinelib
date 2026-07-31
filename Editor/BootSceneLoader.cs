using UnityEditor;
using UnityEditor.SceneManagement;

namespace AlpineLib.Editor {
    /// <summary>
    /// Redirects play mode to a designated boot scene no matter which scene is open in the editor,
    /// so a code driven bootstrap flow is always exercised. Dirty scenes are offered for saving
    /// first and play is aborted when the user declines.
    /// </summary>
    [InitializeOnLoad]
    public static class BootSceneLoader {
        private const string enabledPreferenceKey = "AlpineLib.Editor.BootSceneLoader.Enabled";
        private const string bootScenePathPreferenceKey = "AlpineLib.Editor.BootSceneLoader.BootScenePath";
        private const string toggleMenuPath = "AlpineLib/Editor/Play From Boot Scene";

        static BootSceneLoader() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Whether entering play mode is redirected to <see cref="BootScenePath"/>. Defaults to on.
        /// </summary>
        public static bool IsEnabled {
            get => EditorPrefs.GetBool(enabledPreferenceKey, true);
            set => EditorPrefs.SetBool(enabledPreferenceKey, value);
        }

        /// <summary>
        /// Project relative path of the scene play mode starts from. Returns the stored per machine
        /// override when one is set, otherwise the first scene of the build settings scene list.
        /// Assign an empty string to fall back to the build settings scene again.
        /// </summary>
        public static string BootScenePath {
            get {
                var overridePath = EditorPrefs.GetString(bootScenePathPreferenceKey, string.Empty);
                if (!string.IsNullOrEmpty(overridePath)) return overridePath;

                var buildScenes = EditorBuildSettings.scenes;
                if (buildScenes.Length == 0) return string.Empty;

                return buildScenes[0].path;
            }
            set => EditorPrefs.SetString(bootScenePathPreferenceKey, value ?? string.Empty);
        }

        [MenuItem(toggleMenuPath)]
        private static void ToggleEnabled() {
            IsEnabled = !IsEnabled;
        }

        [MenuItem(toggleMenuPath, true)]
        private static bool ValidateToggleEnabled() {
            Menu.SetChecked(toggleMenuPath, IsEnabled);
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (!IsEnabled) return;

            var bootScenePath = BootScenePath;
            if (string.IsNullOrEmpty(bootScenePath)) return;

            var currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.path == bootScenePath) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                EditorApplication.isPlaying = false;
                return;
            }

            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(bootScenePath);
        }
    }
}
