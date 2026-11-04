using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The faceted silhouette meshes of the backdrop ring, one per (kind, variant), lofted from jittered rings and
    /// painted by height from a <see cref="BackdropPalette"/>.
    /// </summary>
    /// <remarks>
    /// Unit size: the footprint spans x in [−0.5, 0.5] (z narrower, by kind) and the mesh y in [0, 1], so an
    /// instance scales by (width, height, width). Rock is darkest at its foot; a cloud bank (a <see cref="CloudBankShape"/>
    /// heap of rounded blobs) is its top strip above <see cref="CloudSplit"/> and the light half of its underside
    /// strip below, so its belly melts toward the haze.
    /// </remarks>
    public static class BackdropSilhouette {
        /// <summary>Height share below which a cloud bank is painted from its underside strip.</summary>
        public const double CloudSplit = 0.4;

        /// <summary>How far down its strip (from the light end) a cloud bank's underside reaches.</summary>
        public const double UndersideReach = 0.5;

        /// <summary>Segments around the ground annulus.</summary>
        public const int GroundSegments = 72;

        private const float RingJitter = 0.14f;
        private const float HeightJitter = 0.05f;
        private const int FineFaces = 80;
        private const int CoarseFaces = 20;
        private const ulong RingSalt = 0x52494E47UL;
        private const ulong HeightSalt = 0x48474854UL;
        private const ulong CloudSalt = 0x434C4F55UL;

        // Rings as (height share, radius share) from the foot up, per kind.
        private static readonly double[][] MesaRings = { new[] { 0.0, 1.0 }, new[] { 0.1, 0.93 }, new[] { 0.22, 0.8 }, new[] { 0.3, 0.74 }, new[] { 0.88, 0.7 }, new[] { 0.96, 0.66 }, new[] { 1.0, 0.63 } };
        private static readonly double[][] ButteRings = { new[] { 0.0, 1.0 }, new[] { 0.12, 0.84 }, new[] { 0.28, 0.56 }, new[] { 0.36, 0.47 }, new[] { 0.9, 0.42 }, new[] { 1.0, 0.37 } };
        private static readonly double[][] RidgeRings = { new[] { 0.0, 1.0 }, new[] { 0.3, 0.84 }, new[] { 0.62, 0.6 }, new[] { 0.85, 0.42 }, new[] { 1.0, 0.3 } };

        /// <summary>The seed of (<paramref name="kind"/>, <paramref name="variant"/>) under <paramref name="seed"/>.</summary>
        public static ulong VariantSeed(ulong seed, BackdropFeatureKind kind, int variant) {
            return StableHash.Mix(StableHash.Mix(seed, (ulong)kind), (ulong)(uint)variant);
        }

        /// <summary>The unit silhouette of <paramref name="variant"/> of <paramref name="kind"/> (cloud banks in the <see cref="CloudBankShape.Default"/> shape).</summary>
        public static FacetMesh Build(BackdropFeatureKind kind, int variant, ulong seed, BackdropPalette palette) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (variant < 0 || variant >= BackdropSettings.VariantsPerKind) throw new ArgumentOutOfRangeException(nameof(variant), variant, "Outside the kind's variants.");

            ulong variantSeed = VariantSeed(seed, kind, variant);
            if (kind == BackdropFeatureKind.CloudBank) return BuildCloudBank(variantSeed, palette, CloudBankShape.Default);

            FacetMesh mesh = LoftMesh.Build(BuildRings(ProfileOf(kind), SidesOf(kind), DepthOf(kind), variantSeed), true, false);
            SkyAtlasBand rock = palette.RockFor(variant);
            FacetUv.BandByHeight(mesh, (height) => rock.At(height), 0.0, 1.0);
            return mesh;
        }

        /// <summary>A unit cloud bank of <paramref name="shape"/> from <paramref name="variantSeed"/> (see <see cref="VariantSeed"/>).</summary>
        public static FacetMesh BuildCloudBank(ulong variantSeed, BackdropPalette palette, CloudBankShape shape) {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            shape.Validate();
            List<BlobPuff> puffs = CloudPuffs(variantSeed, shape);
            int fine = Math.Min(puffs.Count, (shape.FaceBudget - CoarseFaces * puffs.Count) / (FineFaces - CoarseFaces));
            int[] bySize = RankBySize(puffs);
            var heap = new FacetMesh();
            for (int rank = 0; rank < bySize.Length; rank++) {
                BlobPuff puff = puffs[bySize[rank]];
                BlobSpec spec = CloudSpec(rank < fine ? 1 : 0, puff);
                BlobMesh.AppendTo(heap, spec, StableHash.Mix(variantSeed, (ulong)bySize[rank]), puff.Centre);
            }

            FacetMesh mesh = ToUnit(heap);
            FacetUv.BandByHeight(mesh, (height) => CloudUv(palette, height), 0.0, 1.0);
            return mesh;
        }

        /// <summary>The flat annulus laid under the ring, in metres about its centre, facing up, painted mid <see cref="BackdropPalette.Ground"/>.</summary>
        public static FacetMesh BuildGround(BackdropSettings settings, BackdropPalette palette) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            var mesh = new FacetMesh(GroundSegments * 2);
            Vector2 uv = palette.Ground.At(0.5);
            float inner = (float)settings.GroundInnerRadius;
            float outer = (float)settings.GroundOuterRadius;
            for (int segment = 0; segment < GroundSegments; segment++) {
                double start = segment * 2.0 * Math.PI / GroundSegments;
                double end = (segment + 1) * 2.0 * Math.PI / GroundSegments;
                Vector3 innerStart = OnCircle(inner, start);
                Vector3 innerEnd = OnCircle(inner, end);
                Vector3 outerStart = OnCircle(outer, start);
                Vector3 outerEnd = OnCircle(outer, end);
                mesh.AddFace(innerStart, outerEnd, outerStart, uv);
                mesh.AddFace(innerStart, innerEnd, outerEnd, uv);
            }

            return mesh;
        }

        private static Vector3 OnCircle(float radius, double angle) {
            return new Vector3(radius * (float)Math.Cos(angle), 0f, radius * (float)Math.Sin(angle));
        }

        // A row of puffs biggest about the leaning peak and thin toward both ends, then crown puffs heaped on the middle.
        private static List<BlobPuff> CloudPuffs(ulong seed, CloudBankShape shape) {
            var random = new SplitMix64(StableHash.Mix(seed, CloudSalt));
            var puffs = new List<BlobPuff>(shape.RowPuffs + shape.CrownPuffs);
            double half = shape.Aspect * 0.5;
            for (int index = 0; index < shape.RowPuffs; index++) {
                double along = -1.0 + 2.0 * (index + 0.5) / shape.RowPuffs + (random.NextDouble() - 0.5) / shape.RowPuffs;
                double envelope = Envelope(along, shape.Lean);
                double radius = Lerp(shape.MinRadius, shape.MaxRadius, envelope * (0.5 + 0.5 * random.NextDouble()));
                double stretch = 1.25 + 0.7 * (1.0 - envelope);
                var radii = new Vector3((float)(radius * stretch), (float)(radius * shape.Rise), (float)(radius * 0.9));
                double x = along * (half - radii.X * 0.55);
                puffs.Add(new BlobPuff(new Vector3((float)x, radii.Y * 0.25f, 0f), radii));
            }

            int rowCount = puffs.Count;
            for (int index = 0; index < shape.CrownPuffs; index++) {
                double along = shape.Lean + (index - (shape.CrownPuffs - 1) * 0.5) * 0.3 + (random.NextDouble() - 0.5) * 0.12;
                double radius = Lerp(shape.MinRadius, shape.MaxRadius * 0.75, 0.5 + 0.5 * random.NextDouble());
                var radii = new Vector3((float)(radius * 1.1), (float)(radius * shape.Rise), (float)(radius * 0.8));
                double x = along * half * 0.6;
                double top = RowTop(puffs, rowCount, x);
                puffs.Add(new BlobPuff(new Vector3((float)x, (float)(top - radii.Y * 0.35), (float)((random.NextDouble() - 0.5) * radius * 0.4)), radii));
            }

            return puffs;
        }

        // 1 at the peak, 0 at either end of the row, with the peak shifted by lean.
        private static double Envelope(double along, double lean) {
            double peak = Math.Max(-0.6, Math.Min(0.6, lean));
            double reach = along >= peak ? 1.0 - peak : 1.0 + peak;
            double distance = (along - peak) / reach;
            return Math.Max(0.0, 1.0 - distance * distance);
        }

        private static double RowTop(List<BlobPuff> puffs, int rowCount, double x) {
            double top = 0.0;
            for (int index = 0; index < rowCount; index++) {
                BlobPuff puff = puffs[index];
                double across = (x - puff.Centre.X) / puff.Radii.X;
                if (Math.Abs(across) >= 1.0) continue;

                top = Math.Max(top, puff.Centre.Y + puff.Radii.Y * Math.Sqrt(1.0 - across * across));
            }

            return top;
        }

        private static int[] RankBySize(List<BlobPuff> puffs) {
            var order = new int[puffs.Count];
            for (int index = 0; index < order.Length; index++) order[index] = index;

            Array.Sort(order, (left, right) => {
                int bySize = puffs[right].Radii.Y.CompareTo(puffs[left].Radii.Y);
                return bySize != 0 ? bySize : left.CompareTo(right);
            });
            return order;
        }

        // Round, soft-bellied blobs sharing the bank's flat base at y 0.
        private static BlobSpec CloudSpec(int subdivisions, BlobPuff puff) {
            return new BlobSpec {
                Subdivisions = subdivisions,
                Radii = puff.Radii,
                NoiseAmplitude = 0.1f,
                NoiseFrequency = 1.3f,
                FacetJitter = 0.03f,
                FlattenBelowY = -puff.Centre.Y,
                Squash = 0.45f,
            };
        }

        // Centred on x and z, spanning x in [−0.5, 0.5] (z by the same scale) and y in [0, 1].
        private static FacetMesh ToUnit(FacetMesh heap) {
            FacetBounds bounds = heap.Bounds;
            float across = 1f / (bounds.Max.X - bounds.Min.X);
            float up = 1f / bounds.Max.Y;
            var place = Matrix4x4.CreateTranslation(-bounds.Centre.X, 0f, -bounds.Centre.Z) * Matrix4x4.CreateScale(across, up, across);
            var mesh = new FacetMesh(heap.FaceCount);
            mesh.Append(heap, place);
            return mesh;
        }

        private static Vector2 CloudUv(BackdropPalette palette, double t) {
            double height = 1.0 - t;
            if (height >= CloudSplit) return palette.CloudTop.At((1.0 - height) / (1.0 - CloudSplit));

            return palette.CloudUnderside.At(UndersideReach * (1.0 - height / CloudSplit));
        }

        private static double Lerp(double low, double high, double t) {
            return low + (high - low) * t;
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
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind.");
            }
        }

        private static int SidesOf(BackdropFeatureKind kind) {
            switch (kind) {
                case BackdropFeatureKind.Mesa: return 10;
                case BackdropFeatureKind.Butte: return 9;
                default: return 12;
            }
        }

        // Footprint depth (z) as a share of its width (x).
        private static float DepthOf(BackdropFeatureKind kind) {
            switch (kind) {
                case BackdropFeatureKind.Mesa: return 0.34f;
                case BackdropFeatureKind.Butte: return 0.6f;
                default: return 0.35f;
            }
        }
    }
}
