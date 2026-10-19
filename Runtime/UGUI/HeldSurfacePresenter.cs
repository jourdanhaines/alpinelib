using AlpineLib.Cameras;
using AlpineLib.Motion;
using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// Holds a <see cref="RenderTextureUISurface"/> up in front of the player: spawns a prop under a
    /// holder, poses it facing the eye, slides it in, and routes pointer input onto it.
    /// </summary>
    /// <remarks>
    /// The held prefab's root carries a child <see cref="SlideMotion"/> whose authored local position is
    /// the closed (lowered) pose; beneath it a renderer and a non-convex <see cref="MeshCollider"/> share
    /// the surface mesh. The instance is parented to the holder (typically the player's actor root) so it
    /// rides any moving carrier, posed once in world space at open, and kept fixed while open.
    ///
    /// The holder can be destroyed under the instance (player despawn), so every access is Unity-null
    /// aware and a later <see cref="Present"/> simply spawns a fresh instance. The holder should be
    /// uniformly scaled, or the posed surface is skewed.
    /// </remarks>
    public class HeldSurfacePresenter : MonoBehaviour {
        [Tooltip("Prop spawned under the holder: root → SlideMotion → Renderer + non-convex MeshCollider.")]
        [SerializeField] private GameObject heldPrefab;

        [Tooltip("Surface drawn onto the held prop.")]
        [SerializeField] private RenderTextureUISurface surface;

        [Tooltip("Raycaster routing the eye's pointer onto the held prop.")]
        [SerializeField] private MeshUVPointerRaycaster raycaster;

        [Tooltip("Closest distance in metres the prop is held from the eye.")]
        [SerializeField] private float preferredDistance = 0.45f;

        [Tooltip("Largest share of the view, on either axis, the prop may fill; it is pushed further out to fit.")]
        [SerializeField] private float maxViewFraction = 0.9f;

        [Tooltip("Layer forced onto the whole instance, so world cameras and physics can treat it apart.")]
        [SerializeField] private string heldLayerName = "HeldUI";

        private Transform _instance;
        private SlideMotion _slide;
        private Renderer _renderer;
        private MeshCollider _collider;
        private bool _warnedMissingLayer;
        private bool _warnedBeyondPointerReach;

        /// <summary>True from <see cref="Present"/> until <see cref="Dismiss"/>, including while sliding.</summary>
        public bool IsPresented { get; private set; }

        /// <summary>The spawned prop, or null before the first present and after it is destroyed.</summary>
        public Transform Instance => _instance != null ? _instance : null;

        /// <summary>Furthest the surface can be held and still take pointer input; infinite without a raycaster.</summary>
        public float MaxPointerDistance => raycaster != null ? raycaster.MaxDistance : float.PositiveInfinity;

        /// <summary>
        /// Spawns the prop under <paramref name="holder"/> if needed, poses it facing <paramref name="eye"/>
        /// and slides it up. Returns false when the prefab, holder or eye is missing or the prefab is malformed.
        /// </summary>
        public bool Present(Transform holder, Camera eye) {
            if (heldPrefab == null || holder == null || eye == null) {
                Debug.LogWarning($"{nameof(HeldSurfacePresenter)} on '{name}': cannot present without a held prefab, holder and eye camera.", this);
                return false;
            }

            bool hadInstance = _instance != null;
            if (!EnsureInstance(holder)) return false;

            ForceLayer(_instance);
            if (!_slide.IsInitialised) _slide.Initialise();

            // A prop already up or sliding away is re-posed and reversed from where it is, never popped closed.
            bool alreadyShowing = hadInstance && _instance.gameObject.activeSelf;
            PoseInFrontOf(eye);
            _instance.gameObject.SetActive(true);
            if (!alreadyShowing) _slide.Snap(false);

            _slide.SetOpen(true, true);

            BindSurface(eye);
            IsPresented = true;
            return true;
        }

        /// <summary>Slides the prop away and stops pointer routing; <paramref name="instant"/> hides it this frame.</summary>
        public void Dismiss(bool instant) {
            IsPresented = false;
            if (raycaster != null) raycaster.ClearSource();
            if (_instance == null || _slide == null) {
                StopSurface();
                return;
            }

            _slide.SetOpen(false, !instant);
            if (instant) Conceal();
        }

        /// <summary>Destroys the instance and unbinds the surface, e.g. when the holder is going away.</summary>
        public void ReleaseInstance() {
            IsPresented = false;
            if (raycaster != null) raycaster.ClearSource();

            StopSurface();
            if (_instance != null) Destroy(_instance.gameObject);

            _instance = null;
            _slide = null;
            _renderer = null;
            _collider = null;
        }

        private void Update() {
            if (IsPresented && _instance == null) {
                ReleaseInstance();
                return;
            }

            if (IsPresented || _instance == null || _slide == null) return;
            if (!_instance.gameObject.activeSelf || _slide.Fraction > 0f) return;

            Conceal();
        }

        /// <summary>Spawns the prop, or re-parents a live one that is under a different holder.</summary>
        private bool EnsureInstance(Transform holder) {
            if (_instance != null) {
                if (_instance.parent != holder) _instance.SetParent(holder, true);
                return true;
            }

            GameObject spawned = Instantiate(heldPrefab, holder, false);
            _instance = spawned.transform;
            _slide = spawned.GetComponentInChildren<SlideMotion>(true);
            _collider = spawned.GetComponentInChildren<MeshCollider>(true);
            _renderer = _collider != null ? _collider.GetComponent<Renderer>() : null;
            if (_renderer == null) _renderer = spawned.GetComponentInChildren<Renderer>(true);

            if (_slide != null && _collider != null && _renderer != null) return true;

            Debug.LogError($"{nameof(HeldSurfacePresenter)} on '{name}': held prefab '{heldPrefab.name}' needs a SlideMotion, a MeshCollider and a Renderer.", this);
            ReleaseInstance();
            return false;
        }

        private void PoseInFrontOf(Camera eye) {
            Vector3 meshSize = _collider.sharedMesh != null ? _collider.sharedMesh.bounds.size : Vector3.zero;
            Vector3 scaledSize = Vector3.Scale(meshSize, _collider.transform.lossyScale);
            Vector2 surfaceSize = new Vector2(Mathf.Abs(scaledSize.x), Mathf.Abs(scaledSize.y));

            float distance = CameraFacingPose.FitDistance(eye, surfaceSize, preferredDistance, maxViewFraction);
            WarnIfBeyondPointerReach(distance);
            Pose pose = CameraFacingPose.InFrontOf(eye.transform, distance);
            _instance.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void BindSurface(Camera eye) {
            _collider.enabled = true;

            if (surface != null) {
                surface.Bind(_renderer);
                surface.SetRendering(true);
            }

            if (raycaster != null) raycaster.SetSource(eye, _collider);
        }

        private void WarnIfBeyondPointerReach(float distance) {
            if (_warnedBeyondPointerReach || distance <= MaxPointerDistance) return;

            _warnedBeyondPointerReach = true;
            Debug.LogWarning($"{nameof(HeldSurfacePresenter)} on '{name}': surface held at {distance:0.00} m, beyond the raycaster's {MaxPointerDistance:0.00} m reach; pointer input will miss it.", this);
        }

        private void StopSurface() {
            if (surface == null) return;

            surface.Unbind();
            surface.SetRendering(false);
        }

        /// <summary>Hides the settled prop and stops the surface rendering.</summary>
        private void Conceal() {
            _instance.gameObject.SetActive(false);
            if (_collider != null) _collider.enabled = false;
            if (surface != null) surface.SetRendering(false);
        }

        private void ForceLayer(Transform root) {
            int layer = LayerMask.NameToLayer(heldLayerName);
            if (layer < 0) {
                WarnMissingLayerOnce();
                return;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.layer = layer;
            }
        }

        private void WarnMissingLayerOnce() {
            if (_warnedMissingLayer) return;

            _warnedMissingLayer = true;
            Debug.LogWarning($"{nameof(HeldSurfacePresenter)} on '{name}': layer '{heldLayerName}' is not defined; the held prop keeps its authored layers.", this);
        }
    }
}
