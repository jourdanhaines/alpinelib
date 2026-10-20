using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Keeps the skinned pieces that a holder's hands deform drawn while the hands are raised to hold a
    /// prop, by switching their <see cref="SkinnedMeshRenderer.updateWhenOffscreen"/> on.
    /// </summary>
    /// <remarks>
    /// A skinned renderer is culled by its bind-pose bounds unless it updates offscreen, and hands raised
    /// in front of a first-person eye sit well outside the bounds of hands hanging at the hips, so the arms
    /// vanish exactly while the prop is in view. <see cref="Keep"/> turns offscreen updates on for every
    /// renderer under the animator whose bones include one of the given bones (both hands by default);
    /// <see cref="Release"/> puts each renderer's own setting back. Call <see cref="Refresh"/> after the
    /// body's renderers are rebuilt (worn sleeves or gloves) so new pieces are kept too.
    /// </remarks>
    public sealed class SkinnedReachKeeper {
        private static readonly HumanBodyBones[] DefaultBones = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand };

        private readonly Animator _animator;
        private readonly HumanBodyBones[] _bones;
        private readonly Dictionary<SkinnedMeshRenderer, bool> _previous = new Dictionary<SkinnedMeshRenderer, bool>();
        private readonly List<SkinnedMeshRenderer> _kept = new List<SkinnedMeshRenderer>();
        private readonly HashSet<Transform> _boneTransforms = new HashSet<Transform>();

        public SkinnedReachKeeper(Animator animator, params HumanBodyBones[] bones) {
            _animator = animator;
            _bones = bones != null && bones.Length > 0 ? bones : DefaultBones;
        }

        /// <summary>True between <see cref="Keep"/> and <see cref="Release"/>.</summary>
        public bool IsKeeping { get; private set; }

        /// <summary>Renderers currently kept updating offscreen.</summary>
        public IReadOnlyList<SkinnedMeshRenderer> Renderers => _kept;

        /// <summary>Turns offscreen updates on for every renderer the bones deform.</summary>
        public void Keep() {
            IsKeeping = true;
            Apply();
        }

        /// <summary>Re-scans the renderers while keeping, e.g. after the body's pieces were rebuilt.</summary>
        public void Refresh() {
            if (!IsKeeping) return;

            Apply();
        }

        /// <summary>Restores each kept renderer's own offscreen setting.</summary>
        public void Release() {
            IsKeeping = false;
            foreach (KeyValuePair<SkinnedMeshRenderer, bool> entry in _previous) {
                if (entry.Key != null) entry.Key.updateWhenOffscreen = entry.Value;
            }

            _previous.Clear();
            _kept.Clear();
        }

        /// <summary>
        /// True when <paramref name="renderer"/> has vertices weighted to any of <paramref name="bones"/>. A
        /// mesh whose weights cannot be read (not readable, in a player build) counts whenever its bone list
        /// holds one of them, which on a shared skeleton keeps more pieces than needed but never too few.
        /// </summary>
        public static bool IsSkinnedTo(SkinnedMeshRenderer renderer, ICollection<Transform> bones) {
            if (renderer == null || renderer.bones == null) return false;

            var indices = new HashSet<int>();
            Transform[] rendererBones = renderer.bones;
            for (int index = 0; index < rendererBones.Length; index++) {
                if (rendererBones[index] != null && bones.Contains(rendererBones[index])) indices.Add(index);
            }

            if (indices.Count == 0) return false;

            // The editor can read any mesh's weights; a player build only a readable one.
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || (!mesh.isReadable && !Application.isEditor)) return true;

            return HasWeightOn(mesh, indices);
        }

        private static bool HasWeightOn(Mesh mesh, HashSet<int> boneIndices) {
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            foreach (BoneWeight1 weight in weights) {
                if (weight.weight > 0f && boneIndices.Contains(weight.boneIndex)) return true;
            }

            return false;
        }

        private void Apply() {
            if (_animator == null || !ResolveBones()) return;

            _kept.RemoveAll((renderer) => renderer == null);
            foreach (SkinnedMeshRenderer renderer in _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                KeepRenderer(renderer);
            }
        }

        private void KeepRenderer(SkinnedMeshRenderer renderer) {
            if (!IsSkinnedTo(renderer, _boneTransforms)) return;

            if (!_previous.ContainsKey(renderer)) {
                _previous[renderer] = renderer.updateWhenOffscreen;
                _kept.Add(renderer);
            }

            renderer.updateWhenOffscreen = true;
        }

        private bool ResolveBones() {
            _boneTransforms.Clear();
            if (!_animator.isHuman) return false;

            foreach (HumanBodyBones bone in _bones) {
                Transform transform = _animator.GetBoneTransform(bone);
                if (transform != null) _boneTransforms.Add(transform);
            }

            return _boneTransforms.Count > 0;
        }
    }
}
