using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Poses a held prop between two humanoid hand bones: the prop's grip points land on the hands'
    /// grip points, its +X runs from the left grip to the right, and it faces the way the palms say.
    /// </summary>
    /// <remarks>
    /// Frame convention (see <see cref="IHeldPropAnchor"/>): prop +X left grip → right grip, +Y up,
    /// readable face toward −Z (the holder). The facing comes from the two palm vectors — hand-local
    /// directions that, in the hold pose, point where the prop's +Z should (away from the holder). Left
    /// at zero, the facing falls back to this component's forward, which keeps the prop upright but
    /// does not tilt it with the arms; <see cref="CalibrateFacing"/> measures the vectors from a pose.
    /// <see cref="SetFacingOverride"/> replaces the palms with a facing the caller tracks, for hands whose
    /// palms do not face the way the prop does.
    ///
    /// Prop grip points are in the prop root's local, unscaled units (measure them with any slide
    /// snapped open); the root's world scale is applied when posing. Resolved after the pose layers,
    /// so whatever bends the arms this frame carries the prop with it. Allocation free per frame.
    /// </remarks>
    public class HandPairAnchor : MonoBehaviour, IHeldPropAnchor {
        private const float MinimumLengthSquared = 1e-8f;
        private const float AnimatorRetrySeconds = 1f;

        [Tooltip("Humanoid animator the hands are read from; found in children when left empty.")]
        [SerializeField] private Animator animator;

        [SerializeField] private HumanBodyBones leftHandBone = HumanBodyBones.LeftHand;
        [SerializeField] private HumanBodyBones rightHandBone = HumanBodyBones.RightHand;

        [Tooltip("Grip point in the left hand bone's local space.")]
        [SerializeField] private Vector3 leftGrip;

        [Tooltip("Grip point in the right hand bone's local space.")]
        [SerializeField] private Vector3 rightGrip;

        [Tooltip("Left-hand-local direction the prop's +Z (away from the holder) follows. Zero on both hands uses this transform's forward.")]
        [SerializeField] private Vector3 leftPalm;

        [Tooltip("Right-hand-local direction the prop's +Z (away from the holder) follows. Zero on both hands uses this transform's forward.")]
        [SerializeField] private Vector3 rightPalm;

        [Tooltip("Point the left hand grips, in the prop root's local unscaled units.")]
        [SerializeField] private Vector3 propGripLeft = new Vector3(-0.2f, 0f, 0f);

        [Tooltip("Point the right hand grips, in the prop root's local unscaled units.")]
        [SerializeField] private Vector3 propGripRight = new Vector3(0.2f, 0f, 0f);

        [Tooltip("Time constant in seconds smoothing the prop's rotation in this anchor's frame; 0 follows the hands exactly.")]
        [SerializeField] private float rotationSmoothing;

        private Transform _leftHand;
        private Transform _rightHand;
        private float _nextAnimatorSearch = float.NegativeInfinity;
        private Quaternion _gripFrameInverse = Quaternion.identity;
        private bool _gripFrameDirty = true;
        private Quaternion _smoothedLocal = Quaternion.identity;
        private Quaternion _frameStartLocal = Quaternion.identity;
        private int _smoothedFrame = int.MinValue;
        private Vector3 _facingOverride;
        private bool _hasFacingOverride;

        /// <inheritdoc />
        public bool IsContinuous => true;

        /// <summary>The left hand bone, or null while no humanoid animator is available.</summary>
        public Transform LeftHand => ResolveHands() ? _leftHand : null;

        /// <summary>The right hand bone, or null while no humanoid animator is available.</summary>
        public Transform RightHand => ResolveHands() ? _rightHand : null;

        /// <summary>Points the anchor at <paramref name="target"/>'s hands, e.g. after the body is rebuilt.</summary>
        public void SetAnimator(Animator target) {
            animator = target;
            _leftHand = null;
            _rightHand = null;
            _nextAnimatorSearch = float.NegativeInfinity;
        }

        /// <summary>Sets the prop-local grip points (root-local, unscaled), e.g. from sockets on a fresh prop.</summary>
        public void SetPropGrips(Vector3 left, Vector3 right) {
            propGripLeft = left;
            propGripRight = right;
            _gripFrameDirty = true;
        }

        /// <summary>Sets the hand-local grip points.</summary>
        public void SetHandGrips(Vector3 left, Vector3 right) {
            leftGrip = left;
            rightGrip = right;
        }

        /// <summary>Sets the hand-local palm vectors; see the remarks for what they mean.</summary>
        public void SetPalms(Vector3 left, Vector3 right) {
            leftPalm = left;
            rightPalm = right;
        }

        /// <summary>
        /// Measures both palm vectors from the current pose so the prop's +Z follows
        /// <paramref name="worldFacing"/> in it. Call with the hold pose applied at level look.
        /// </summary>
        public bool CalibrateFacing(Vector3 worldFacing) {
            if (!ResolveHands() || worldFacing.sqrMagnitude < MinimumLengthSquared) return false;

            leftPalm = _leftHand.InverseTransformDirection(worldFacing.normalized);
            rightPalm = _rightHand.InverseTransformDirection(worldFacing.normalized);
            return true;
        }

        /// <summary>
        /// Takes the prop's +Z (away from the holder) from <paramref name="worldFacing"/> instead of the palm
        /// vectors until <see cref="ClearFacingOverride"/>, e.g. a facing track authored with the hands'
        /// animation. Position and the across axis still come from the hands.
        /// </summary>
        public void SetFacingOverride(Vector3 worldFacing) {
            _facingOverride = worldFacing;
            _hasFacingOverride = worldFacing.sqrMagnitude >= MinimumLengthSquared && IsFinite(worldFacing);
        }

        /// <summary>Returns the facing to the palm vectors.</summary>
        public void ClearFacingOverride() {
            _hasFacingOverride = false;
        }

        /// <summary>Drops the smoothed rotation, so the next resolve snaps to the hands.</summary>
        public void ResetSmoothing() {
            _smoothedFrame = int.MinValue;
        }

        /// <inheritdoc />
        public bool TryResolvePose(Transform prop, out Pose pose) {
            pose = default;
            if (!ResolveHands()) return false;

            Vector3 gripLeft = _leftHand.TransformPoint(leftGrip);
            Vector3 gripRight = _rightHand.TransformPoint(rightGrip);
            if (!TryBuildFrame(gripRight - gripLeft, ResolveFacing(), out Quaternion handFrame)) return false;

            Quaternion rotation = Smooth(handFrame * GripFrameInverse());
            Vector3 propScale = prop != null ? prop.lossyScale : Vector3.one;
            Vector3 propMidpoint = Vector3.Scale((propGripLeft + propGripRight) * 0.5f, propScale);
            pose = new Pose((gripLeft + gripRight) * 0.5f - rotation * propMidpoint, rotation);
            return true;
        }

        /// <summary>
        /// Rotation whose +X is <paramref name="across"/> and whose +Z is <paramref name="facing"/> with
        /// its component along <paramref name="across"/> removed. False when either is degenerate or not finite.
        /// </summary>
        public static bool TryBuildFrame(Vector3 across, Vector3 facing, out Quaternion frame) {
            frame = Quaternion.identity;
            if (!IsFinite(across) || !IsFinite(facing)) return false;
            if (across.sqrMagnitude < MinimumLengthSquared) return false;

            Vector3 right = across.normalized;
            Vector3 forward = facing - right * Vector3.Dot(facing, right);
            if (forward.sqrMagnitude < MinimumLengthSquared) return false;

            forward.Normalize();
            frame = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
            return true;
        }

        private static bool IsFinite(Vector3 vector) {
            return float.IsFinite(vector.x) && float.IsFinite(vector.y) && float.IsFinite(vector.z);
        }

        private void OnValidate() {
            _gripFrameDirty = true;
        }

        private Vector3 ResolveFacing() {
            if (_hasFacingOverride) return _facingOverride;
            if (leftPalm == Vector3.zero && rightPalm == Vector3.zero) return transform.forward;

            Vector3 facing = _leftHand.TransformDirection(leftPalm) + _rightHand.TransformDirection(rightPalm);
            return facing.sqrMagnitude >= MinimumLengthSquared ? facing : transform.forward;
        }

        /// <summary>Inverse of the prop-local frame the grips define, so that frame lands on the hands' frame.</summary>
        private Quaternion GripFrameInverse() {
            if (!_gripFrameDirty) return _gripFrameInverse;

            _gripFrameDirty = false;
            Vector3 across = propGripRight - propGripLeft;
            Vector3 facing = Mathf.Abs(across.normalized.z) > 0.99f ? Vector3.Cross(across, Vector3.up) : Vector3.forward;
            _gripFrameInverse = TryBuildFrame(across, facing, out Quaternion gripFrame) ? Quaternion.Inverse(gripFrame) : Quaternion.identity;
            return _gripFrameInverse;
        }

        /// <summary>
        /// Exponential smoothing held in this anchor's frame, so a turning carrier does not drag the
        /// prop. A second resolve in one frame re-smooths from the frame's starting value, so it sees
        /// this frame's hands without advancing twice.
        /// </summary>
        private Quaternion Smooth(Quaternion target) {
            if (rotationSmoothing <= 0f) return target;

            Quaternion local = Quaternion.Inverse(transform.rotation) * target;
            int frame = Time.frameCount;
            if (_smoothedFrame == int.MinValue || frame - _smoothedFrame > 1) {
                _frameStartLocal = local;
                _smoothedLocal = local;
            } else {
                if (frame != _smoothedFrame) _frameStartLocal = _smoothedLocal;
                _smoothedLocal = Quaternion.Slerp(_frameStartLocal, local, 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothing));
            }

            _smoothedFrame = frame;
            return transform.rotation * _smoothedLocal;
        }

        private bool ResolveHands() {
            if (_leftHand != null && _rightHand != null) return true;
            if (!ResolveAnimator()) return false;

            _leftHand = animator.GetBoneTransform(leftHandBone);
            _rightHand = animator.GetBoneTransform(rightHandBone);
            return _leftHand != null && _rightHand != null;
        }

        /// <summary>Finds the animator in children, searching at most once a second while there is none.</summary>
        private bool ResolveAnimator() {
            if (animator == null) {
                if (Time.unscaledTime < _nextAnimatorSearch) return false;

                _nextAnimatorSearch = Time.unscaledTime + AnimatorRetrySeconds;
                animator = GetComponentInChildren<Animator>();
            }

            return animator != null && animator.isInitialized && animator.isHuman;
        }
    }
}
