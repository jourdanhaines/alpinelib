using System;
using System.Collections.Generic;
using AlpineLib.Animation.Bones;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// The skeleton as pose modifiers see it (humanoid avatar or <see cref="BoneMap"/>): bones by name, and additive writes that stay
    /// additive.
    /// </summary>
    /// <remarks>
    /// Procedural rotation and position are applied on top of whatever the animator wrote this frame. An
    /// animator that is culled writes nothing, and a layer that kept adding to its own output would wind a
    /// bone round (or drift it) a little further every frame. Each touched bone therefore remembers the
    /// animated local pose it started from and the pose it was left with: a bone found exactly as it was
    /// left was not re-animated, and is put back to its animated pose before anything is added again.
    /// </remarks>
    public class PoseBones {
        private readonly Animator _animator;
        private readonly BoneMap _map;
        private readonly Dictionary<HumanBodyBones, Transform> _bones = new Dictionary<HumanBodyBones, Transform>();
        private readonly Dictionary<string, Transform> _named = new Dictionary<string, Transform>();
        private readonly List<Transform> _touched = new List<Transform>();
        private readonly Dictionary<Transform, Quaternion> _animated = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> _written = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _animatedPosition = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> _writtenPosition = new Dictionary<Transform, Vector3>();

        public PoseBones(Animator animator) {
            _animator = animator;
            _map = BoneResolver.FindMap(animator);
        }

        /// <summary>True when bones can be resolved: a humanoid avatar or a bone map.</summary>
        public bool HasSkeleton => _animator != null && (_animator.isHuman || _map != null);

        /// <summary>Kept for existing callers; true for a mapped generic rig too.</summary>
        [Obsolete("Use HasSkeleton; a mapped generic rig also resolves bones.")]
        public bool IsHumanoid => HasSkeleton;

        /// <summary>The bone's transform, or null when neither the map nor the avatar provides it.</summary>
        public Transform Find(HumanBodyBones bone) {
            if (_bones.TryGetValue(bone, out Transform cached)) return cached;

            Transform found = BoneResolver.Find(_animator, _map, bone);
            _bones[bone] = found;
            return found;
        }

        /// <summary>
        /// The first transform named <paramref name="boneName"/> under the animator (itself included, depth first), for
        /// bones neither an avatar nor a map names (a tail, a long nose); null when there is none.
        /// </summary>
        public Transform FindByName(string boneName) {
            if (string.IsNullOrEmpty(boneName) || _animator == null) return null;
            if (_named.TryGetValue(boneName, out Transform cached) && cached != null) return cached;

            Transform found = FindDescendant(_animator.transform, boneName);
            _named[boneName] = found;
            return found;
        }

        /// <summary>
        /// Opens a frame of writes: bones the animator left untouched go back to their animated pose.
        /// </summary>
        public void BeginFrame() {
            foreach (Transform bone in _touched) {
                RebaseBone(bone);
            }
        }

        /// <summary>Turns a bone about a world space axis, on top of its current pose.</summary>
        public void RotateWorld(Transform bone, Vector3 worldAxis, float degrees) {
            if (bone == null) return;

            Track(bone);
            bone.Rotate(worldAxis, degrees, Space.World);
        }

        /// <summary>Sets a bone's world rotation outright, replacing whatever earlier layers added.</summary>
        public void RotateWorldTo(Transform bone, Quaternion worldRotation) {
            if (bone == null) return;

            Track(bone);
            bone.rotation = worldRotation;
        }

        /// <summary>Sets a bone's world position outright, replacing whatever earlier layers moved it by.</summary>
        public void MoveWorldTo(Transform bone, Vector3 worldPosition) {
            if (bone == null) return;

            Track(bone);
            bone.position = worldPosition;
        }

        /// <summary>Closes the frame, remembering what each touched bone was left with.</summary>
        public void EndFrame() {
            foreach (Transform bone in _touched) {
                _written[bone] = bone.localRotation;
                _writtenPosition[bone] = bone.localPosition;
            }
        }

        private static Transform FindDescendant(Transform parent, string boneName) {
            if (parent.name == boneName) return parent;

            foreach (Transform child in parent) {
                Transform found = FindDescendant(child, boneName);
                if (found != null) return found;
            }

            return null;
        }

        private void Track(Transform bone) {
            if (_animated.ContainsKey(bone)) return;

            _touched.Add(bone);
            _animated[bone] = bone.localRotation;
            _animatedPosition[bone] = bone.localPosition;
        }

        private void RebaseBone(Transform bone) {
            if (bone == null) return;

            RebaseRotation(bone);
            RebasePosition(bone);
        }

        // Exact comparisons on purpose: Unity's == calls two values a hair apart equal, which would
        // mistake a barely moving animated bone for one the animator skipped. Rotation and position are
        // judged apart, since a rig may animate one channel of a bone and leave the other alone.
        private void RebaseRotation(Transform bone) {
            bool wasReanimated = !_written.TryGetValue(bone, out Quaternion written) || !bone.localRotation.Equals(written);

            if (wasReanimated) {
                _animated[bone] = bone.localRotation;
                return;
            }

            bone.localRotation = _animated[bone];
        }

        private void RebasePosition(Transform bone) {
            bool wasReanimated = !_writtenPosition.TryGetValue(bone, out Vector3 written) || !bone.localPosition.Equals(written);

            if (wasReanimated) {
                _animatedPosition[bone] = bone.localPosition;
                return;
            }

            bone.localPosition = _animatedPosition[bone];
        }
    }
}
