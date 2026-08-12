using System.Collections.Generic;
using AlpineLib.UI;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Flow inspector for <see cref="UIScreenCoordinator"/>: a row per listed screen with a preview
    /// button that solos it in edit mode, nested coordinators drawn indented beneath the screen that
    /// owns them, a boot-state reset, and a populate button that fills the list from the hierarchy.
    /// In play mode the buttons drive the live coordinator instead.
    /// </summary>
    /// <remarks>
    /// Previewing writes real CanvasGroup values into the scene — alpha, interactable, raycasts —
    /// never <c>SetActive</c>, mirroring the runtime contract that hidden screens stay active. The
    /// writes go through Undo so a preview is one Ctrl+Z from gone, and any residue left in a saved
    /// scene is harmless: the coordinator re-seeds every listed screen in <c>Awake</c>, so play mode
    /// always starts from the authored flow, not from whatever was last previewed.
    /// </remarks>
    [CustomEditor(typeof(UIScreenCoordinator))]
    public class UIScreenCoordinatorEditor : UnityEditor.Editor {
        private const float indentWidth = 16f;
        private const float buttonWidth = 64f;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            var coordinator = (UIScreenCoordinator)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(Application.isPlaying ? "Live Flow" : "Screen Preview", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawFlowBody(coordinator);
            EditorGUILayout.EndVertical();

            if (Application.isPlaying) {
                Repaint();
                return;
            }

            DrawEditModeActions(coordinator);
        }

        private void DrawFlowBody(UIScreenCoordinator coordinator) {
            if (coordinator.Screens.Count == 0) {
                EditorGUILayout.HelpBox("No screens listed. Add entries above or use Populate From Children.", MessageType.Info);
                return;
            }

            if (Application.isPlaying) {
                DrawLiveRows(coordinator);
                return;
            }

            DrawPreviewRows(coordinator, new List<(UIScreenCoordinator Owner, UIScreen Screen)>(), 0);
        }

        // -------------------------------------------------------------- play mode

        private static void DrawLiveRows(UIScreenCoordinator coordinator) {
            string currentName = coordinator.CurrentScreen != null ? coordinator.CurrentScreen.name : "—";
            EditorGUILayout.LabelField("Current Screen", currentName);

            foreach (UIScreenEntry entry in coordinator.Screens) {
                if (entry?.Screen == null) continue;
                if (!DrawScreenRow(entry.Screen.name, entry.Screen.IsVisible, 0, "Show")) continue;

                coordinator.ShowScreen(entry.Screen);
            }
        }

        // -------------------------------------------------------------- edit mode preview

        /// <summary>
        /// Draws one coordinator's rows, recursing into coordinators directly owned by each screen.
        /// <paramref name="chain"/> carries the solo steps needed to make this coordinator's screens
        /// actually visible — previewing a nested screen first solos every ancestor screen above it.
        /// </summary>
        private void DrawPreviewRows(UIScreenCoordinator owner, List<(UIScreenCoordinator Owner, UIScreen Screen)> chain, int indent) {
            foreach (UIScreenEntry entry in owner.Screens) {
                if (entry?.Screen == null) {
                    DrawMissingRow(indent);
                    continue;
                }

                if (DrawScreenRow(entry.Screen.name, IsPreviewVisible(entry.Screen), indent, "Preview")) {
                    PreviewThroughChain(chain, owner, entry.Screen);
                }

                DrawNestedCoordinators(owner, entry.Screen, chain, indent);
            }
        }

        private void DrawNestedCoordinators(UIScreenCoordinator owner, UIScreen screen, List<(UIScreenCoordinator Owner, UIScreen Screen)> chain, int indent) {
            foreach (UIScreenCoordinator child in FindDirectChildCoordinators(owner, screen)) {
                DrawChildHeader(child, indent + 1);

                var childChain = new List<(UIScreenCoordinator Owner, UIScreen Screen)>(chain) { (owner, screen) };
                DrawPreviewRows(child, childChain, indent + 2);
            }
        }

        /// <summary>Solos every ancestor screen on the chain, then the picked screen itself.</summary>
        private static void PreviewThroughChain(List<(UIScreenCoordinator Owner, UIScreen Screen)> chain, UIScreenCoordinator leafOwner, UIScreen leafScreen) {
            foreach ((UIScreenCoordinator Owner, UIScreen Screen) step in chain) {
                SoloScreen(step.Owner, step.Screen);
            }

            SoloScreen(leafOwner, leafScreen);
        }

        /// <summary>
        /// The edit-mode mirror of <see cref="UIScreenCoordinator.ShowScreen(UIScreen, bool)"/>:
        /// the target fully visible, every sibling fully hidden, and each coordinator directly owned
        /// by the target reset to its first screen — so the preview shows exactly the state runtime
        /// switching would produce.
        /// </summary>
        private static void SoloScreen(UIScreenCoordinator owner, UIScreen targetScreen) {
            foreach (UIScreenEntry entry in owner.Screens) {
                if (entry?.Screen == null) continue;

                SetPreviewVisibility(entry.Screen, entry.Screen == targetScreen);
            }

            if (targetScreen == null) return;

            foreach (UIScreenCoordinator child in FindDirectChildCoordinators(owner, targetScreen)) {
                SoloScreen(child, FirstListedScreen(child));
            }
        }

        private static void SetPreviewVisibility(UIScreen screen, bool isVisible) {
            var canvasGroup = screen.GetComponent<CanvasGroup>();
            if (canvasGroup == null) return;

            Undo.RecordObject(canvasGroup, "Preview UI Screen");
            canvasGroup.alpha = isVisible ? 1f : 0f;
            canvasGroup.interactable = isVisible;
            canvasGroup.blocksRaycasts = isVisible;
            PrefabUtility.RecordPrefabInstancePropertyModifications(canvasGroup);
        }

        private static bool IsPreviewVisible(UIScreen screen) {
            var canvasGroup = screen.GetComponent<CanvasGroup>();
            return canvasGroup != null && canvasGroup.alpha > 0f;
        }

        // -------------------------------------------------------------- actions

        private void DrawEditModeActions(UIScreenCoordinator coordinator) {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset to Boot State")) {
                SoloScreen(coordinator, FirstListedScreen(coordinator));
            }

            if (GUILayout.Button("Populate From Children")) {
                PopulateFromChildren(coordinator);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Fills the screens list with every descendant <see cref="UIScreen"/> whose nearest
        /// coordinator is this one, in hierarchy order, inheriting the coordinator's fade duration.
        /// </summary>
        private void PopulateFromChildren(UIScreenCoordinator coordinator) {
            var owned = new List<UIScreen>();
            foreach (UIScreen candidate in coordinator.GetComponentsInChildren<UIScreen>(true)) {
                if (FindNearestCoordinatorAbove(candidate.transform) != coordinator) continue;

                owned.Add(candidate);
            }

            if (!ConfirmReplaceList(coordinator, owned.Count)) return;

            SerializedProperty screensProperty = serializedObject.FindProperty("screens");
            screensProperty.arraySize = owned.Count;

            for (int index = 0; index < owned.Count; index++) {
                SerializedProperty element = screensProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("screen").objectReferenceValue = owned[index];
                element.FindPropertyRelative("fadeDurationOverride").floatValue = -1f;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static bool ConfirmReplaceList(UIScreenCoordinator coordinator, int foundCount) {
            if (coordinator.Screens.Count == 0) return true;

            return EditorUtility.DisplayDialog(
                "Replace screens?",
                $"Replace the {coordinator.Screens.Count} listed screens with the {foundCount} found in children?",
                "Replace",
                "Cancel");
        }

        // -------------------------------------------------------------- hierarchy walks
        // Duplicated from the runtime coordinator rather than widening its API: the walk is ten
        // lines, and the editor needs it against arbitrary inspected objects.

        private static List<UIScreenCoordinator> FindDirectChildCoordinators(UIScreenCoordinator owner, UIScreen screen) {
            var owned = new List<UIScreenCoordinator>();

            foreach (UIScreenCoordinator candidate in screen.GetComponentsInChildren<UIScreenCoordinator>(true)) {
                if (candidate == owner) continue;
                if (!IsDirectlyOwned(candidate.transform, screen.transform)) continue;

                owned.Add(candidate);
            }

            return owned;
        }

        private static bool IsDirectlyOwned(Transform candidate, Transform screenRoot) {
            if (candidate == screenRoot) return true;

            for (Transform ancestor = candidate.parent; ancestor != null && ancestor != screenRoot; ancestor = ancestor.parent) {
                if (ancestor.GetComponent<UIScreenCoordinator>() != null) return false;
            }

            return true;
        }

        private static UIScreenCoordinator FindNearestCoordinatorAbove(Transform screenTransform) {
            for (Transform ancestor = screenTransform.parent; ancestor != null; ancestor = ancestor.parent) {
                UIScreenCoordinator coordinator = ancestor.GetComponent<UIScreenCoordinator>();
                if (coordinator != null) return coordinator;
            }

            return null;
        }

        private static UIScreen FirstListedScreen(UIScreenCoordinator coordinator) {
            foreach (UIScreenEntry entry in coordinator.Screens) {
                if (entry?.Screen != null) return entry.Screen;
            }

            return null;
        }

        // -------------------------------------------------------------- rows

        private static bool DrawScreenRow(string label, bool isVisible, int indent, string buttonLabel) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * indentWidth);
            EditorGUILayout.LabelField((isVisible ? "● " : "○ ") + label);
            bool isPressed = GUILayout.Button(buttonLabel, GUILayout.Width(buttonWidth));
            EditorGUILayout.EndHorizontal();

            return isPressed;
        }

        private static void DrawChildHeader(UIScreenCoordinator child, int indent) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * indentWidth);
            EditorGUILayout.LabelField(child.gameObject.name, EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMissingRow(int indent) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * indentWidth);
            EditorGUILayout.LabelField("○ (missing screen)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
