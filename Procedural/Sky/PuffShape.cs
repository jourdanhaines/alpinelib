using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The faceted mesh of one smoke puff variant: a round clump of overlapping level-0 blobs (<see cref="BlobCluster"/>)
    /// about a unit diameter, centred on the origin and painted by height from one atlas strip.
    /// </summary>
    /// <remarks>
    /// Variant v has <see cref="BlobsOf"/> blobs (5 to 8). Faces buried inside another blob's kernel (behind all its
    /// face planes) are dropped; while still over <see cref="FaceBudget"/> the satellites are pulled in a little and the
    /// clump rebuilt, as a last resort dropping satellites (never below 5 blobs). The puff only ever scales uniformly
    /// (never stretched after meshing). The shape depends on the variant and seed only; the strip it is painted from is
    /// the caller's (one mesh per shape and shade).
    /// </remarks>
    public static class PuffShape {
        /// <summary>Most triangles one puff mesh may have.</summary>
        public const int FaceBudget = 120;

        /// <summary>How far down its strip (from the light end) a puff's underside reaches.</summary>
        public const double UndersideReach = 0.5;

        private const int Level = 0;
        private const int MinBlobs = 5;
        private const int MaxTightening = 12;
        private const float Tightening = 0.92f;
        private const double CoreRadius = 0.34;
        private const double GoldenAngle = 2.399963229728653;
        private const float KernelMargin = 1e-4f;
        private const ulong ShapeSalt = 0x534D4F4B45UL;

        /// <summary>Blobs in variant <paramref name="variant"/>: 5, 5, 6, 6, 7, 8.</summary>
        public static int BlobsOf(int variant) {
            if (variant < 0 || variant >= PuffSettings.VariantCount) throw new ArgumentOutOfRangeException(nameof(variant), variant, "No such puff variant.");

            return variant == PuffSettings.VariantCount - 1 ? 8 : 5 + variant / 2;
        }

        /// <summary>The seed of puff variant <paramref name="variant"/> under <paramref name="seed"/>.</summary>
        public static ulong VariantSeed(ulong seed, int variant) {
            return StableHash.Mix(StableHash.Mix(seed, ShapeSalt), (ulong)(uint)variant);
        }

        /// <summary>Variant <paramref name="variant"/> under <paramref name="seed"/>, painted from <paramref name="band"/>.</summary>
        public static FacetMesh Build(int variant, ulong seed, SkyAtlasBand band) {
            ulong variantSeed = VariantSeed(seed, variant);
            List<BlobPuff> blobs = Layout(BlobsOf(variant), variantSeed);
            BlobSpec spec = Spec();
            FacetMesh mesh = BuildVisible(blobs, spec, variantSeed);
            for (int attempt = 0; attempt < MaxTightening && mesh.FaceCount > FaceBudget; attempt++) {
                blobs = Tighten(blobs);
                mesh = BuildVisible(blobs, spec, variantSeed);
            }
            while (mesh.FaceCount > FaceBudget && blobs.Count > MinBlobs) {
                blobs.RemoveAt(blobs.Count - 1);
                mesh = BuildVisible(blobs, spec, variantSeed);
            }
            if (mesh.FaceCount > FaceBudget) throw new InvalidOperationException($"Puff variant {variant} keeps {mesh.FaceCount} faces over the budget {FaceBudget}.");

            FacetMesh fitted = FitUnit(mesh);
            FacetBounds bounds = fitted.Bounds;
            FacetUv.BandByHeight(fitted, (shade) => band.At(shade * UndersideReach), bounds.Min.Y, bounds.Max.Y);
            return fitted;
        }

        /// <summary>The blobs of a puff: a core and satellites on a golden spiral leaning upward, all overlapping it.</summary>
        public static List<BlobPuff> Layout(int count, ulong variantSeed) {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Must be positive.");

            var random = new SplitMix64(variantSeed);
            var blobs = new List<BlobPuff>(count) { new BlobPuff(Vector3.Zero, Round(CoreRadius * (0.95 + 0.1 * random.NextDouble()))) };
            int satellites = count - 1;
            double crowding = Math.Min(1.0, Math.Sqrt(5.0 / Math.Max(1, satellites)));
            double spin = random.NextDouble() * 2.0 * Math.PI;
            for (int index = 0; index < satellites; index++) {
                double height = 0.75 - 1.3 * (index + 0.5) / satellites;
                double ring = Math.Sqrt(Math.Max(0.0, 1.0 - height * height));
                double angle = spin + index * GoldenAngle;
                double distance = 0.22 + 0.08 * random.NextDouble();
                double radius = (0.2 + 0.08 * random.NextDouble()) * crowding;
                var centre = new Vector3((float)(Math.Cos(angle) * ring * distance), (float)(height * distance), (float)(Math.Sin(angle) * ring * distance));
                blobs.Add(new BlobPuff(centre, Round(radius)));
            }

            return blobs;
        }

        private static BlobSpec Spec() {
            return new BlobSpec {
                Subdivisions = Level,
                Radii = Vector3.One,
                NoiseAmplitude = 0.05f,
                NoiseFrequency = 1.5f,
                FacetJitter = 0.02f,
                FlattenBelowY = null,
                Squash = 0f,
            };
        }

        private static Vector3 Round(double radius) {
            return new Vector3((float)radius, (float)radius, (float)radius);
        }

        // Satellites pulled toward the core so more of their faces bury; the core stays.
        private static List<BlobPuff> Tighten(List<BlobPuff> blobs) {
            var tightened = new List<BlobPuff>(blobs.Count) { blobs[0] };
            for (int index = 1; index < blobs.Count; index++) tightened.Add(new BlobPuff(blobs[index].Centre * Tightening, blobs[index].Radii));
            return tightened;
        }

        // The cluster with faces collapsed or buried in another blob dropped: a face is buried when its three corners lie
        // behind every face plane of that blob (its kernel, convex and inside its real solid, so no hole can open).
        private static FacetMesh BuildVisible(List<BlobPuff> blobs, in BlobSpec spec, ulong variantSeed) {
            FacetMesh cluster = BlobCluster.Build(blobs, spec, variantSeed);
            int perBlob = cluster.FaceCount / blobs.Count;
            Plane[][] kernels = new Plane[blobs.Count][];
            for (int index = 0; index < blobs.Count; index++) kernels[index] = FacePlanes(cluster, index * perBlob, perBlob);

            var visible = new FacetMesh(cluster.FaceCount);
            List<Vector3> corners = cluster.Positions;
            for (int face = 0; face < cluster.FaceCount; face++) {
                Vector3 a = corners[face * 3];
                Vector3 b = corners[face * 3 + 1];
                Vector3 c = corners[face * 3 + 2];
                if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-12f) continue;
                if (IsBuried(a, b, c, kernels, face / perBlob)) continue;

                visible.AddFace(a, b, c);
            }

            return visible;
        }

        // Outward unit plane of every non-degenerate face of one blob.
        private static Plane[] FacePlanes(FacetMesh cluster, int firstFace, int faceCount) {
            var planes = new List<Plane>(faceCount);
            List<Vector3> corners = cluster.Positions;
            for (int face = firstFace; face < firstFace + faceCount; face++) {
                Vector3 a = corners[face * 3];
                Vector3 normal = Vector3.Cross(corners[face * 3 + 1] - a, corners[face * 3 + 2] - a);
                float length = normal.Length();
                if (length < 1e-12f) continue;

                Vector3 unit = normal / length;
                planes.Add(new Plane(unit, -Vector3.Dot(unit, a)));
            }

            return planes.ToArray();
        }

        private static bool IsBuried(Vector3 a, Vector3 b, Vector3 c, Plane[][] kernels, int owner) {
            for (int index = 0; index < kernels.Length; index++) {
                if (index == owner) continue;
                if (IsInside(a, kernels[index]) && IsInside(b, kernels[index]) && IsInside(c, kernels[index])) return true;
            }

            return false;
        }

        private static bool IsInside(Vector3 point, Plane[] planes) {
            foreach (Plane plane in planes) {
                if (Plane.DotCoordinate(plane, point) > -KernelMargin) return false;
            }

            return true;
        }

        // Centred on its box and scaled as a whole until its widest span is 1.
        private static FacetMesh FitUnit(FacetMesh mesh) {
            FacetBounds bounds = mesh.Bounds;
            Vector3 size = bounds.Size;
            float scale = 1f / Math.Max(size.X, Math.Max(size.Y, size.Z));
            Matrix4x4 transform = Matrix4x4.CreateTranslation(-bounds.Centre) * Matrix4x4.CreateScale(scale);
            var fitted = new FacetMesh(mesh.FaceCount);
            fitted.Append(mesh, transform);
            return fitted;
        }
    }
}
