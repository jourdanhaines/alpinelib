using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// The sky's cached meshes: one unit silhouette per backdrop (kind, variant) and the ground annulus under the
    /// ring, built once from a seed, a palette and the ring's settings and shared by every camera.
    /// <see cref="Dispose"/> destroys them.
    /// </summary>
    /// <remarks>
    /// Records each cloud bank's own width over height (<see cref="CloudBankAspects"/>) for the layout to size it by.
    /// Vertex alpha is the fog blend weight <c>_FOG_SCALE_BLEND</c> reads: rock's foot fog
    /// (<see cref="BackdropSilhouette.FootFogWeight"/>), 0 on cloud banks and the annulus.
    /// </remarks>
    public sealed class SkyMeshVariants : IDisposable {
        private readonly Mesh[] _backdrop = new Mesh[BackdropSettings.MeshCount];
        private readonly double[] _cloudBankAspects = new double[BackdropSettings.VariantsPerKind];
        private Mesh _ground;

        /// <summary>Builds every backdrop silhouette of <paramref name="seed"/> painted from <paramref name="palette"/>, and the annulus of <paramref name="settings"/>. Main thread.</summary>
        public SkyMeshVariants(ulong seed, BackdropPalette palette, BackdropSettings settings) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            for (int index = 0; index < _backdrop.Length; index++) {
                var kind = (BackdropFeatureKind)(index / BackdropSettings.VariantsPerKind);
                int variant = index % BackdropSettings.VariantsPerKind;
                FacetMesh facets = BuildSilhouette(kind, variant, seed, palette);
                bool isRock = kind != BackdropFeatureKind.CloudBank;
                _backdrop[index] = ToFoggedMesh(facets, $"Backdrop {kind} {variant}", isRock);
            }
            _ground = ToFoggedMesh(BackdropSilhouette.BuildGround(settings, palette), "Backdrop Ground", false);
        }

        /// <summary>Width over height of each cloud bank mesh, by variant (hand to the <see cref="BackdropLayout"/>).</summary>
        public IReadOnlyList<double> CloudBankAspects => _cloudBankAspects;

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

        private FacetMesh BuildSilhouette(BackdropFeatureKind kind, int variant, ulong seed, BackdropPalette palette) {
            if (kind != BackdropFeatureKind.CloudBank) return BackdropSilhouette.Build(kind, variant, seed, palette);

            ulong variantSeed = BackdropSilhouette.VariantSeed(seed, kind, variant);
            FacetMesh mesh = BackdropSilhouette.BuildCloudBank(variantSeed, palette, CloudBankShape.Default, out double aspect);
            _cloudBankAspects[variant] = aspect;
            return mesh;
        }

        // White vertex colours whose alpha is the foot fog weight at each corner's unit height (rock), or 0.
        private static Mesh ToFoggedMesh(FacetMesh facets, string name, bool hasFootFog) {
            Mesh mesh = FacetMeshUnity.ToMesh(facets, name);
            var colors = new Color32[facets.Positions.Count];
            for (int corner = 0; corner < colors.Length; corner++) {
                double weight = hasFootFog ? BackdropSilhouette.FootFogWeight(facets.Positions[corner].Y) : 0.0;
                colors[corner] = new Color32(255, 255, 255, (byte)Math.Round(255.0 * weight));
            }
            mesh.SetColors(colors);
            return mesh;
        }

        private static void DestroyMesh(Mesh mesh) {
            if (mesh == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
