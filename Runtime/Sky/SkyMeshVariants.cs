using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// The sky's cached meshes: one drifting cloud per (class, variant) with its coarse far mesh, built once from a
    /// seed and palette and shared by every camera. Records each cloud's puffs for its ground shadow.
    /// <see cref="Dispose"/> destroys them.
    /// </summary>
    public sealed class SkyMeshVariants : IDisposable {
        private readonly Mesh[] _clouds = new Mesh[CloudFieldSettings.MeshCount];
        private readonly Mesh[] _farClouds = new Mesh[CloudFieldSettings.MeshCount];
        private readonly BlobPuff[][] _cloudPuffs = new BlobPuff[CloudFieldSettings.MeshCount][];

        /// <summary>No meshes yet: build them with <see cref="BuildClouds"/>.</summary>
        public SkyMeshVariants() {
        }

        /// <summary>True once the cloud meshes are built.</summary>
        public bool HasClouds => _clouds[0] != null;

        /// <summary>
        /// Builds every cloud mesh of <paramref name="seed"/> (<see cref="CloudHeap"/>, metres, base at y 0) for the
        /// classes of <paramref name="settings"/>, painted from <paramref name="palette"/>. Main thread.
        /// </summary>
        public void BuildClouds(ulong seed, CloudFieldSettings settings, CloudPalette palette) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (IsDisposed) throw new ObjectDisposedException(nameof(SkyMeshVariants));

            settings.Validate();
            DestroyClouds();
            for (int index = 0; index < _clouds.Length; index++) {
                CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(index);
                int variant = index % CloudFieldSettings.VariantsPerClass;
                CloudClassSettings classSettings = settings.ClassOf(cloudClass);
                double diameter = CloudHeap.TargetDiameter(classSettings, variant);
                ulong variantSeed = CloudHeap.VariantSeed(seed, cloudClass, variant);
                FacetMesh facets = CloudHeap.Build(classSettings, diameter, variantSeed, palette, out BlobPuff[] puffs);
                FacetMesh far = CloudHeap.BuildFar(classSettings, diameter, variantSeed, palette, facets.Bounds.Max.Y);
                _clouds[index] = FacetMeshUnity.ToMesh(facets, $"Cloud {cloudClass} {variant}");
                _farClouds[index] = FacetMeshUnity.ToMesh(far, $"Cloud {cloudClass} {variant} Far");
                _cloudPuffs[index] = puffs;
            }
        }

        /// <summary>The cloud mesh of <see cref="CloudInstance.MeshIndex"/> <paramref name="meshIndex"/>, or null before <see cref="BuildClouds"/>.</summary>
        public Mesh Cloud(int meshIndex) {
            return _clouds[meshIndex];
        }

        /// <summary>The coarse mesh of <paramref name="meshIndex"/> drawn past the far mesh radius (same puffs, fewer facets), or null before <see cref="BuildClouds"/>.</summary>
        public Mesh FarCloud(int meshIndex) {
            return _farClouds[meshIndex];
        }

        /// <summary>The puffs cloud mesh <paramref name="meshIndex"/> was heaped from (mesh space, metres), or null.</summary>
        public IReadOnlyList<BlobPuff> CloudPuffs(int meshIndex) {
            return _cloudPuffs[meshIndex];
        }

        /// <summary>True once <see cref="Dispose"/> ran.</summary>
        public bool IsDisposed { get; private set; }

        public void Dispose() {
            if (IsDisposed) return;

            IsDisposed = true;
            DestroyClouds();
        }

        private void DestroyClouds() {
            for (int index = 0; index < _clouds.Length; index++) {
                DestroyMesh(_clouds[index]);
                DestroyMesh(_farClouds[index]);
                _clouds[index] = null;
                _farClouds[index] = null;
                _cloudPuffs[index] = null;
            }
        }

        private static void DestroyMesh(Mesh mesh) {
            if (mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
