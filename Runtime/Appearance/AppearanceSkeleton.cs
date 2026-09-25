using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// Every transform under a character's model root, by name — the lookup skinned items rebind their
    /// bones through and socket items find their attach point in.
    /// </summary>
    /// <remarks>
    /// Bones are matched by name, never by path or serialized reference, so a re-exported body keeps its
    /// items. Built appearance pieces are skipped; a name found twice is ambiguous and never resolves.
    /// </remarks>
    public sealed class AppearanceSkeleton {
        private readonly Dictionary<string, Transform> _byName = new Dictionary<string, Transform>();
        private readonly HashSet<string> _ambiguous = new HashSet<string>();

        private AppearanceSkeleton(Transform root) {
            Root = root;
        }

        /// <summary>The model root the map was built under.</summary>
        public Transform Root { get; }

        /// <summary>False once the model root has been destroyed.</summary>
        public bool IsValid => Root != null;

        /// <summary>Number of uniquely named transforms.</summary>
        public int Count => _byName.Count - _ambiguous.Count;

        /// <summary>Maps every transform under <paramref name="modelRoot"/>, itself included.</summary>
        public static bool TryBuild(Transform modelRoot, out AppearanceSkeleton skeleton, out string error) {
            skeleton = null;
            if (modelRoot == null) {
                error = "There is no model root to map.";
                return false;
            }

            skeleton = new AppearanceSkeleton(modelRoot);
            skeleton.AddSubtree(modelRoot);
            error = null;
            return true;
        }

        /// <summary>Finds the one live transform named <paramref name="boneName"/>.</summary>
        public bool TryGet(string boneName, out Transform bone) {
            bone = null;
            if (string.IsNullOrEmpty(boneName) || _ambiguous.Contains(boneName)) return false;
            if (!_byName.TryGetValue(boneName, out bone)) return false;

            return bone != null;
        }

        /// <summary>True when more than one transform carries <paramref name="boneName"/>.</summary>
        public bool IsAmbiguous(string boneName) {
            return boneName != null && _ambiguous.Contains(boneName);
        }

        private void AddSubtree(Transform root) {
            var pending = new Stack<Transform>();
            pending.Push(root);
            while (pending.Count > 0) {
                Transform current = pending.Pop();
                if (current != root && current.name.StartsWith(CharacterAppearance.PiecePrefix)) continue;

                Add(current);
                for (int index = 0; index < current.childCount; index++) pending.Push(current.GetChild(index));
            }
        }

        private void Add(Transform bone) {
            if (_byName.ContainsKey(bone.name)) {
                _ambiguous.Add(bone.name);
                return;
            }

            _byName.Add(bone.name, bone);
        }
    }
}
