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
    /// The eye also rides the target's capsule height, read through <see cref="ICameraTarget"/>, rather
    /// than a fixed height, so an actor that crouches takes the camera down with it. Without that the
    /// capsule shrinks under a low ceiling while the camera stays at standing height, and the player
    /// crawls through a crouch tunnel looking out through the roof.
    ///
    /// Yaw is kept relative to the target's <see cref="ICameraTarget.YawFrame"/> — the deck it stands
    /// on — and resolved against that frame's heading whenever it is read. A target carried round a
    /// curve therefore turns the view with the deck on the very frame the deck turns, before any brain
    /// reads <see cref="PlanarForward"/>, with nothing feeding deltas in from outside. Switching frames
    /// preserves the world yaw, so nothing moves on screen when a rider boards or alights.
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

        [Tooltip("Drop the eye with the target's capsule height, so crouching lowers the camera instead of leaving it inside the ceiling.")]
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
        public float Yaw {
            get {
                ResolveYawFrame();
                return Mathf.Repeat(FrameHeading() + _frameYaw, 360f);
            }
        }

        /// <inheritdoc />
        public float Pitch => _pitch;

        /// <inheritdoc />
        public Vector3 PlanarForward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;

        /// <inheritdoc />
        public Vector3 PlanarRight => Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;

        /// <summary>
        /// Target the eye rides on, or null while the rig is parked.
        /// </summary>
        public Transform Target => _target;

        private Transform _target;
        private ICameraTarget _cameraTarget;
        private Transform _yawFrame;
        private float _frameYaw;
        private float _eyeInset;
        private Vector2 _pendingLook;
        private float _pitch;

        private void Awake() {
            ResolveCameraAnchor();

            Vector3 startingAngles = transform.rotation.eulerAngles;
            _frameYaw = startingAngles.y;
            _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, startingAngles.x), pitchMin, pitchMax);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The eye is placed immediately rather than on the next frame so a caller that spawns an actor
        /// and hands it to the rig does not get one frame of camera parked at the origin.
        /// </remarks>
        public void SetTarget(Transform target) {
            _target = target;
            ResolveCameraTarget();
            ResolveYawFrame();
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
            ResolveYawFrame();
            _pendingLook = Vector2.zero;
            _frameYaw = Mathf.Repeat(yawDegrees, 360f) - FrameHeading();
            _pitch = Mathf.Clamp(pitchDegrees, pitchMin, pitchMax);
        }

        /// <remarks>
        /// Look input is consumed even without a target so that queued deltas never pile up into a lurch
        /// on the frame a target finally arrives, matching <see cref="ThirdPersonCameraRig"/>.
        /// </remarks>
        private void LateUpdate() {
            ResolveYawFrame();
            ConsumeLookInput();

            if (_target == null) return;

            ApplyEyePose();
            PositionAnchor();
        }

        private void ConsumeLookInput() {
            _frameYaw += _pendingLook.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - _pendingLook.y * lookSensitivity, pitchMin, pitchMax);
            _pendingLook = Vector2.zero;
        }

        /// <summary>
        /// Adopts the target's current yaw frame, carrying the world yaw across so a change of frame
        /// moves nothing.
        /// </summary>
        private void ResolveYawFrame() {
            Transform frame = _cameraTarget != null ? _cameraTarget.YawFrame : null;
            if (ReferenceEquals(frame, _yawFrame)) return;

            float worldYaw = FrameHeading() + _frameYaw;
            _yawFrame = frame;
            _frameYaw = worldYaw - FrameHeading();
        }

        private float FrameHeading() {
            return _yawFrame != null ? _yawFrame.eulerAngles.y : 0f;
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
                Quaternion.Euler(_pitch, Yaw, 0f)
            );
        }

        /// <summary>
        /// Eye height above the target's feet for this frame: the authored height while standing, and
        /// the same distance below the crown of the capsule once something resizes it.
        /// </summary>
        private float ResolveEyeHeight() {
            if (_cameraTarget == null || !trackCapsuleHeight) return eyeOffset.y;

            return Mathf.Max(_cameraTarget.Height - _eyeInset, 0f);
        }

        /// <summary>
        /// Finds what the target knows about itself beyond its transform, and how far below the crown of
        /// its capsule the authored eye sits.
        /// </summary>
        /// <remarks>
        /// The inset is measured once, against the height the target has when first targeted — its
        /// standing height — so the eye keeps the head position the offset was authored for and drops
        /// exactly as far as the crown does. Searched up the hierarchy so a rig aimed at a dedicated head
        /// transform still finds the actor. Targets with nothing to say keep the authored eye height and
        /// the world yaw frame.
        /// </remarks>
        private void ResolveCameraTarget() {
            _cameraTarget = _target != null ? _target.GetComponentInParent<ICameraTarget>() : null;
            if (_cameraTarget == null) return;

            _eyeInset = Mathf.Max(_cameraTarget.Height - eyeOffset.y, 0f);
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
