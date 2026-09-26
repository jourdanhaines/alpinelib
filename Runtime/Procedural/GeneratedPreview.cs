using System;
using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// The editor-preview plumbing for a component that generates children: a DontSave rebuild queued
    /// from <c>OnEnable</c>/<c>OnValidate</c> and a clear when disabled, so saved scenes and prefabs only
    /// hold the generator's settings.
    /// </summary>
    /// <remarks>
    /// The owner forwards its own <c>OnEnable</c>, <c>OnValidate</c> and <c>OnDisable</c> here and creates
    /// the instance lazily (a field initializer cannot capture <c>this</c>). Rebuilds are deferred through
    /// <c>EditorApplication.delayCall</c> because Unity forbids creating objects inside <c>OnValidate</c>;
    /// batchmode never fires delayCall, so batch callers use <see cref="RebuildNow"/>. Play mode builds are
    /// the owner's job (typically from <c>Awake</c>).
    /// </remarks>
    public sealed class GeneratedPreview {
        /// <summary>Flags that keep a generated object out of saved scenes, prefabs and builds.</summary>
        public const HideFlags PreviewFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

        private readonly MonoBehaviour _owner;
        private readonly Action _rebuild;
        private readonly Action _clear;

#if UNITY_EDITOR
        private bool _previewQueued;
#endif

        /// <summary>Preview plumbing for <paramref name="owner"/>, which builds with <paramref name="rebuild"/> and tears down with <paramref name="clear"/>.</summary>
        public GeneratedPreview(MonoBehaviour owner, Action rebuild, Action clear) {
            _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            _rebuild = rebuild ?? throw new ArgumentNullException(nameof(rebuild));
            _clear = clear ?? throw new ArgumentNullException(nameof(clear));
        }

        /// <summary>
        /// True when the owner should show a preview: an edit-mode scene object or one open in prefab mode;
        /// never a prefab asset, another preview scene (e.g. <c>LoadPrefabContents</c>) or play mode.
        /// </summary>
        public bool IsPreviewable {
            get {
#if UNITY_EDITOR
                if (_owner == null || Application.isPlaying || !_owner.gameObject.scene.IsValid()) return false;
                if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(_owner)) return false;
                if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(_owner.gameObject) != null) return true;

                return !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(_owner.gameObject.scene);
#else
                return false;
#endif
            }
        }

        /// <summary>Forward from the owner's <c>OnEnable</c>: queues a preview rebuild.</summary>
        public void OnEnable() {
#if UNITY_EDITOR
            QueuePreview();
#endif
        }

        /// <summary>Forward from the owner's <c>OnValidate</c>: queues a preview rebuild.</summary>
        public void OnValidate() {
#if UNITY_EDITOR
            QueuePreview();
#endif
        }

        /// <summary>Forward from the owner's <c>OnDisable</c>: clears the preview once the hierarchy settles.</summary>
        public void OnDisable() {
#if UNITY_EDITOR
            if (Application.isPlaying) return;

            // Destroying children while the hierarchy is deactivating is not allowed.
            UnityEditor.EditorApplication.delayCall += ClearIfStillDisabled;
#endif
        }

        /// <summary>Rebuilds immediately, for batch callers where delayCall never fires.</summary>
        public void RebuildNow() {
            if (_owner == null) return;

            _rebuild();
        }

        /// <summary>Applies <see cref="PreviewFlags"/> to an object and every descendant.</summary>
        public static void MarkPreview(GameObject target) {
            if (target == null) return;

            foreach (Transform child in target.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.hideFlags = PreviewFlags;
            }
        }

        /// <summary>
        /// Destroys a generated child. In play mode it is deactivated and detached first, so the deferred
        /// destroy never leaves a stale child for a rebuild in the same frame to trip over.
        /// </summary>
        public static void DestroyChild(Transform child) {
            if (child == null) return;
            if (!Application.isPlaying) {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                return;
            }

            child.gameObject.SetActive(false);
            child.SetParent(null, true);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        /// <summary>Destroys any object: deferred in play mode, immediate otherwise.</summary>
        public static void DestroyAny(UnityEngine.Object target) {
            if (target == null) return;
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(target);
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }

#if UNITY_EDITOR
        // Several OnEnable/OnValidate calls in one frame share a single deferred rebuild.
        private void QueuePreview() {
            if (_previewQueued || !IsPreviewable) return;

            _previewQueued = true;
            UnityEditor.EditorApplication.delayCall += RebuildPreview;
        }

        private void RebuildPreview() {
            _previewQueued = false;
            if (_owner == null || !_owner.isActiveAndEnabled || !IsPreviewable) return;

            _rebuild();
        }

        private void ClearIfStillDisabled() {
            if (_owner == null || _owner.isActiveAndEnabled || Application.isPlaying) return;

            _clear();
        }
#endif
    }
}
