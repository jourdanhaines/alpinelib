using System;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// The sky's cached meshes: one unit silhouette per backdrop (kind, variant), built once from a seed and a
    /// palette and shared by every camera. <see cref="Dispose"/> destroys them.
    /// </summary>
    public sealed class SkyMeshVariants : IDisposable {
        private readonly Mesh[] _backdrop = new Mesh[BackdropSettings.MeshCount];

        /// <summary>Builds every backdrop silhouette of <paramref name="seed"/> painted from <paramref name="palette"/>. Main thread.</summary>
        public SkyMeshVariants(ulong seed, BackdropPalette palette) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            for (int index = 0; index < _backdrop.Length; index++) {
                var kind = (BackdropFeatureKind)(index / BackdropSettings.VariantsPerKind);
                int variant = index % BackdropSettings.VariantsPerKind;
                _backdrop[index] = FacetMeshUnity.ToMesh(BackdropSilhouette.Build(kind, variant, seed, palette), $"Backdrop {kind} {variant}");
            }
        }

        /// <summary>True once <see cref="Dispose"/> ran.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Backdrop meshes, indexed by <see cref="BackdropFeature.MeshIndex"/>.</summary>
        public int BackdropCount => _backdrop.Length;

        /// <summary>The silhouette of <see cref="BackdropFeature.MeshIndex"/> <paramref name="meshIndex"/>.</summary>
        public Mesh Backdrop(int meshIndex) {
            return _backdrop[meshIndex];
        }

        public void Dispose() {
            if (IsDisposed) return;

            IsDisposed = true;
            for (int index = 0; index < _backdrop.Length; index++) {
                DestroyMesh(_backdrop[index]);
                _backdrop[index] = null;
            }
        }

        private static void DestroyMesh(Mesh mesh) {
            if (mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
