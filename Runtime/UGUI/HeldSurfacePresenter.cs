using AlpineLib.Cameras;
using AlpineLib.Equipment;
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
    /// rides any moving carrier, posed in world space at open, and kept fixed while open.
    ///
    /// Posed once in <c>LateUpdate</c> after <see cref="Present"/> so camera rigs have written this frame's
    /// eye pose; the execution order puts it after the rigs and <see cref="CameraPerspectiveController"/>,
    /// so the provisional pose Present writes from <c>Update</c> is never drawn (a Present issued after
    /// this component's LateUpdate shows it for one frame).
    ///
    /// The holder can be destroyed under the instance (player despawn), so every access is Unity-null
    /// aware and a later <see cref="Present"/> simply spawns a fresh instance. The holder should be
    /// uniformly scaled, or the posed surface is skewed.
    ///
    /// Hand mode (<see cref="PresentHeld"/>) instead poses the prop every frame from an
    /// <see cref="IHeldPropAnchor"/> with the slide snapped open; the surface renders from the first
    /// frame but takes pointer input only after <see cref="SetPointerEnabled"/>, so a raise animation
    /// can finish first. <see cref="Conceal"/> hides a hand-held prop at once.
    /// </remarks>
    [DefaultExecutionOrder(300)]
    public class HeldSurfacePresenter : HeldPropPresenter {
        [Tooltip("Surface drawn onto the held prop.")]
        [SerializeField] private RenderTextureUISurface surface;

        [Tooltip("Raycaster routing the eye's pointer onto the held prop.")]
        [SerializeField] private MeshUVPointerRaycaster raycaster;

        [Tooltip("Closest distance in metres the prop is held from the eye.")]
        [SerializeField] private float preferredDistance = 0.45f;

        [Tooltip("Largest share of the view, on either axis, the prop may fill; it is pushed further out to fit.")]
        [SerializeField] private float maxViewFraction = 0.9f;

        private SlideMotion _slide;
        private Renderer _renderer;
        private MeshCollider _collider;
        private bool _warnedBeyondPointerReach;
        private Camera _pendingEye;
        private Camera _pointerEye;

        /// <summary>Furthest the surface can be held and still take pointer input; infinite without a raycaster.</summary>
        public float MaxPointerDistance => raycaster != null ? raycaster.MaxDistance : float.PositiveInfinity;

        /// <summary>
        /// Spawns the prop under <paramref name="holder"/> if needed, poses it facing <paramref name="eye"/>
        /// and slides it up. Returns false when the prefab, holder or eye is missing or the prefab is malformed.
        /// </summary>
        public bool Present(Transform holder, Camera eye) {
            if (HeldPrefab == null || holder == null || eye == null) {
                Debug.LogWarning($"{nameof(HeldSurfacePresenter)} on '{name}': cannot present without a held prefab, holder and eye camera.", this);
                return false;
            }

            bool hadInstance = Instance != null;
            if (!EnsureInstance(holder)) return false;
            if (_slide == null) {
                Debug.LogError($"{nameof(HeldSurfacePresenter)} on '{name}': held prefab '{HeldPrefab.name}' needs a SlideMotion to present in front of the eye.", this);
                return false;
            }

            ClearAnchor();
            ForceLayer(Instance);
            if (!_slide.IsInitialised) _slide.Initialise();

            // A prop already up or sliding away is re-posed and reversed from where it is, never popped closed.
            bool alreadyShowing = hadInstance && Instance.gameObject.activeSelf;
            PoseInFrontOf(eye);
            _pendingEye = eye;
            Instance.gameObject.SetActive(true);
            if (!alreadyShowing) _slide.Snap(false);

            _slide.SetOpen(true, true);

            BindSurface(eye);
            IsPresented = true;
            return true;
        }

        /// <summary>
        /// Hand mode: shows the prop at once (slide snapped open), posed every frame from
        /// <paramref name="anchor"/>, with the surface rendering. Pointer input stays off until
        /// <see cref="SetPointerEnabled"/>, which casts from <paramref name="eye"/>.
        /// </summary>
        public bool PresentHeld(Transform holder, IHeldPropAnchor anchor, Camera eye) {
            _pendingEye = null;
            _pointerEye = eye;
            if (raycaster != null) raycaster.ClearSource();
            if (!base.Present(holder, anchor)) return false;

            SnapSlideOpen();
            BindSurfaceRendering();
            return true;
        }

        /// <inheritdoc />
        /// <remarks>Hand mode with the last eye given; prefer <see cref="PresentHeld"/>.</remarks>
        public override bool Present(Transform holder, IHeldPropAnchor anchor) {
            return PresentHeld(holder, anchor, _pointerEye);
        }

        /// <summary>Starts or stops routing the eye's pointer onto the presented prop.</summary>
        public void SetPointerEnabled(bool enabled) {
            if (raycaster == null) return;

            if (!enabled || !IsPresented || _collider == null || _pointerEye == null) {
                raycaster.ClearSource();
                return;
            }

            raycaster.SetSource(_pointerEye, _collider);
        }

        /// <summary>Slides the prop away and stops pointer routing; <paramref name="instant"/> hides it this frame.</summary>
        public void Dismiss(bool instant) {
            IsPresented = false;
            ClearAnchor();
            _pendingEye = null;
            _pointerEye = null;
            if (raycaster != null) raycaster.ClearSource();
            if (Instance == null || _slide == null) {
                StopSurface();
                return;
            }

            _slide.SetOpen(false, !instant);
            if (instant) HideSettled();
        }

        /// <summary>Hides the prop this frame, unbinds the surface and stops pointer routing.</summary>
        public override void Conceal() {
            _pendingEye = null;
            _pointerEye = null;
            if (raycaster != null) raycaster.ClearSource();

            base.Conceal();
            if (_collider != null) _collider.enabled = false;
            StopSurface();
        }

        /// <summary>Destroys the instance and unbinds the surface, e.g. when the holder is going away.</summary>
        public override void ReleaseInstance() {
            _pendingEye = null;
            _pointerEye = null;
            if (raycaster != null) raycaster.ClearSource();

            StopSurface();
            base.ReleaseInstance();

            _slide = null;
            _renderer = null;
            _collider = null;
        }

        protected override void Update() {
            if (IsPresented && Instance == null) {
                ReleaseInstance();
                return;
            }

            if (IsPresented || Instance == null || _slide == null) return;
            if (!Instance.gameObject.activeSelf || _slide.Fraction > 0f) return;

            HideSettled();
        }

        /// <summary>Re-poses a freshly presented prop against the eye pose the rigs wrote this frame.</summary>
        protected override void LateUpdate() {
            base.LateUpdate();
            if (_pendingEye == null) return;

            Camera eye = _pendingEye;
            _pendingEye = null;
            if (!IsPresented || Instance == null || _collider == null) return;

            PoseInFrontOf(eye);
        }

        /// <remarks>
        /// The collider may sit on a skinned renderer's object, so the renderer is read from the
        /// collider's own object first.
        /// </remarks>
        protected override bool OnInstanceCreated(GameObject spawned) {
            _slide = spawned.GetComponentInChildren<SlideMotion>(true);
            _collider = spawned.GetComponentInChildren<MeshCollider>(true);
            _renderer = _collider != null ? _collider.GetComponent<Renderer>() : null;
            if (_renderer == null) _renderer = spawned.GetComponentInChildren<Renderer>(true);

            if (_collider != null && _renderer != null) return true;

            Debug.LogError($"{nameof(HeldSurfacePresenter)} on '{name}': held prefab '{HeldPrefab.name}' needs a MeshCollider and a Renderer.", this);
            return false;
        }

        private void SnapSlideOpen() {
            if (_slide == null) return;

            if (!_slide.IsInitialised) _slide.Initialise();
            _slide.Snap(true);
        }

        private void PoseInFrontOf(Camera eye) {
            Vector3 meshSize = _collider.sharedMesh != null ? _collider.sharedMesh.bounds.size : Vector3.zero;
            Vector3 scaledSize = Vector3.Scale(meshSize, _collider.transform.lossyScale);
            Vector2 surfaceSize = new Vector2(Mathf.Abs(scaledSize.x), Mathf.Abs(scaledSize.y));

            float distance = CameraFacingPose.FitDistance(eye, surfaceSize, preferredDistance, maxViewFraction);
            WarnIfBeyondPointerReach(distance);
            Pose pose = CameraFacingPose.InFrontOf(eye.transform, distance);
            Instance.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void BindSurface(Camera eye) {
            _pointerEye = eye;
            BindSurfaceRendering();
            if (raycaster != null) raycaster.SetSource(eye, _collider);
        }

        private void BindSurfaceRendering() {
            _collider.enabled = true;
            if (surface == null) return;

            surface.Bind(_renderer);
            surface.SetRendering(true);
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
        private void HideSettled() {
            Instance.gameObject.SetActive(false);
            if (_collider != null) _collider.enabled = false;
            if (surface != null) surface.SetRendering(false);
        }
    }
}
