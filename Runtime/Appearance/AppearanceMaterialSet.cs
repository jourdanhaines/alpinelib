using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// One selectable look of an item. A single material covers every submesh of every renderer; several
    /// are matched to submeshes by index. Null entries keep the prefab's own material.
    /// </summary>
    [Serializable]
    public class AppearanceMaterialSet {
        [Tooltip("Editor-facing name of this look. Never travels on the wire; the set's index does.")]
        [SerializeField] private string label;
        [Tooltip("One material for every submesh, or one per submesh index. Empty entries keep the prefab's material.")]
        [SerializeField] private Material[] materials = Array.Empty<Material>();

        public AppearanceMaterialSet() {
        }

        public AppearanceMaterialSet(string label, params Material[] materials) {
            this.label = label;
            this.materials = materials ?? Array.Empty<Material>();
        }

        /// <summary>Editor-facing name of this look.</summary>
        public string Label => label;

        /// <summary>The authored materials.</summary>
        public IReadOnlyList<Material> Materials => materials ?? Array.Empty<Material>();

        /// <summary>Overwrites <paramref name="submeshMaterials"/> with this set's non-null materials.</summary>
        public void ApplyTo(Material[] submeshMaterials) {
            if (materials == null || materials.Length == 0 || submeshMaterials == null) return;
            if (materials.Length == 1) {
                FillAll(submeshMaterials, materials[0]);
                return;
            }

            int count = Mathf.Min(materials.Length, submeshMaterials.Length);
            for (int index = 0; index < count; index++) {
                if (materials[index] != null) submeshMaterials[index] = materials[index];
            }
        }

        private static void FillAll(Material[] submeshMaterials, Material material) {
            if (material == null) return;

            for (int index = 0; index < submeshMaterials.Length; index++) {
                submeshMaterials[index] = material;
            }
        }
    }
}
