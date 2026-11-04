using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The faceted silhouette meshes of the backdrop ring, one per (kind, variant), lofted from jittered rings and
    /// painted by height from a <see cref="BackdropPalette"/>.
    /// </summary>
    /// <remarks>
    /// Unit size: the footprint spans x in [−0.5, 0.5] (z narrower, by kind) and the mesh y in [0, 1], so an
    /// instance scales by (width, height, width). Rock is darkest at its foot; a cloud bank is its top strip
    /// above <see cref="CloudSplit"/> and its underside strip below.
    /// </remarks>
    public static class BackdropSilhouette {
        /// <summary>Height share below which a cloud bank is painted from its underside strip.</summary>
        public const double CloudSplit = 0.45;

        private const float RingJitter = 0.14f;
        private const float HeightJitter = 0.05f;
        private const ulong RingSalt = 0x52494E47UL;
        private const ulong HeightSalt = 0x48474854UL;

        // Rings as (height share, radius share) from the foot up, per kind.
        private static readonly double[][] MesaRings = { new[] { 0.0, 1.0 }, new[] { 0.1, 0.93 }, new[] { 0.22, 0.8 }, new[] { 0.3, 0.74 }, new[] { 0.88, 0.7 }, new[] { 0.96, 0.66 }, new[] { 1.0, 0.63 } };
        private static readonly double[][] ButteRings = { new[] { 0.0, 1.0 }, new[] { 0.12, 0.84 }, new[] { 0.28, 0.56 }, new[] { 0.36, 0.47 }, new[] { 0.9, 0.42 }, new[] { 1.0, 0.37 } };
        private static readonly double[][] RidgeRings = { new[] { 0.0, 1.0 }, new[] { 0.35, 0.76 }, new[] { 0.7, 0.44 }, new[] { 1.0, 0.12 } };
        private static readonly double[][] CloudRings = { new[] { 0.0, 0.66 }, new[] { 0.25, 0.92 }, new[] { 0.55, 1.0 }, new[] { 0.8, 0.78 }, new[] { 1.0, 0.36 } };

        /// <summary>The seed of (<paramref name="kind"/>, <paramref name="variant"/>) under <paramref name="seed"/>.</summary>
        public static ulong VariantSeed(ulong seed, BackdropFeatureKind kind, int variant) {
            return StableHash.Mix(StableHash.Mix(seed, (ulong)kind), (ulong)(uint)variant);
        }

        /// <summary>The unit silhouette of <paramref name="variant"/> of <paramref name="kind"/>.</summary>
        public static FacetMesh Build(BackdropFeatureKind kind, int variant, ulong seed, BackdropPalette palette) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (variant < 0 || variant >= BackdropSettings.VariantsPerKind) throw new ArgumentOutOfRangeException(nameof(variant), variant, "Outside the kind's variants.");

            ulong variantSeed = VariantSeed(seed, kind, variant);
            List<LoftRing> rings = BuildRings(ProfileOf(kind), SidesOf(kind), DepthOf(kind), variantSeed);
            bool isCloud = kind == BackdropFeatureKind.CloudBank;
            FacetMesh mesh = LoftMesh.Build(rings, true, isCloud);
            if (isCloud) {
                FacetUv.BandByHeight(mesh, (t) => CloudUv(palette, t), 0.0, 1.0);
            } else {
                SkyAtlasBand rock = palette.RockFor(variant);
                FacetUv.BandByHeight(mesh, (t) => rock.At(t), 0.0, 1.0);
            }

            return mesh;
        }

        private static System.Numerics.Vector2 CloudUv(BackdropPalette palette, double t) {
            double height = 1.0 - t;
            if (height >= CloudSplit) return palette.CloudTop.At((1.0 - height) / (1.0 - CloudSplit));

            return palette.CloudUnderside.At(1.0 - height / CloudSplit);
        }

        private static List<LoftRing> BuildRings(double[][] profile, int sides, float depth, ulong seed) {
            var rings = new List<LoftRing>(profile.Length);
            for (int index = 0; index < profile.Length; index++) {
                double height = ProfileHeight(profile, index, seed);
                float radius = 0.5f * (float)profile[index][1];
                ulong ringSeed = StableHash.Mix(StableHash.Mix(seed, RingSalt), (ulong)index);
                rings.Add(new LoftRing((float)height, LoftMesh.JitterPolygon(ringSeed, sides, radius, radius * depth, RingJitter, true)));
            }

            return rings;
        }

        // Foot and top stay at 0 and 1; inner rings move up or down a little per variant, never past a neighbour.
        private static double ProfileHeight(double[][] profile, int index, ulong seed) {
            double height = profile[index][0];
            if (index == 0 || index == profile.Length - 1) return height;

            double roll = LatticeHash.Unit(StableHash.Mix(seed, HeightSalt), index) * 2.0 - 1.0;
            double gap = Math.Min(height - profile[index - 1][0], profile[index + 1][0] - height);
            return height + roll * Math.Min(HeightJitter, gap * 0.4);
        }

        private static double[][] ProfileOf(BackdropFeatureKind kind) {
            switch (kind) {
                case BackdropFeatureKind.Mesa: return MesaRings;
                case BackdropFeatureKind.Butte: return ButteRings;
                case BackdropFeatureKind.Ridge: return RidgeRings;
                case BackdropFeatureKind.CloudBank: return CloudRings;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind.");
            }
        }

        private static int SidesOf(BackdropFeatureKind kind) {
            return kind == BackdropFeatureKind.Butte ? 9 : kind == BackdropFeatureKind.Mesa ? 10 : 12;
        }

        // Footprint depth (z) as a share of its width (x).
        private static float DepthOf(BackdropFeatureKind kind) {
            switch (kind) {
                case BackdropFeatureKind.Mesa: return 0.34f;
                case BackdropFeatureKind.Butte: return 0.6f;
                case BackdropFeatureKind.Ridge: return 0.16f;
                default: return 0.24f;
            }
        }
    }
}
