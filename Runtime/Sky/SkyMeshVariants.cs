using System;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// The sky's cached meshes: one unit silhouette per backdrop (kind, variant) and the ground annulus under the
    /// ring, built once from a seed, a palette and the ring's settings and shared by every camera.
    /// <see cref="Dispose"/> destroys them.
    /// </summary>
    public sealed class SkyMeshVariants : IDisposable {
        private readonly Mesh[] _backdrop = new Mesh[BackdropSettings.MeshCount];
        private Mesh _ground;

        /// <summary>Builds every backdrop silhouette of <paramref name="seed"/> painted from <paramref name="palette"/>, and the annulus of <paramref name="settings"/>. Main thread.</summary>
        public SkyMeshVariants(ulong seed, BackdropPalette palette, BackdropSettings settings) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            for (int index = 0; index < _backdrop.Length; index++) {
                var kind = (BackdropFeatureKind)(index / BackdropSettings.VariantsPerKind);
                int variant = index % BackdropSettings.VariantsPerKind;
                _backdrop[index] = FacetMeshUnity.ToMesh(BackdropSilhouette.Build(kind, variant, seed, palette), $"Backdrop {kind} {variant}");
            }
            _ground = FacetMeshUnity.ToMesh(BackdropSilhouette.BuildGround(settings, palette), "Backdrop Ground");
        }

        /// <summary>True once <see cref="Dispose"/> ran.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Backdrop meshes, indexed by <see cref="BackdropFeature.MeshIndex"/>.</summary>
        public int BackdropCount => _backdrop.Length;

        /// <summary>The silhouette of <see cref="BackdropFeature.MeshIndex"/> <paramref name="meshIndex"/>.</summary>
        public Mesh Backdrop(int meshIndex) {
            return _backdrop[meshIndex];
        }

        /// <summary>The ground annulus, in metres about its centre.</summary>
        public Mesh Ground => _ground;

        public void Dispose() {
            if (IsDisposed) return;

            IsDisposed = true;
            for (int index = 0; index < _backdrop.Length; index++) {
                DestroyMesh(_backdrop[index]);
                _backdrop[index] = null;
            }
            DestroyMesh(_ground);
            _ground = null;
        }

        private static void DestroyMesh(Mesh mesh) {
            if (mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
