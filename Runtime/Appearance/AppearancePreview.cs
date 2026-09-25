using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Edit-mode preview helpers: pieces built outside play mode are never saved, and are destroyed
    /// immediately in edit mode or detached then destroyed in play mode.
    /// </summary>
    public static class AppearancePreview {
        /// <summary>Flags on every preview object, so a saved prefab or scene stays bare.</summary>
        public const HideFlags PreviewFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

#if UNITY_EDITOR
        // Every sweep meets the same locked object again; warn about each once per domain.
        private static readonly HashSet<int> WarnedLocked = new HashSet<int>();
#endif

        /// <summary>Marks <paramref name="root"/> and everything under it as preview.</summary>
        public static void MarkPreview(GameObject root) {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.hideFlags = PreviewFlags;
            }
        }

        /// <summary>True when <paramref name="target"/> carries the don't-save-in-editor flag.</summary>
        public static bool IsPreview(GameObject target) {
            return target != null && (target.hideFlags & HideFlags.DontSaveInEditor) != 0;
        }

        /// <summary>
        /// Destroys immediately in edit mode. In play mode a game object is deactivated and unparented
        /// first, so the deferred destroy never leaves it to be found by name.
        /// </summary>
        public static void DestroyAny(Object target) {
            if (target == null) return;
            if (!Application.isPlaying) {
                Object.DestroyImmediate(target);
                return;
            }

            if (target is GameObject gameObject) {
                gameObject.SetActive(false);
                gameObject.transform.SetParent(null, true);
            }

            Object.Destroy(target);
        }

        /// <summary>
        /// True for an object baked into a prefab instance, which the editor refuses to destroy.
        /// </summary>
        public static bool IsLockedByPrefab(GameObject target) {
#if UNITY_EDITOR
            if (Application.isPlaying) return false;
            if (!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(target)) return false;
            if (UnityEditor.PrefabUtility.IsAddedGameObjectOverride(target)) return false;
            if (UnityEditor.PrefabUtility.IsOutermostPrefabInstanceRoot(target)) return false;

            if (WarnedLocked.Add(target.GetInstanceID())) {
                Debug.LogWarning($"AppearancePreview::IsLockedByPrefab->{target.name} is baked into a prefab instance; remove it from the prefab.", target);
            }

            return true;
#else
            return false;
#endif
        }
    }
}
