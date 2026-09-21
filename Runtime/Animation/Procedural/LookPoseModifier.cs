using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Cameras;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Bends the spine, neck and head to follow <see cref="Actor.LookPitch"/>, and tells a first-person
    /// rig where that bend carries the eye.
    /// </summary>
    /// <remarks>
    /// The look is shared out along a chain of bones rather than given to the head alone: a head
    /// cranked ninety degrees on a rigid body reads as broken, and a first-person camera looking
    /// straight down would stare into its own chest. With the torso taking a little of it the head
    /// swings out ahead of the body, and the eye — solved through the same chain by
    /// <see cref="LookPoseSolver"/> — goes with it.
    ///
    /// The eye is solved from the resting skeleton, not the animated one, so the view is steady through
    /// a walk cycle; the bones are turned on top of the animated pose, so the body keeps its motion.
    /// It swings from a fixed height above the head joint rather than from wherever the rig rests its
    /// eye: a rig lowers its eye with a crouching capsule, and an eye below the neck would swing
    /// backwards.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class LookPoseModifier : PoseModifier, ICameraEyeModel {
        [Tooltip("Bones that share the look, from the base of the spine to the head. Shares in each direction should sum to one.")]
        [SerializeField] private LookPoseLink[] chain = {
            new LookPoseLink(HumanBodyBones.Spine, 0.05f, 0f),
            new LookPoseLink(HumanBodyBones.Chest, 0.08f, 0f),
            new LookPoseLink(HumanBodyBones.UpperChest, 0.1f, 0f),
            new LookPoseLink(HumanBodyBones.Neck, 0.3f, 0.4f),
            new LookPoseLink(HumanBodyBones.Head, 0.47f, 0.6f)
        };

        [Tooltip("How far above the last bone of the chain the eye sits, in metres.")]
        [SerializeField] private float eyeAboveHead = 0.1f;

        private readonly List<Transform> _bones = new List<Transform>();
        private LookPoseJoint[] _joints = new LookPoseJoint[0];
        private Actor _actor;
        private Transform _actorRoot;

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
            _actor = GetComponent<Actor>();
            _actorRoot = actorRoot;
            _bones.Clear();

            var joints = new List<LookPoseJoint>();
            float carriedDown = 0f;
            float carriedUp = 0f;

            foreach (LookPoseLink link in chain) {
                carriedDown += link.shareDown;
                carriedUp += link.shareUp;

                Transform bone = bones.Find(link.bone);
                if (bone == null) continue;

                _bones.Add(bone);
                joints.Add(new LookPoseJoint(actorRoot.InverseTransformPoint(bone.position), carriedDown, carriedUp));
                carriedDown = 0f;
                carriedUp = 0f;
            }

            _joints = joints.ToArray();
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            Vector3 pitchAxis = _actorRoot.right;

            for (int index = 0; index < _bones.Count; index++) {
                float bend = LookPoseSolver.ResolveBend(in _joints[index], _actor.LookPitch, Weight);
                bones.RotateWorld(_bones[index], pitchAxis, bend);
            }
        }

        /// <inheritdoc />
        public Vector3 ResolveEyeOffset(float pitchDegrees, Vector3 restingEyeLocal) {
            if (!isActiveAndEnabled || _joints.Length == 0) return Vector3.zero;

            Vector3 swungPoint = restingEyeLocal;
            swungPoint.y = _joints[_joints.Length - 1].Pivot.y + eyeAboveHead;

            return LookPoseSolver.ResolveTipOffset(_joints, swungPoint, pitchDegrees, Weight);
        }
    }
}
