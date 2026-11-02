using UnityEngine;

namespace AlpineLib.Animation.Bones {
    /// <summary>
    /// Finds a body's bones by humanoid slot: through a <see cref="BoneMap"/> when the animator has one
    /// on itself or a parent, else through the humanoid avatar.
    /// </summary>
    /// <remarks>
    /// The map lookup walks the hierarchy; callers that resolve every frame cache the result (as
    /// <c>PoseBones</c> does) or hold the map from <see cref="FindMap"/>.
    /// </remarks>
    public static class BoneResolver {
        /// <summary>The bone's transform, or null when neither a map nor the avatar provides it.</summary>
        public static Transform Find(Animator animator, HumanBodyBones bone) {
            return Find(animator, FindMap(animator), bone);
        }

        /// <summary>As <see cref="Find(Animator, HumanBodyBones)"/>, with a map the caller already found.</summary>
        public static Transform Find(Animator animator, BoneMap map, HumanBodyBones bone) {
            if (map != null) return map.TryGet(bone, out Transform mapped) ? mapped : null;
            if (animator == null || !animator.isHuman) return null;

            return animator.GetBoneTransform(bone);
        }

        /// <summary>True when the animator's bones can be resolved: a humanoid avatar or a bone map.</summary>
        public static bool HasSkeleton(Animator animator) {
            if (animator == null) return false;

            return animator.isHuman || FindMap(animator) != null;
        }

        /// <summary>The bone map on the animator or one of its parents, or null.</summary>
        public static BoneMap FindMap(Animator animator) {
            return animator != null ? animator.GetComponentInParent<BoneMap>(true) : null;
        }
    }
}
