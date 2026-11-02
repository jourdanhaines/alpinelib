using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Animation.Bones {
    /// <summary>
    /// Names the transforms of a generic rig by humanoid slot, so systems that ask for
    /// <see cref="HumanBodyBones"/> work on a body without a humanoid avatar.
    /// </summary>
    /// <remarks>
    /// Data only; resolved through <see cref="BoneResolver"/>. Transforms are matched by exact name under
    /// the skeleton root, so a re-exported body keeps its map. A name found twice is ambiguous and never
    /// resolves. Subtrees whose name starts with a skipped prefix (built appearance pieces) are ignored.
    /// </remarks>
    [DisallowMultipleComponent]
    public class BoneMap : MonoBehaviour {
        [Tooltip("Transform the bone names are searched under; this transform when left empty.")]
        [SerializeField] private Transform skeletonRoot;

        [SerializeField] private BoneMapEntry[] entries = new BoneMapEntry[0];

        [Tooltip("Subtrees whose root name starts with one of these are not searched.")]
        [SerializeField] private string[] skippedPrefixes = { "Appearance_" };

        private readonly Dictionary<HumanBodyBones, Transform> _resolved = new Dictionary<HumanBodyBones, Transform>();
        private readonly Dictionary<string, Transform> _byName = new Dictionary<string, Transform>();
        private readonly HashSet<string> _ambiguous = new HashSet<string>();
        private bool _isIndexed;

        /// <summary>The authored slot → name rows.</summary>
        public IReadOnlyList<BoneMapEntry> Entries => entries ?? Array.Empty<BoneMapEntry>();

        /// <summary>Transform the names are searched under.</summary>
        public Transform SkeletonRoot => skeletonRoot != null ? skeletonRoot : transform;

        /// <summary>Replaces the map, e.g. from an assembler building the body prefab.</summary>
        public void SetEntries(Transform root, BoneMapEntry[] mapEntries) {
            skeletonRoot = root;
            entries = mapEntries ?? new BoneMapEntry[0];
            Invalidate();
        }

        /// <summary>Drops the name index, e.g. after the skeleton was rebuilt.</summary>
        public void Invalidate() {
            _resolved.Clear();
            _byName.Clear();
            _ambiguous.Clear();
            _isIndexed = false;
        }

        /// <summary>The transform mapped to <paramref name="bone"/>; false when unmapped, missing or ambiguous.</summary>
        public bool TryGet(HumanBodyBones bone, out Transform found) {
            if (_resolved.TryGetValue(bone, out found)) {
                if (found != null) return true;
                if (ReferenceEquals(found, null)) return false;

                // A cached transform that was destroyed: the skeleton was rebuilt under the map.
                Invalidate();
            }

            EnsureIndexed();
            found = ResolveEntry(bone);
            _resolved[bone] = found;
            return found != null;
        }

        /// <summary>
        /// Appends one line per problem — empty or missing names, ambiguous names, a slot mapped twice —
        /// and returns true when there were none. Re-indexes first so it reflects the current hierarchy.
        /// </summary>
        public bool Validate(List<string> problems) {
            Invalidate();
            EnsureIndexed();

            int before = problems.Count;
            var seen = new HashSet<HumanBodyBones>();
            foreach (BoneMapEntry entry in Entries) {
                if (!seen.Add(entry.bone)) problems.Add($"{entry.bone} is mapped more than once.");

                ValidateEntry(entry, problems);
            }

            return problems.Count == before;
        }

        private void ValidateEntry(BoneMapEntry entry, List<string> problems) {
            if (string.IsNullOrEmpty(entry.transformName)) {
                problems.Add($"{entry.bone} has no transform name.");
                return;
            }

            if (_ambiguous.Contains(entry.transformName)) {
                problems.Add($"{entry.bone}: '{entry.transformName}' names more than one transform under '{SkeletonRoot.name}'.");
                return;
            }

            if (!_byName.ContainsKey(entry.transformName)) {
                problems.Add($"{entry.bone}: no transform named '{entry.transformName}' under '{SkeletonRoot.name}'.");
            }
        }

        private Transform ResolveEntry(HumanBodyBones bone) {
            foreach (BoneMapEntry entry in Entries) {
                if (entry.bone != bone) continue;
                if (string.IsNullOrEmpty(entry.transformName) || _ambiguous.Contains(entry.transformName)) return null;

                return _byName.TryGetValue(entry.transformName, out Transform found) ? found : null;
            }

            return null;
        }

        // Indexes only the names the map asks for, so unrelated duplicates elsewhere do not matter.
        private void EnsureIndexed() {
            if (_isIndexed) return;

            _isIndexed = true;
            var wanted = new HashSet<string>();
            foreach (BoneMapEntry entry in Entries) {
                if (!string.IsNullOrEmpty(entry.transformName)) wanted.Add(entry.transformName);
            }

            Transform root = SkeletonRoot;
            var pending = new Stack<Transform>();
            pending.Push(root);
            while (pending.Count > 0) {
                Transform current = pending.Pop();
                if (current != root && IsSkipped(current.name)) continue;

                if (wanted.Contains(current.name)) AddName(current);
                for (int index = 0; index < current.childCount; index++) pending.Push(current.GetChild(index));
            }
        }

        private void AddName(Transform bone) {
            if (_byName.ContainsKey(bone.name)) {
                _ambiguous.Add(bone.name);
                return;
            }

            _byName.Add(bone.name, bone);
        }

        private bool IsSkipped(string transformName) {
            if (skippedPrefixes == null) return false;

            foreach (string prefix in skippedPrefixes) {
                if (!string.IsNullOrEmpty(prefix) && transformName.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private void OnValidate() {
            Invalidate();
        }
    }
}
