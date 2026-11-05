using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Fading twins of shared materials: a copy with <see cref="DitherFade.Keyword"/> on, made once per source and
    /// drawn only while something fades, so finished fades go back to the shared material. Twins are instantiated from
    /// the source's committed template (<see cref="DitherFadeTemplates"/>, which ships the fading variant) and take the
    /// source's current values; a source without one gets a runtime copy and a warning, since a player build may lack
    /// its variant. Main thread; <see cref="Dispose"/> destroys every twin.
    /// </summary>
    public sealed class DitherFadeMaterials : IDisposable {
        private readonly Dictionary<Material, Material> _twins = new Dictionary<Material, Material>();
        private readonly List<Material> _untemplated = new List<Material>();
        private readonly DitherFadeTemplates _templates;
        private readonly bool _warnWithoutTemplate;

        /// <summary>Runtime twins only, no warnings (editor tools, gates).</summary>
        public DitherFadeMaterials() : this(null, false) {
        }

        /// <summary>Twins from <paramref name="templates"/>; a source without one warns when <paramref name="warnWithoutTemplate"/>.</summary>
        public DitherFadeMaterials(DitherFadeTemplates templates, bool warnWithoutTemplate = true) {
            _templates = templates;
            _warnWithoutTemplate = warnWithoutTemplate;
        }

        /// <summary>Twins made so far.</summary>
        public int Count => _twins.Count;

        /// <summary>The sources twins were made for, with or without a template.</summary>
        public IEnumerable<Material> Sources {
            get {
                foreach (KeyValuePair<Material, Material> pair in _twins) {
                    if (pair.Value != null) yield return pair.Key;
                }
            }
        }

        /// <summary>Sources that had no template, so their twins are runtime copies.</summary>
        public IReadOnlyList<Material> Untemplated => _untemplated;

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
            _untemplated.Clear();
        }

        private Material CreateTwin(Material source) {
            Material template = null;
            if (_templates == null || !_templates.TryGet(source, out template)) NoteUntemplated(source);

            var twin = new Material(template != null ? template : source) { name = $"{source.name} (Fade)", hideFlags = HideFlags.DontSave };
            if (template != null) twin.CopyPropertiesFromMaterial(source);
            twin.EnableKeyword(new UnityEngine.Rendering.LocalKeyword(source.shader, DitherFade.Keyword));
            twin.SetFloat(DitherFade.ToggleId, 1f);
            twin.SetFloat(DitherFade.AmountId, 1f);
            return twin;
        }

        private void NoteUntemplated(Material source) {
            _untemplated.Add(source);
            if (!_warnWithoutTemplate) return;

            Debug.LogWarning($"DitherFadeMaterials: no fade template for '{source.name}'; its twin is a runtime copy whose fading variant a player build may strip (it would pop in).");
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
