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
    ///
    /// Grip points are in metres for a prop root of unit world scale. Resolved after the pose layers,
    /// so whatever bends the arms this frame carries the prop with it. Allocation free per frame.
    /// </remarks>
    public class HandPairAnchor : MonoBehaviour, IHeldPropAnchor {
        private const float MinimumLengthSquared = 1e-8f;

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

        [Tooltip("Prop-local point the left hand grips.")]
        [SerializeField] private Vector3 propGripLeft = new Vector3(-0.2f, 0f, 0f);

        [Tooltip("Prop-local point the right hand grips.")]
        [SerializeField] private Vector3 propGripRight = new Vector3(0.2f, 0f, 0f);

        [Tooltip("Time constant in seconds smoothing the prop's rotation relative to this transform; 0 follows the hands exactly.")]
        [SerializeField] private float rotationSmoothing;

        private Transform _leftHand;
        private Transform _rightHand;
        private Quaternion _gripFrameInverse = Quaternion.identity;
        private bool _gripFrameDirty = true;
        private Quaternion _smoothedLocal = Quaternion.identity;
        private int _smoothedFrame = int.MinValue;

        /// <inheritdoc />
        public bool IsContinuous => true;

        public Transform LeftHand => ResolveHands() ? _leftHand : null;

        public Transform RightHand => ResolveHands() ? _rightHand : null;

        /// <summary>Sets the prop-local grip points, e.g. from sockets on a freshly spawned prop.</summary>
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

        /// <summary>Drops the smoothed rotation, so the next resolve snaps to the hands.</summary>
        public void ResetSmoothing() {
            _smoothedFrame = int.MinValue;
        }

        /// <inheritdoc />
        public bool TryResolvePose(out Pose pose) {
            pose = default;
            if (!ResolveHands()) return false;

            Vector3 gripLeft = _leftHand.TransformPoint(leftGrip);
            Vector3 gripRight = _rightHand.TransformPoint(rightGrip);
            if (!TryBuildFrame(gripRight - gripLeft, ResolveFacing(), out Quaternion handFrame)) return false;

            Quaternion rotation = Smooth(handFrame * GripFrameInverse());
            Vector3 propMidpoint = (propGripLeft + propGripRight) * 0.5f;
            pose = new Pose((gripLeft + gripRight) * 0.5f - rotation * propMidpoint, rotation);
            return true;
        }

        /// <summary>
        /// Rotation whose +X is <paramref name="across"/> and whose +Z is <paramref name="facing"/> with
        /// its component along <paramref name="across"/> removed. False when either is degenerate.
        /// </summary>
        public static bool TryBuildFrame(Vector3 across, Vector3 facing, out Quaternion frame) {
            frame = Quaternion.identity;
            if (across.sqrMagnitude < MinimumLengthSquared) return false;

            Vector3 right = across.normalized;
            Vector3 forward = facing - right * Vector3.Dot(facing, right);
            if (forward.sqrMagnitude < MinimumLengthSquared) return false;

            forward.Normalize();
            frame = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
            return true;
        }

        private void OnValidate() {
            _gripFrameDirty = true;
        }

        private Vector3 ResolveFacing() {
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

        /// <summary>Exponential smoothing held in this transform's frame, so a turning carrier does not drag the prop.</summary>
        private Quaternion Smooth(Quaternion target) {
            if (rotationSmoothing <= 0f) return target;

            Quaternion local = Quaternion.Inverse(transform.rotation) * target;
            int frame = Time.frameCount;
            if (_smoothedFrame == int.MinValue || frame - _smoothedFrame > 1) {
                _smoothedLocal = local;
            } else if (frame != _smoothedFrame) {
                _smoothedLocal = Quaternion.Slerp(_smoothedLocal, local, 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothing));
            }

            _smoothedFrame = frame;
            return transform.rotation * _smoothedLocal;
        }

        private bool ResolveHands() {
            if (_leftHand != null && _rightHand != null) return true;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isInitialized || !animator.isHuman) return false;

            _leftHand = animator.GetBoneTransform(leftHandBone);
            _rightHand = animator.GetBoneTransform(rightHandBone);
            return _leftHand != null && _rightHand != null;
        }
    }
}
