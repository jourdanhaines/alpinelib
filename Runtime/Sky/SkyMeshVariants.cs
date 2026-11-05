using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// The sky's cached meshes: one unit silhouette per backdrop (kind, variant), the ground annulus under the
    /// ring and one drifting cloud per (class, variant), built once from seeds and palettes and shared by every
    /// camera. <see cref="Dispose"/> destroys them.
    /// </summary>
    /// <remarks>
    /// Backdrop and clouds build separately (<see cref="BuildBackdrop"/>, <see cref="BuildClouds"/>), so a bad
    /// setting for one never costs the other. Records each cloud bank's own width over height
    /// (<see cref="CloudBankAspects"/>) for the layout to size it by, and each cloud's puffs for its ground shadow.
    /// Vertex alpha is the fog blend weight <c>_FOG_SCALE_BLEND</c> reads: rock's foot fog
    /// (<see cref="BackdropSilhouette.FootFogWeight"/>), 0 on cloud banks and the annulus.
    /// </remarks>
    public sealed class SkyMeshVariants : IDisposable {
        private readonly Mesh[] _backdrop = new Mesh[BackdropSettings.MeshCount];
        private readonly double[] _cloudBankAspects = new double[BackdropSettings.VariantsPerKind];
        private readonly Mesh[] _clouds = new Mesh[CloudFieldSettings.MeshCount];
        private readonly BlobPuff[][] _cloudPuffs = new BlobPuff[CloudFieldSettings.MeshCount][];
        private Mesh _ground;

        /// <summary>No meshes yet: build them with <see cref="BuildBackdrop"/> and <see cref="BuildClouds"/>.</summary>
        public SkyMeshVariants() {
        }

        /// <summary>Builds the backdrop at once (see <see cref="BuildBackdrop"/>). Main thread.</summary>
        public SkyMeshVariants(ulong seed, BackdropPalette palette, BackdropSettings settings) {
            BuildBackdrop(seed, palette, settings);
        }

        /// <summary>True once the backdrop silhouettes are built.</summary>
        public bool HasBackdrop => _ground != null;

        /// <summary>True once the cloud meshes are built.</summary>
        public bool HasClouds => _clouds[0] != null;

        /// <summary>Builds every backdrop silhouette of <paramref name="seed"/> painted from <paramref name="palette"/>, and the annulus of <paramref name="settings"/>. Main thread.</summary>
        public void BuildBackdrop(ulong seed, BackdropPalette palette, BackdropSettings settings) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (IsDisposed) throw new ObjectDisposedException(nameof(SkyMeshVariants));

            DestroyBackdrop();
            for (int index = 0; index < _backdrop.Length; index++) {
                var kind = (BackdropFeatureKind)(index / BackdropSettings.VariantsPerKind);
                int variant = index % BackdropSettings.VariantsPerKind;
                FacetMesh facets = BuildSilhouette(kind, variant, seed, palette);
                bool isRock = kind != BackdropFeatureKind.CloudBank;
                _backdrop[index] = ToFoggedMesh(facets, $"Backdrop {kind} {variant}", isRock);
            }
            _ground = ToFoggedMesh(BackdropSilhouette.BuildGround(settings, palette), "Backdrop Ground", false);
        }

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
                FacetMesh facets = CloudHeap.Build(classSettings, diameter, CloudHeap.VariantSeed(seed, cloudClass, variant), palette, out BlobPuff[] puffs);
                _clouds[index] = FacetMeshUnity.ToMesh(facets, $"Cloud {cloudClass} {variant}");
                _cloudPuffs[index] = puffs;
            }
        }

        /// <summary>The cloud mesh of <see cref="CloudInstance.MeshIndex"/> <paramref name="meshIndex"/>, or null before <see cref="BuildClouds"/>.</summary>
        public Mesh Cloud(int meshIndex) {
            return _clouds[meshIndex];
        }

        /// <summary>The puffs cloud mesh <paramref name="meshIndex"/> was heaped from (mesh space, metres), or null.</summary>
        public IReadOnlyList<BlobPuff> CloudPuffs(int meshIndex) {
            return _cloudPuffs[meshIndex];
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
            DestroyBackdrop();
            DestroyClouds();
        }

        private void DestroyBackdrop() {
            for (int index = 0; index < _backdrop.Length; index++) {
                DestroyMesh(_backdrop[index]);
                _backdrop[index] = null;
            }
            DestroyMesh(_ground);
            _ground = null;
        }

        private void DestroyClouds() {
            for (int index = 0; index < _clouds.Length; index++) {
                DestroyMesh(_clouds[index]);
                _clouds[index] = null;
                _cloudPuffs[index] = null;
            }
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
