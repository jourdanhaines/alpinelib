using Unity.CodeEditor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Forces the active external script editor to regenerate its project files. Unity only does
    /// this on its own when assets change through the editor, so scripts added or removed outside
    /// Unity leave the solution stale until a sync is requested.
    /// </summary>
    public static class RegenerateProjectFiles {
        private const string autoSyncPreferenceKey = "AlpineLib.Editor.RegenerateProjectFiles.AutoSync";
        private const string regenerateMenuPath = "AlpineLib/Editor/Regenerate Project Files";
        private const string autoSyncMenuPath = "AlpineLib/Editor/Regenerate Project Files On Script Reload";

        /// <summary>
        /// Whether project files regenerate automatically after every script reload. Defaults to on.
        /// </summary>
        public static bool IsAutoSyncEnabled {
            get => EditorPrefs.GetBool(autoSyncPreferenceKey, true);
            set => EditorPrefs.SetBool(autoSyncPreferenceKey, value);
        }

        /// <summary>
        /// Regenerates project files through whichever external script editor is currently selected,
        /// so the behaviour is not tied to any one IDE integration package.
        /// </summary>
        [MenuItem(regenerateMenuPath)]
        public static void Regenerate() {
            CodeEditor.CurrentEditor.SyncAll();
            Debug.Log("[AlpineLib] Project files regenerated.");
        }

        [MenuItem(autoSyncMenuPath)]
        private static void ToggleAutoSync() {
            IsAutoSyncEnabled = !IsAutoSyncEnabled;
        }

        [MenuItem(autoSyncMenuPath, true)]
        private static bool ValidateToggleAutoSync() {
            Menu.SetChecked(autoSyncMenuPath, IsAutoSyncEnabled);
            return true;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded() {
            if (!IsAutoSyncEnabled) return;

            Regenerate();
        }
    }
}
