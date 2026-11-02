using AlpineLib.Animation.Bones;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Holds a prop at a pose relative to one body bone, so the prop rides that bone through the
    /// animation, e.g. a sheet carried on the chest as baked in a <see cref="HeldPoseTrack"/>.
    /// </summary>
    /// <remarks>
    /// Frame convention as <see cref="IHeldPropAnchor"/>. The pose is in the carrier bone's local space:
    /// position through <see cref="Transform.TransformPoint(Vector3)"/>, so the bone's world scale applies.
    /// A pose set from a track each frame wins over the authored rest pose until it is cleared. The
    /// bone comes from the humanoid avatar or a <see cref="BoneMap"/>.
    /// </remarks>
    public class BonePoseAnchor : MonoBehaviour, IHeldPropAnchor {
        private const float AnimatorRetrySeconds = 1f;

        [Tooltip("Animator the carrier bone is read from (humanoid or bone-mapped); found in children when left empty.")]
        [SerializeField] private Animator animator;

        [SerializeField] private HumanBodyBones carrierBone = HumanBodyBones.Spine;

        [Tooltip("Prop root pose in the carrier bone's local space, used while no pose is set.")]
        [SerializeField] private Pose restLocalPose = Pose.identity;

        private Transform _carrier;
        private Pose _localPose = Pose.identity;
        private bool _hasLocalPose;
        private float _nextAnimatorSearch = float.NegativeInfinity;

        /// <inheritdoc />
        public bool IsContinuous => true;

        /// <summary>The carrier bone, or null while no animator with a skeleton is available.</summary>
        public Transform Carrier => ResolveCarrier() ? _carrier : null;

        /// <summary>Bone the pose is relative to.</summary>
        public HumanBodyBones CarrierBone => carrierBone;

        /// <summary>True while a set pose overrides the rest pose.</summary>
        public bool HasLocalPose => _hasLocalPose;

        /// <summary>The carrier-local pose currently used.</summary>
        public Pose CurrentLocalPose => _hasLocalPose ? _localPose : restLocalPose;

        /// <summary>Points the anchor at <paramref name="target"/>'s skeleton, e.g. after the body is rebuilt.</summary>
        public void SetAnimator(Animator target) {
            animator = target;
            _carrier = null;
            _nextAnimatorSearch = float.NegativeInfinity;
        }

        /// <summary>Changes the carrier bone, e.g. to match a track's.</summary>
        public void SetCarrierBone(HumanBodyBones bone) {
            carrierBone = bone;
            _carrier = null;
        }

        /// <summary>Sets the authored fallback pose.</summary>
        public void SetRestLocalPose(Pose local) {
            restLocalPose = local;
        }

        /// <summary>Overrides the rest pose, e.g. with this frame's sample of a baked track.</summary>
        public void SetLocalPose(Pose local) {
            _localPose = local;
            _hasLocalPose = true;
        }

        /// <summary>Goes back to the rest pose.</summary>
        public void ClearLocalPose() {
            _hasLocalPose = false;
        }

        /// <inheritdoc />
        public bool TryResolvePose(Transform prop, out Pose pose) {
            pose = default;
            if (!ResolveCarrier()) return false;

            Pose local = CurrentLocalPose;
            pose = new Pose(_carrier.TransformPoint(local.position), _carrier.rotation * local.rotation);
            return true;
        }

        private bool ResolveCarrier() {
            if (_carrier != null) return true;
            if (!ResolveAnimator()) return false;

            _carrier = BoneResolver.Find(animator, carrierBone);
            return _carrier != null;
        }

        /// <summary>Finds the animator in children, searching at most once a second while there is none.</summary>
        private bool ResolveAnimator() {
            if (animator == null) {
                if (Time.unscaledTime < _nextAnimatorSearch) return false;

                _nextAnimatorSearch = Time.unscaledTime + AnimatorRetrySeconds;
                animator = GetComponentInChildren<Animator>();
            }

            return animator != null && animator.isInitialized && BoneResolver.HasSkeleton(animator);
        }
    }
}
