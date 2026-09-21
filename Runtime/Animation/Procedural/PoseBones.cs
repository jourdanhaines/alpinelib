using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// The humanoid skeleton as pose modifiers see it: bones by name, and additive writes that stay
    /// additive.
    /// </summary>
    /// <remarks>
    /// Procedural rotation is applied on top of whatever the animator wrote this frame. An animator
    /// that is culled writes nothing, and a layer that kept adding to its own output would wind a bone
    /// round a little further every frame. Each touched bone therefore remembers the animated rotation
    /// it started from and the rotation it was left with: a bone found exactly as it was left was not
    /// re-animated, and is put back to its animated rotation before anything is added again.
    /// </remarks>
    public class PoseBones {
        private readonly Animator _animator;
        private readonly Dictionary<HumanBodyBones, Transform> _bones = new Dictionary<HumanBodyBones, Transform>();
        private readonly List<Transform> _touched = new List<Transform>();
        private readonly Dictionary<Transform, Quaternion> _animated = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> _written = new Dictionary<Transform, Quaternion>();

        public PoseBones(Animator animator) {
            _animator = animator;
        }

        /// <summary>True when the animator has a humanoid avatar to resolve bones against.</summary>
        public bool IsHumanoid => _animator != null && _animator.isHuman;

        /// <summary>The bone's transform, or null when the avatar does not map it.</summary>
        public Transform Find(HumanBodyBones bone) {
            if (_bones.TryGetValue(bone, out Transform cached)) return cached;

            Transform found = IsHumanoid ? _animator.GetBoneTransform(bone) : null;
            _bones[bone] = found;
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

        /// <summary>Closes the frame, remembering what each touched bone was left with.</summary>
        public void EndFrame() {
            foreach (Transform bone in _touched) {
                _written[bone] = bone.localRotation;
            }
        }

        private void Track(Transform bone) {
            if (_animated.ContainsKey(bone)) return;

            _touched.Add(bone);
            _animated[bone] = bone.localRotation;
        }

        private void RebaseBone(Transform bone) {
            if (bone == null) return;

            // Exact comparison on purpose: Unity's == calls two rotations a hair apart equal, which
            // would mistake a barely moving animated bone for one the animator skipped.
            bool wasReanimated = !_written.TryGetValue(bone, out Quaternion written) || !bone.localRotation.Equals(written);

            if (wasReanimated) {
                _animated[bone] = bone.localRotation;
                return;
            }

            bone.localRotation = _animated[bone];
        }
    }
}
