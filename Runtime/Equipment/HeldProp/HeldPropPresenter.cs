using System;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Spawns a prop under a holder and keeps it where an <see cref="IHeldPropAnchor"/> says, e.g.
    /// between two hands that are animated and bent every frame.
    /// </summary>
    /// <remarks>
    /// Runs at execution order 300, after the actor, its pose modifiers and the camera rigs, so a
    /// continuous anchor reads this frame's final bones. The instance is parented to the holder so it
    /// rides any moving carrier between poses, and its whole hierarchy is forced onto
    /// <c>heldLayerName</c> at spawn. The holder can be destroyed under the instance (player despawn),
    /// so every access is Unity-null aware and a later present spawns a fresh one.
    /// </remarks>
    [DefaultExecutionOrder(300)]
    public class HeldPropPresenter : MonoBehaviour {
        [Tooltip("Prop spawned under the holder.")]
        [SerializeField] private GameObject heldPrefab;

        [Tooltip("Layer forced onto the whole instance, so world cameras and physics can treat it apart.")]
        [SerializeField] private string heldLayerName = "HeldUI";

        private Transform _instance;
        private IHeldPropAnchor _anchor;
        private bool _poseOnce;
        private bool _warnedMissingLayer;

        /// <summary>Raised once per spawned instance, after its layer is forced and before it is first posed.</summary>
        public event Action<Transform> InstanceSpawned;

        /// <summary>True while the prop is up (subclasses may count a slide-in as presented).</summary>
        public bool IsPresented { get; protected set; }

        /// <summary>The spawned prop, or null before the first present and after it is destroyed.</summary>
        public Transform Instance => _instance != null ? _instance : null;

        /// <summary>Anchor posing the prop, null when presented some other way or concealed.</summary>
        public IHeldPropAnchor Anchor => _anchor;

        /// <summary>Prefab spawned under the holder; null when unassigned.</summary>
        protected GameObject HeldPrefab => heldPrefab;

        /// <summary>
        /// Spawns the prop under <paramref name="holder"/> if needed and poses it from
        /// <paramref name="anchor"/>; a continuous anchor keeps posing it every <c>LateUpdate</c>. The
        /// prop stays hidden until the anchor first resolves a pose, so it never shows at a stale one.
        /// </summary>
        public virtual bool Present(Transform holder, IHeldPropAnchor anchor) {
            if (heldPrefab == null || holder == null || anchor == null) {
                Debug.LogWarning($"{GetType().Name} on '{name}': cannot present without a held prefab, holder and anchor.", this);
                return false;
            }

            if (!EnsureInstance(holder)) return false;

            _anchor = anchor;
            _poseOnce = true;
            _instance.gameObject.SetActive(ApplyAnchorPose());
            IsPresented = true;
            return true;
        }

        /// <summary>Hides the prop at once and stops posing it; the instance is kept for the next present.</summary>
        public virtual void Conceal() {
            IsPresented = false;
            ClearAnchor();
            if (_instance != null) _instance.gameObject.SetActive(false);
        }

        /// <summary>Destroys the instance, e.g. when the holder is going away.</summary>
        public virtual void ReleaseInstance() {
            IsPresented = false;
            ClearAnchor();
            if (_instance != null) Destroy(_instance.gameObject);

            _instance = null;
        }

        protected virtual void Update() {
            if (IsPresented && _instance == null) ReleaseInstance();
        }

        /// <summary>Re-poses from the anchor: every frame for a continuous one, once after present otherwise.</summary>
        protected virtual void LateUpdate() {
            if (!IsPresented || _anchor == null || _instance == null) return;
            if (!_anchor.IsContinuous && !_poseOnce) return;
            if (!ApplyAnchorPose()) return;

            _poseOnce = false;
            if (!_instance.gameObject.activeSelf) _instance.gameObject.SetActive(true);
        }

        protected void ClearAnchor() {
            _anchor = null;
            _poseOnce = false;
        }

        /// <summary>Spawns the prop, or re-parents a live one that is under a different holder.</summary>
        protected bool EnsureInstance(Transform holder) {
            if (_instance != null) {
                if (_instance.parent != holder) _instance.SetParent(holder, true);
                return true;
            }

            if (heldPrefab == null || holder == null) return false;

            GameObject spawned = Instantiate(heldPrefab, holder, false);
            _instance = spawned.transform;
            ForceLayer(_instance);
            if (!OnInstanceCreated(spawned)) {
                ReleaseInstance();
                return false;
            }

            InstanceSpawned?.Invoke(_instance);
            return true;
        }

        /// <summary>Lets a subclass resolve and validate parts of a fresh instance; false releases it.</summary>
        protected virtual bool OnInstanceCreated(GameObject spawned) {
            return true;
        }

        protected void ForceLayer(Transform root) {
            int layer = LayerMask.NameToLayer(heldLayerName);
            if (layer < 0) {
                WarnMissingLayerOnce();
                return;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.layer = layer;
            }
        }

        private bool ApplyAnchorPose() {
            if (!_anchor.TryResolvePose(_instance, out Pose pose)) return false;

            _instance.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }

        private void WarnMissingLayerOnce() {
            if (_warnedMissingLayer) return;

            _warnedMissingLayer = true;
            Debug.LogWarning($"{GetType().Name} on '{name}': layer '{heldLayerName}' is not defined; the held prop keeps its authored layers.", this);
        }
    }
}
