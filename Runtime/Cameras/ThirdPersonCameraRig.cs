using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Orbiting spring arm for third-person framing. The rig object itself is the pivot: it chases the
    /// target with damped follow and carries the yaw/pitch orbit, while a child anchor holds the actual
    /// camera at a shoulder offset behind that pivot. A sphere cast pulls the anchor in whenever
    /// geometry sits between the pivot and where the camera wants to be.
    /// </summary>
    /// <remarks>
    /// The rig reads no input device of its own — the game feeds it look deltas through
    /// <see cref="AddLookInput"/>, which accumulate until the next <c>LateUpdate</c> consumes them. That
    /// keeps the rig usable from any input stack. <see cref="PlanarForward"/> and
    /// <see cref="PlanarRight"/> are yaw-only, so movement stays camera-relative without ever tilting
    /// into or out of the ground.
    /// </remarks>
    public class ThirdPersonCameraRig : MonoBehaviour, ICameraRig {
        [Header("Target")]
        [Tooltip("Child transform the camera lives on. Falls back to the first child, then to a created anchor.")]
        [SerializeField] private Transform cameraAnchor;

        [Header("Follow")]
        [Tooltip("Approach rate of the pivot towards the target, in units of e-folds per second. Higher is tighter; 0 pins the pivot to the target.")]
        [SerializeField] private float followDamping = 12f;

        [Header("Orbit")]
        [Tooltip("Distance from the pivot back to the camera before collision pull-in.")]
        [SerializeField] private float distance = 4f;

        [Tooltip("Offset applied in pivot-local space. Positive X frames the target off-centre over the shoulder.")]
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.5f, 1.6f, 0f);

        [Tooltip("Lowest pitch in degrees. Negative looks up.")]
        [SerializeField] private float pitchMin = -35f;

        [Tooltip("Highest pitch in degrees. Positive looks down.")]
        [SerializeField] private float pitchMax = 65f;

        [Tooltip("Multiplier applied to the degrees handed to AddLookInput.")]
        [SerializeField] private float lookSensitivity = 1f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask;

        [Tooltip("Radius of the sphere cast that probes for geometry between the pivot and the camera.")]
        [SerializeField] private float collisionRadius = 0.25f;

        /// <inheritdoc />
        public Transform CameraAnchor => cameraAnchor;

        /// <summary>
        /// Transform the camera is mounted on. Positioned every <c>LateUpdate</c>; never parent anything
        /// to it that should stay put.
        /// </summary>
        /// <remarks>
        /// Kept as an alias of <see cref="CameraAnchor"/>, which is the name <see cref="ICameraRig"/>
        /// gives the same transform. Games written against this rig before the interface existed still
        /// read it, and renaming it would break them for no behavioural gain.
        /// </remarks>
        public Transform CameraTransform => CameraAnchor;

        /// <inheritdoc />
        public float Yaw => _yaw;

        /// <inheritdoc />
        public float Pitch => _pitch;

        /// <summary>
        /// Where the camera is looking, flattened onto the ground plane and normalised. Use this to make
        /// movement input camera-relative.
        /// </summary>
        public Vector3 PlanarForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Right of <see cref="PlanarForward"/>, flattened onto the ground plane and normalised.
        /// </summary>
        public Vector3 PlanarRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        /// <summary>
        /// Target the pivot follows, or null while the rig is parked.
        /// </summary>
        public Transform Target => _target;

        private const float MinimumCastDistance = 0.01f;

        private Transform _target;
        private Vector2 _pendingLook;
        private float _yaw;
        private float _pitch;

        private void Awake() {
            ResolveCameraAnchor();

            Vector3 startingAngles = transform.rotation.eulerAngles;
            _yaw = startingAngles.y;
            _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, startingAngles.x), pitchMin, pitchMax);
        }

        /// <summary>
        /// Points the rig at a new target, or at nothing when passed null. Snapping places the pivot on
        /// the target immediately instead of letting it glide in from wherever the rig was.
        /// </summary>
        public void SetTarget(Transform target, bool snap = true) {
            _target = target;

            if (!snap) return;

            SnapToTarget();
        }

        /// <remarks>
        /// Implemented explicitly and routed through the rig's own two-argument
        /// <see cref="SetTarget(Transform, bool)"/>: the snap flag is specific to a damped rig and has no
        /// place on <see cref="ICameraRig"/>, and adding a one-argument overload beside a method whose
        /// second parameter is optional would leave that default unreachable. Interface callers get the
        /// snapping default, and every existing call site keeps the signature it was written against.
        /// </remarks>
        void ICameraRig.SetTarget(Transform target) {
            SetTarget(target);
        }

        /// <summary>
        /// Places the pivot and camera on the target this frame, skipping the follow damping. Does
        /// nothing without a target.
        /// </summary>
        public void SnapToTarget() {
            if (_target == null) return;

            transform.position = _target.position;
            ApplyOrbitRotation();
            PositionCamera();
        }

        /// <summary>
        /// Queues a look delta in degrees — X yaws, positive Y looks up. Deltas accumulate across the
        /// frame and are consumed once in <c>LateUpdate</c>, so callers may add from as many sources as
        /// they like without fighting each other.
        /// </summary>
        public void AddLookInput(Vector2 degreesDelta) {
            _pendingLook += degreesDelta;
        }

        /// <summary>
        /// Overwrites the orbit angles outright, bypassing sensitivity and any queued look input. Pitch
        /// is clamped to the authored range.
        /// </summary>
        public void SetLookAngles(float yawDegrees, float pitchDegrees) {
            _pendingLook = Vector2.zero;
            _yaw = Mathf.Repeat(yawDegrees, 360f);
            _pitch = Mathf.Clamp(pitchDegrees, pitchMin, pitchMax);
        }

        private void LateUpdate() {
            ConsumeLookInput();

            if (_target == null) return;

            FollowTarget();
            ApplyOrbitRotation();
            PositionCamera();
        }

        private void ConsumeLookInput() {
            _yaw = Mathf.Repeat(_yaw + _pendingLook.x * lookSensitivity, 360f);
            _pitch = Mathf.Clamp(_pitch - _pendingLook.y * lookSensitivity, pitchMin, pitchMax);
            _pendingLook = Vector2.zero;
        }

        private void FollowTarget() {
            Vector3 targetPosition = _target.position;

            if (followDamping <= 0f) {
                transform.position = targetPosition;
                return;
            }

            float approach = 1f - Mathf.Exp(-followDamping * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, approach);
        }

        private void ApplyOrbitRotation() {
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void PositionCamera() {
            if (cameraAnchor == null) return;

            Quaternion orbit = transform.rotation;
            Vector3 pivot = transform.position;
            Vector3 desiredPosition = pivot + orbit * (shoulderOffset + Vector3.back * distance);

            cameraAnchor.SetPositionAndRotation(ResolveCollision(pivot, desiredPosition), orbit);
        }

        private Vector3 ResolveCollision(Vector3 pivot, Vector3 desiredPosition) {
            Vector3 travel = desiredPosition - pivot;
            float castDistance = travel.magnitude;

            if (castDistance < MinimumCastDistance) return desiredPosition;

            Vector3 direction = travel / castDistance;
            bool blocked = Physics.SphereCast(
                pivot, collisionRadius, direction, out RaycastHit hit,
                castDistance, collisionMask, QueryTriggerInteraction.Ignore
            );

            if (!blocked) return desiredPosition;

            return pivot + direction * Mathf.Max(hit.distance, 0f);
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
