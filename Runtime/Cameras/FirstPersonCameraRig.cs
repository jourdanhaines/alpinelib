using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Eye-level rig for first-person framing. The rig object itself is the pivot: it sits at the
    /// target's eye offset and carries the yaw/pitch aim, while a child anchor republishes that pose for
    /// whatever camera is looking through it.
    /// </summary>
    /// <remarks>
    /// Deliberately the plainest rig in the module — no follow damping and no collision probe. Damping
    /// is wrong here because the camera is the player's head: lagging it behind the actor turns every
    /// step into a swim, and there is no arm between pivot and camera for geometry to intrude into, so
    /// there is nothing for a sphere cast to solve. The pose is therefore recomputed outright each
    /// <c>LateUpdate</c>, which also means retargeting needs no snap flag the way
    /// <see cref="ThirdPersonCameraRig"/> does.
    ///
    /// The eye also rides the target's <see cref="CharacterController"/> capsule rather than a fixed
    /// height, so an actor that crouches takes the camera down with it. Without that the capsule shrinks
    /// under a low ceiling while the camera stays at standing height, and the player crawls through a
    /// crouch tunnel looking out through the roof.
    ///
    /// Like the third-person rig it reads no input device of its own — look deltas arrive through
    /// <see cref="AddLookInput"/> and accumulate until the next <c>LateUpdate</c> consumes them, and
    /// <see cref="PlanarForward"/>/<see cref="PlanarRight"/> stay yaw-only so movement never tilts into
    /// or out of the ground with the aim.
    /// </remarks>
    public class FirstPersonCameraRig : MonoBehaviour, ICameraRig {
        [Header("Target")]
        [Tooltip("Child transform the camera pose is published on. Falls back to the first child, then to a created anchor.")]
        [SerializeField] private Transform cameraAnchor;

        [Tooltip("Eye position relative to the target, in the target's local space. Y is eye height while standing.")]
        [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Drop the eye with the target's CharacterController capsule, so crouching lowers the camera instead of leaving it inside the ceiling.")]
        [SerializeField] private bool trackCapsuleHeight = true;

        [Header("Look")]
        [Tooltip("Lowest pitch in degrees. Negative looks up.")]
        [SerializeField] private float pitchMin = -85f;

        [Tooltip("Highest pitch in degrees. Positive looks down.")]
        [SerializeField] private float pitchMax = 85f;

        [Tooltip("Multiplier applied to the degrees handed to AddLookInput.")]
        [SerializeField] private float lookSensitivity = 1f;

        /// <inheritdoc />
        public Transform CameraAnchor => cameraAnchor;

        /// <inheritdoc />
        public float Yaw => _yaw;

        /// <inheritdoc />
        public float Pitch => _pitch;

        /// <inheritdoc />
        public Vector3 PlanarForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        /// <inheritdoc />
        public Vector3 PlanarRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        /// <summary>
        /// Target the eye rides on, or null while the rig is parked.
        /// </summary>
        public Transform Target => _target;

        private Transform _target;
        private CharacterController _targetCapsule;
        private float _eyeInset;
        private Vector2 _pendingLook;
        private float _yaw;
        private float _pitch;

        private void Awake() {
            ResolveCameraAnchor();

            Vector3 startingAngles = transform.rotation.eulerAngles;
            _yaw = startingAngles.y;
            _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, startingAngles.x), pitchMin, pitchMax);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The eye is placed immediately rather than on the next frame so a caller that spawns an actor
        /// and hands it to the rig does not get one frame of camera parked at the origin.
        /// </remarks>
        public void SetTarget(Transform target) {
            _target = target;
            ResolveTargetCapsule();
            SnapToTarget();
        }

        /// <summary>
        /// Places the eye and anchor on the target this frame. Does nothing without a target.
        /// </summary>
        public void SnapToTarget() {
            if (_target == null) return;

            ApplyEyePose();
            PositionAnchor();
        }

        /// <inheritdoc />
        public void AddLookInput(Vector2 degreesDelta) {
            _pendingLook += degreesDelta;
        }

        /// <inheritdoc />
        public void SetLookAngles(float yawDegrees, float pitchDegrees) {
            _pendingLook = Vector2.zero;
            _yaw = Mathf.Repeat(yawDegrees, 360f);
            _pitch = Mathf.Clamp(pitchDegrees, pitchMin, pitchMax);
        }

        /// <remarks>
        /// Look input is consumed even without a target so that queued deltas never pile up into a lurch
        /// on the frame a target finally arrives, matching <see cref="ThirdPersonCameraRig"/>.
        /// </remarks>
        private void LateUpdate() {
            ConsumeLookInput();

            if (_target == null) return;

            ApplyEyePose();
            PositionAnchor();
        }

        private void ConsumeLookInput() {
            _yaw = Mathf.Repeat(_yaw + _pendingLook.x * lookSensitivity, 360f);
            _pitch = Mathf.Clamp(_pitch - _pendingLook.y * lookSensitivity, pitchMin, pitchMax);
            _pendingLook = Vector2.zero;
        }

        /// <remarks>
        /// The offset is rotated by the target's facing rather than applied in world space, so an
        /// off-centre eye — one shifted sideways or forward onto a muzzle or a beak — rides the actor
        /// instead of drifting around it as the actor turns. The default offset is purely vertical, for
        /// which the two are identical.
        /// </remarks>
        private void ApplyEyePose() {
            Vector3 offset = eyeOffset;
            offset.y = ResolveEyeHeight();

            transform.SetPositionAndRotation(
                _target.position + _target.rotation * offset,
                Quaternion.Euler(_pitch, _yaw, 0f)
            );
        }

        /// <summary>
        /// Eye height above the target's feet for this frame: the authored height while standing, and
        /// the same distance below the crown of the capsule once something resizes it.
        /// </summary>
        /// <remarks>
        /// Read fresh every frame rather than cached, because whatever shrinks the capsule — a crouch
        /// system easing <c>height</c> down over several frames, say — is then followed exactly, with no
        /// smoothing of its own to fight the one already driving the capsule.
        /// </remarks>
        private float ResolveEyeHeight() {
            if (_targetCapsule == null) return eyeOffset.y;

            return Mathf.Max(_targetCapsule.height - _eyeInset, 0f);
        }

        /// <summary>
        /// Caches the target's capsule, and how far below the top of it the authored eye sits.
        /// </summary>
        /// <remarks>
        /// The inset is measured once, against the height the capsule has when it is first targeted —
        /// its standing height, since actors are targeted upright — rather than re-derived per frame, so
        /// the eye keeps the head position the offset was authored for instead of being redefined by
        /// every resize. The eye then drops exactly as far as the crown does, which is what makes
        /// crouching in first person duck the camera under a low ceiling instead of leaving it hanging
        /// inside the roof while the capsule fits underneath.
        ///
        /// Searched up the hierarchy rather than on the target alone so a rig aimed at a dedicated head
        /// or eye transform still finds the actor's capsule. Targets with no capsule — a spectator
        /// point, a cutscene dolly — simply keep the authored eye height.
        /// </remarks>
        private void ResolveTargetCapsule() {
            _targetCapsule = null;

            if (!trackCapsuleHeight) return;
            if (_target == null) return;

            _targetCapsule = _target.GetComponentInParent<CharacterController>();
            if (_targetCapsule == null) return;

            _eyeInset = Mathf.Max(_targetCapsule.height - eyeOffset.y, 0f);
        }

        private void PositionAnchor() {
            if (cameraAnchor == null) return;

            cameraAnchor.SetPositionAndRotation(transform.position, transform.rotation);
        }

        private void ResolveCameraAnchor() {
            if (cameraAnchor != null) return;

            if (transform.childCount > 0) {
                cameraAnchor = transform.GetChild(0);
                return;
            }

            var anchor = new GameObject("Camera Anchor");
            anchor.transform.SetParent(transform, false);
            cameraAnchor = anchor.transform;
        }
    }
}
