using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Fading twins of shared materials: a copy with <see cref="DitherFade.Keyword"/> on, made once per source and
    /// drawn only while something fades, so finished fades go back to the shared material. Main thread;
    /// <see cref="Dispose"/> destroys every twin.
    /// </summary>
    public sealed class DitherFadeMaterials : IDisposable {
        private readonly Dictionary<Material, Material> _twins = new Dictionary<Material, Material>();

        /// <summary>Twins made so far.</summary>
        public int Count => _twins.Count;

        /// <summary>The fading twin of <paramref name="source"/>; false when its shader cannot fade.</summary>
        public bool TryGetTwin(Material source, out Material twin) {
            twin = null;
            if (source == null) return false;
            if (_twins.TryGetValue(source, out twin)) return twin != null;
            if (!DitherFade.Supports(source)) {
                _twins.Add(source, null);
                return false;
            }

            twin = CreateTwin(source);
            _twins.Add(source, twin);
            return true;
        }

        /// <summary>True when every one of <paramref name="sources"/> has a twin, written to <paramref name="twins"/>.</summary>
        public bool TryGetTwins(Material[] sources, Material[] twins) {
            for (int index = 0; index < sources.Length; index++) {
                if (!TryGetTwin(sources[index], out twins[index])) return false;
            }
            return true;
        }

        public void Dispose() {
            foreach (Material twin in _twins.Values) {
                if (twin != null) DestroyObject(twin);
            }
            _twins.Clear();
        }

        private static Material CreateTwin(Material source) {
            var twin = new Material(source) { name = $"{source.name} (Fade)", hideFlags = HideFlags.DontSave };
            twin.EnableKeyword(new UnityEngine.Rendering.LocalKeyword(source.shader, DitherFade.Keyword));
            twin.SetFloat(DitherFade.ToggleId, 1f);
            twin.SetFloat(DitherFade.AmountId, 1f);
            return twin;
        }

        private static void DestroyObject(Object target) {
            if (Application.isPlaying) {
                Object.Destroy(target);
                return;
            }
            Object.DestroyImmediate(target);
        }
    }
}
