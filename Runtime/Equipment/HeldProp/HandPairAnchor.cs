using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Poses a held prop between two humanoid hand bones by matching grip frames: a pinch frame on each
    /// hand (hand-bone local) meets a grip frame on the prop (prop-root local).
    /// </summary>
    /// <remarks>
    /// Frame convention (see <see cref="IHeldPropAnchor"/>): prop +X left grip → right grip, +Y up,
    /// readable face toward −Z (the holder). The fit is hybrid: the across axis runs between the two
    /// pinch points, so neither hand's rotation can swing the prop sideways out of the other, and the
    /// facing is the halfway rotation the two pinches imply for the prop, with its across part removed.
    /// The prop is then centred between the pinches, any spacing mismatch split evenly.
    ///
    /// Prop grip positions are in the prop root's local, unscaled units; the root's world scale is
    /// applied when posing. Resolved after the pose layers, so whatever bends the arms this frame
    /// carries the prop with it. Allocation free per frame.
    ///
    /// The older point-grip API (hand grip points plus palm vectors or a facing override) still works
    /// and takes over while any of its inputs is set; it is obsolete.
    /// </remarks>
    public class HandPairAnchor : MonoBehaviour, IHeldPropAnchor {
        private const float MinimumLengthSquared = 1e-8f;
        private const float AnimatorRetrySeconds = 1f;

        [Tooltip("Humanoid animator the hands are read from; found in children when left empty.")]
        [SerializeField] private Animator animator;

        [SerializeField] private HumanBodyBones leftHandBone = HumanBodyBones.LeftHand;
        [SerializeField] private HumanBodyBones rightHandBone = HumanBodyBones.RightHand;

        [Tooltip("Pinch frame in the left hand bone's local space.")]
        [SerializeField] private Pose leftPinch = Pose.identity;

        [Tooltip("Pinch frame in the right hand bone's local space.")]
        [SerializeField] private Pose rightPinch = Pose.identity;

        [Tooltip("Frame the left hand pinches, in the prop root's local space (unscaled position).")]
        [SerializeField] private Pose propGripLeft = new Pose(new Vector3(-0.2f, 0f, 0f), Quaternion.identity);

        [Tooltip("Frame the right hand pinches, in the prop root's local space (unscaled position).")]
        [SerializeField] private Pose propGripRight = new Pose(new Vector3(0.2f, 0f, 0f), Quaternion.identity);

        [Tooltip("Obsolete point grip in the left hand bone's local space; non-zero selects the legacy fit.")]
        [SerializeField] private Vector3 leftGrip;

        [Tooltip("Obsolete point grip in the right hand bone's local space; non-zero selects the legacy fit.")]
        [SerializeField] private Vector3 rightGrip;

        [Tooltip("Obsolete left-hand-local direction the prop's +Z follows in the legacy fit.")]
        [SerializeField] private Vector3 leftPalm;

        [Tooltip("Obsolete right-hand-local direction the prop's +Z follows in the legacy fit.")]
        [SerializeField] private Vector3 rightPalm;

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

        /// <summary>Sets the prop's grip frames (root-local, unscaled position), e.g. from a folding rig each frame.</summary>
        public void SetPropGrips(Pose left, Pose right) {
            propGripLeft = left;
            propGripRight = right;
            _gripFrameDirty = true;
        }

        /// <summary>Sets the hand-local pinch frames.</summary>
        public void SetHandPinches(Pose left, Pose right) {
            leftPinch = left;
            rightPinch = right;
        }

        /// <summary>Sets the prop-local grip points (root-local, unscaled) with no grip rotation.</summary>
        [System.Obsolete("Point grips are replaced by grip frames: use SetPropGrips(Pose, Pose). Removed with the legacy fit.")]
        public void SetPropGrips(Vector3 left, Vector3 right) {
            SetPropGrips(new Pose(left, Quaternion.identity), new Pose(right, Quaternion.identity));
        }

        /// <summary>Sets the hand-local grip points; non-zero points select the legacy fit.</summary>
        [System.Obsolete("Use SetHandPinches. Removed with the legacy fit.")]
        public void SetHandGrips(Vector3 left, Vector3 right) {
            leftGrip = left;
            rightGrip = right;
        }

        /// <summary>Sets the hand-local palm vectors the legacy fit faces the prop by.</summary>
        [System.Obsolete("Facing comes from the pinch frames. Removed with the legacy fit.")]
        public void SetPalms(Vector3 left, Vector3 right) {
            leftPalm = left;
            rightPalm = right;
        }

        /// <summary>
        /// Legacy fit: measures both palm vectors from the current pose so the prop's +Z follows
        /// <paramref name="worldFacing"/> in it. Call with the hold pose applied at level look.
        /// </summary>
        [System.Obsolete("Facing comes from the pinch frames. Removed with the legacy fit.")]
        public bool CalibrateFacing(Vector3 worldFacing) {
            if (!ResolveHands() || worldFacing.sqrMagnitude < MinimumLengthSquared) return false;

            leftPalm = _leftHand.InverseTransformDirection(worldFacing.normalized);
            rightPalm = _rightHand.InverseTransformDirection(worldFacing.normalized);
            return true;
        }

        /// <summary>
        /// Legacy fit: takes the prop's +Z (away from the holder) from <paramref name="worldFacing"/> instead of the palm
        /// vectors until <see cref="ClearFacingOverride"/>, e.g. a facing track authored with the hands'
        /// animation. Position and the across axis still come from the hands.
        /// </summary>
        [System.Obsolete("Facing comes from the pinch frames. Removed with the legacy fit.")]
        public void SetFacingOverride(Vector3 worldFacing) {
            _facingOverride = worldFacing;
            _hasFacingOverride = worldFacing.sqrMagnitude >= MinimumLengthSquared && IsFinite(worldFacing);
        }

        /// <summary>Returns the legacy facing to the palm vectors.</summary>
        [System.Obsolete("Facing comes from the pinch frames. Removed with the legacy fit.")]
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

            Vector3 propScale = prop != null ? prop.lossyScale : Vector3.one;
            return UsesLegacyFit() ? TryResolveLegacy(propScale, out pose) : TryResolveFrames(propScale, out pose);
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

        /// <summary>
        /// Grip-frame fit: across from the pinch points, facing from the halfway rotation the pinches
        /// imply for the prop, centred between the pinches.
        /// </summary>
        private bool TryResolveFrames(Vector3 propScale, out Pose pose) {
            pose = default;
            Vector3 pinchLeft = _leftHand.TransformPoint(leftPinch.position);
            Vector3 pinchRight = _rightHand.TransformPoint(rightPinch.position);
            Vector3 gripLeft = Vector3.Scale(propGripLeft.position, propScale);
            Vector3 gripRight = Vector3.Scale(propGripRight.position, propScale);
            Vector3 propFacing = PropFacing(gripRight - gripLeft);
            if (!TryBuildFrame(gripRight - gripLeft, propFacing, out Quaternion propFrame)) return false;

            // The same prop-local facing carried to the world by each pinch, so a consistent pair
            // reproduces the prop rotation the pinches imply exactly.
            Quaternion impliedLeft = _leftHand.rotation * leftPinch.rotation * Quaternion.Inverse(propGripLeft.rotation);
            Quaternion impliedRight = _rightHand.rotation * rightPinch.rotation * Quaternion.Inverse(propGripRight.rotation);
            Vector3 facing = Quaternion.Slerp(impliedLeft, impliedRight, 0.5f) * propFacing;
            if (!TryBuildFrame(pinchRight - pinchLeft, facing, out Quaternion handFrame)) return false;

            Quaternion rotation = Smooth(handFrame * Quaternion.Inverse(propFrame));
            pose = new Pose((pinchLeft + pinchRight) * 0.5f - rotation * ((gripLeft + gripRight) * 0.5f), rotation);
            return true;
        }

        /// <summary>The prop's +Z, or a level direction across it when the grips run along +Z.</summary>
        private static Vector3 PropFacing(Vector3 across) {
            return Mathf.Abs(across.normalized.z) > 0.99f ? Vector3.Cross(across, Vector3.up) : Vector3.forward;
        }

        /// <summary>The obsolete point-grip fit, used while any of its inputs is set.</summary>
        private bool UsesLegacyFit() {
            return _hasFacingOverride || leftGrip != Vector3.zero || rightGrip != Vector3.zero || leftPalm != Vector3.zero || rightPalm != Vector3.zero;
        }

        private bool TryResolveLegacy(Vector3 propScale, out Pose pose) {
            pose = default;
            Vector3 gripLeft = _leftHand.TransformPoint(leftGrip);
            Vector3 gripRight = _rightHand.TransformPoint(rightGrip);
            if (!TryBuildFrame(gripRight - gripLeft, ResolveFacing(), out Quaternion handFrame)) return false;

            Quaternion rotation = Smooth(handFrame * GripFrameInverse());
            Vector3 propMidpoint = Vector3.Scale((propGripLeft.position + propGripRight.position) * 0.5f, propScale);
            pose = new Pose((gripLeft + gripRight) * 0.5f - rotation * propMidpoint, rotation);
            return true;
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
            Vector3 across = propGripRight.position - propGripLeft.position;
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
