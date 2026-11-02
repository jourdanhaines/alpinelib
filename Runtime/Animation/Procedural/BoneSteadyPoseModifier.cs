using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Holds one bone at a reference rotation, and its position too when asked, blended in by
    /// <see cref="PoseModifier.Weight"/>, e.g. a spine kept at the pose a held prop was authored on while the
    /// legs walk underneath, or the hips' bob cancelled for a held prop the local camera reads.
    /// </summary>
    /// <remarks>
    /// Stack order −10 by default: before <see cref="LookPoseModifier"/> (0), so the look still bends the
    /// steadied bone and the eye model is unchanged. Without <see cref="SteadyPosition"/> only the bone's rotation
    /// is replaced and the hips' bob below it carries through; with it the position is pinned too (rotation
    /// first, then position). Children follow with it. The reference is relative to the
    /// bone's parent or to the actor root (<see cref="BoneSteadySpace"/>); the actor root also cancels the
    /// hips' turn and roll in a walk. A no-op at weight 0 (the stack skips it) or before a reference is set.
    /// The bone comes from the humanoid avatar or a bone map.
    /// </remarks>
    public class BoneSteadyPoseModifier : PoseModifier {
        /// <summary>Stack order new components get: before the look at 0.</summary>
        public const int DefaultOrder = -10;

        [Tooltip("Bone held at the reference.")]
        [SerializeField] private HumanBodyBones bone = HumanBodyBones.Spine;

        [Tooltip("What the reference rotation is relative to.")]
        [SerializeField] private BoneSteadySpace space = BoneSteadySpace.Parent;

        [Tooltip("Rotation the bone is blended toward, relative to the chosen space.")]
        [SerializeField] private Quaternion referenceRotation = Quaternion.identity;

        [Tooltip("Position the bone is blended toward, relative to the chosen space; used when Steady Position is on.")]
        [SerializeField] private Vector3 referencePosition;

        [Tooltip("Pin the bone's position as well as its rotation; off leaves the animated position.")]
        [SerializeField] private bool steadyPosition;

        [Tooltip("False until a reference is set; the layer does nothing without one.")]
        [SerializeField] private bool hasReference;

        private Transform _bone;
        private Transform _actorRoot;

        public BoneSteadyPoseModifier() : base(DefaultOrder) {
        }

        /// <summary>Bone held at the reference.</summary>
        public HumanBodyBones Bone => bone;

        /// <summary>What <see cref="ReferenceRotation"/> is relative to.</summary>
        public BoneSteadySpace Space => space;

        /// <summary>The rotation the bone is blended toward, relative to <see cref="Space"/>.</summary>
        public Quaternion ReferenceRotation => referenceRotation;

        /// <summary>The position the bone is blended toward, relative to <see cref="Space"/>, when <see cref="SteadyPosition"/>.</summary>
        public Vector3 ReferencePosition => referencePosition;

        /// <summary>True when the bone's position is pinned as well as its rotation.</summary>
        public bool SteadyPosition => steadyPosition;

        /// <summary>True once a reference is set.</summary>
        public bool HasReference => hasReference;

        /// <summary>The bound bone's transform, or null before the stack binds.</summary>
        public Transform BoundBone => _bone;

        /// <summary>Changes the held bone; takes effect at the next bind.</summary>
        public void SetBone(HumanBodyBones target) {
            bone = target;
        }

        /// <summary>Sets the rotation the bone is blended toward, relative to <paramref name="relativeTo"/>; the position stays as set.</summary>
        public void SetReference(Quaternion rotation, BoneSteadySpace relativeTo) {
            referenceRotation = rotation.normalized;
            space = relativeTo;
            hasReference = true;
        }

        /// <summary>Sets the rotation and position the bone is blended toward, relative to <paramref name="relativeTo"/>, and turns the position pin on.</summary>
        public void SetReference(Quaternion rotation, Vector3 position, BoneSteadySpace relativeTo) {
            SetReference(rotation, relativeTo);
            referencePosition = position;
            steadyPosition = true;
        }

        /// <summary>Takes the bound bone's current rotation (and position, when pinned) in <see cref="Space"/> as the reference; false before the stack binds.</summary>
        public bool CaptureReference() {
            if (_bone == null) return false;

            Quaternion rotation = Quaternion.Inverse(SpaceRotation()) * _bone.rotation;
            if (!steadyPosition) {
                SetReference(rotation, space);
                return true;
            }

            Transform frame = SpaceFrame();
            Vector3 position = frame != null ? frame.InverseTransformPoint(_bone.position) : _bone.position;
            SetReference(rotation, position, space);
            return true;
        }

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actorRoot = actorRoot;
            _bone = bones.Find(bone);
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            if (_bone == null || !hasReference) return;

            Quaternion target = SpaceRotation() * referenceRotation;
            bones.RotateWorldTo(_bone, Quaternion.Slerp(_bone.rotation, target, Weight));
            if (!steadyPosition) return;

            bones.MoveWorldTo(_bone, Vector3.Lerp(_bone.position, SpacePoint(referencePosition), Weight));
        }

        // The transform the reference is relative to this frame.
        private Transform SpaceFrame() {
            return space == BoneSteadySpace.Actor ? _actorRoot : _bone.parent;
        }

        // The world rotation the reference is relative to this frame.
        private Quaternion SpaceRotation() {
            Transform frame = SpaceFrame();
            return frame != null ? frame.rotation : Quaternion.identity;
        }

        // A point given relative to the reference space, in world space this frame.
        private Vector3 SpacePoint(Vector3 point) {
            Transform frame = SpaceFrame();
            return frame != null ? frame.TransformPoint(point) : point;
        }
    }
}
