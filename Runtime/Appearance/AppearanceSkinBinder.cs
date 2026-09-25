using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>Rebinds a skinned item's bones, by name, onto a character's own skeleton.</summary>
    public static class AppearanceSkinBinder {
        /// <summary>
        /// Points every bone and the root bone of <paramref name="renderer"/> at the same-named transform in
        /// <paramref name="skeleton"/>. Any null, missing or ambiguous bone fails the whole bind and leaves
        /// the renderer untouched.
        /// </summary>
        public static bool TryBind(SkinnedMeshRenderer renderer, AppearanceSkeleton skeleton, out string error) {
            error = null;
            if (renderer == null || skeleton == null || !skeleton.IsValid) {
                error = "There is no renderer or no live skeleton to bind.";
                return false;
            }

            Transform[] sourceBones = renderer.bones;
            if (sourceBones == null || sourceBones.Length == 0) {
                error = $"'{renderer.name}' has no bones to bind.";
                return false;
            }

            var problems = new List<string>();
            var bones = new Transform[sourceBones.Length];
            for (int index = 0; index < sourceBones.Length; index++) {
                bones[index] = Resolve(sourceBones[index], skeleton, $"bone {index}", problems);
            }

            Transform rootBone = Resolve(renderer.rootBone, skeleton, "root bone", problems);
            if (problems.Count > 0) {
                error = $"'{renderer.name}' cannot bind: {string.Join("; ", problems)}.";
                return false;
            }

            Bounds bounds = renderer.localBounds;
            renderer.bones = bones;
            renderer.rootBone = rootBone;
            renderer.localBounds = bounds;
            return true;
        }

        private static Transform Resolve(Transform source, AppearanceSkeleton skeleton, string role, List<string> problems) {
            if (source == null) {
                problems.Add($"{role} is null");
                return null;
            }

            if (skeleton.TryGet(source.name, out Transform bone)) return bone;

            problems.Add(skeleton.IsAmbiguous(source.name) ? $"'{source.name}' is ambiguous on the body" : $"'{source.name}' is missing from the body");
            return null;
        }
    }
}
