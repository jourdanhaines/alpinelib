using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The faceted mesh of one drifting cloud: many overlapping round puffs on one flat base, biggest in the middle,
    /// with a few crown puffs heaped on top, built in metres at the size it is drawn (instances only scale it
    /// uniformly, so its puffs stay round) and painted from a <see cref="CloudPalette"/> by height.
    /// </summary>
    /// <remarks>
    /// Every puff starts as a round level-1 blob; faces buried inside another puff (and collapsed ones) are dropped,
    /// and only if the mesh is still over its class's budget are the smallest puffs coarsened, one at a time. The
    /// upper <see cref="TopShare"/> of its height is painted from the top strip, the belly from the light end of the
    /// underside strip, so it reads soft from below. The base lies at y 0, centred on x and z.
    /// </remarks>
    public static class CloudHeap {
        /// <summary>Upper share of the height painted from the top strip.</summary>
        public const double TopShare = 0.65;

        /// <summary>How far down the underside strip (from its light end) the base reaches.</summary>
        public const double UndersideReach = 0.15;

        /// <summary>Share of a puff's radius inside which another puff's faces count as buried (under its least noisy radius).</summary>
        public const double BuriedShare = 0.9;

        private const int FineLevel = 1;
        private const int CoarseLevel = 0;
        private const double GoldenAngle = 2.399963229728653;
        private const double CrownShare = 0.3;
        private const double BaseEpsilon = 1e-3;
        private const double SquashBelow = 0.45;
        private const ulong PuffSalt = 0x50554646UL;
        private const ulong ShapeSalt = 0x53484150UL;

        /// <summary>The seed of mesh <paramref name="variant"/> of <paramref name="cloudClass"/> under <paramref name="seed"/>.</summary>
        public static ulong VariantSeed(ulong seed, CloudClass cloudClass, int variant) {
            return StableHash.Mix(StableHash.Mix(seed, (ulong)cloudClass), (ulong)(uint)variant);
        }

        /// <summary>
        /// Footprint diameter of mesh <paramref name="variant"/> of a class, metres: spread across the range that
        /// keeps every instance scale of <paramref name="field"/> inside the class's diameter range.
        /// </summary>
        public static double TargetDiameter(CloudClassSettings cloudClass, CloudFieldSettings field, int variant) {
            if (cloudClass == null) throw new ArgumentNullException(nameof(cloudClass));
            if (field == null) throw new ArgumentNullException(nameof(field));

            double low = cloudClass.MinDiameter / field.MinScale;
            double high = cloudClass.MaxDiameter / field.MaxScale;
            double t = CloudFieldSettings.VariantsPerClass > 1 ? variant / (double)(CloudFieldSettings.VariantsPerClass - 1) : 0.5;
            return low + (high - low) * t;
        }

        /// <summary>
        /// Mesh <paramref name="variant"/> of <paramref name="cloudClass"/> under <paramref name="seed"/>, about
        /// <paramref name="diameter"/> metres across, with <paramref name="puffs"/> the puffs it was heaped from
        /// (centres and radii in metres, for footprints).
        /// </summary>
        public static FacetMesh Build(CloudClassSettings cloudClass, double diameter, ulong variantSeed, CloudPalette palette, out BlobPuff[] puffs) {
            if (cloudClass == null) throw new ArgumentNullException(nameof(cloudClass));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (!(diameter > 0.0)) throw new ArgumentOutOfRangeException(nameof(diameter), diameter, "Must be positive.");

            cloudClass.Validate();
            List<BlobPuff> layout = Puffs(cloudClass, diameter, variantSeed);
            puffs = layout.ToArray();
            int[] levels = new int[layout.Count];
            for (int index = 0; index < levels.Length; index++) levels[index] = FineLevel;

            int[] bySize = RankBySize(layout);
            FacetMesh mesh = BuildVisible(layout, levels, variantSeed);
            for (int rank = bySize.Length - 1; rank >= 0 && mesh.FaceCount > cloudClass.FaceBudget; rank--) {
                levels[bySize[rank]] = CoarseLevel;
                mesh = BuildVisible(layout, levels, variantSeed);
            }

            float top = mesh.Bounds.Max.Y;
            FacetUv.BandByHeight(mesh, (height) => CloudUv(palette, height), 0.0, top);
            return mesh;
        }

        /// <summary>
        /// The puffs of a cloud about <paramref name="diameter"/> across: base puffs on a golden-angle spiral (largest
        /// in the middle, every one inside the footprint), then crown puffs set on the base's top near the middle.
        /// </summary>
        public static List<BlobPuff> Puffs(CloudClassSettings cloudClass, double diameter, ulong variantSeed) {
            if (cloudClass == null) throw new ArgumentNullException(nameof(cloudClass));

            var random = new SplitMix64(StableHash.Mix(variantSeed, ShapeSalt));
            int count = random.NextInt(cloudClass.MinPuffs, cloudClass.MaxPuffs + 1);
            int crown = Math.Max(1, (int)Math.Round(count * CrownShare));
            int baseCount = count - crown;
            double footprint = diameter * 0.5;
            double largest = footprint * 1.45 / Math.Sqrt(baseCount + 1.0);
            double spin = random.NextDouble() * 2.0 * Math.PI;
            double oval = 0.75 + 0.2 * random.NextDouble();
            var puffs = new List<BlobPuff>(count);
            for (int index = 0; index < baseCount; index++) puffs.Add(BasePuff(index, baseCount, footprint, largest, spin, oval, random));

            for (int index = 0; index < crown; index++) puffs.Add(CrownPuff(puffs, baseCount, footprint, largest, random));
            return FitFootprint(puffs, diameter);
        }

        // Centred and scaled as a whole (never per axis, so puffs stay round) until the wider footprint span is the diameter.
        private static List<BlobPuff> FitFootprint(List<BlobPuff> puffs, double diameter) {
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (BlobPuff puff in puffs) {
                minX = Math.Min(minX, puff.Centre.X - puff.Radii.X);
                maxX = Math.Max(maxX, puff.Centre.X + puff.Radii.X);
                minZ = Math.Min(minZ, puff.Centre.Z - puff.Radii.Z);
                maxZ = Math.Max(maxZ, puff.Centre.Z + puff.Radii.Z);
            }

            float scale = (float)(diameter / Math.Max(maxX - minX, maxZ - minZ));
            var centre = new Vector3((float)(0.5 * (minX + maxX)), 0f, (float)(0.5 * (minZ + maxZ)));
            var fitted = new List<BlobPuff>(puffs.Count);
            foreach (BlobPuff puff in puffs) fitted.Add(new BlobPuff((puff.Centre - centre) * scale, puff.Radii * scale));
            return fitted;
        }

        // Out on the spiral the puffs shrink, so the heap tapers to its rim; all sit just above the shared base.
        private static BlobPuff BasePuff(int index, int baseCount, double footprint, double largest, double spin, double oval, SplitMix64 random) {
            double reach = Math.Sqrt((index + 0.5) / baseCount);
            double radius = largest * (1.0 - 0.5 * reach) * (0.8 + 0.3 * random.NextDouble());
            double rise = 0.95 + 0.2 * random.NextDouble();
            double angle = spin + index * GoldenAngle;
            double distance = reach * Math.Max(0.0, footprint - radius);
            double x = Math.Cos(angle) * distance;
            double z = Math.Sin(angle) * distance * oval;
            var radii = new Vector3((float)radius, (float)(radius * rise), (float)radius);
            return new BlobPuff(new Vector3((float)x, (float)(radius * 0.15), (float)z), radii);
        }

        // Round puffs heaped on the middle of the base, sunk a third into it, so the cloud domes up.
        private static BlobPuff CrownPuff(List<BlobPuff> puffs, int baseCount, double footprint, double largest, SplitMix64 random) {
            double radius = largest * (0.55 + 0.25 * random.NextDouble());
            double angle = random.NextDouble() * 2.0 * Math.PI;
            double distance = footprint * 0.3 * Math.Sqrt(random.NextDouble());
            double x = Math.Cos(angle) * distance;
            double z = Math.Sin(angle) * distance;
            double rise = 1.0 + 0.15 * random.NextDouble();
            double top = TopAt(puffs, baseCount, x, z);
            var radii = new Vector3((float)radius, (float)(radius * rise), (float)radius);
            return new BlobPuff(new Vector3((float)x, (float)(top - radii.Y * 0.35), (float)z), radii);
        }

        private static double TopAt(List<BlobPuff> puffs, int baseCount, double x, double z) {
            double top = 0.0;
            for (int index = 0; index < baseCount; index++) {
                BlobPuff puff = puffs[index];
                double acrossX = (x - puff.Centre.X) / puff.Radii.X;
                double acrossZ = (z - puff.Centre.Z) / puff.Radii.Z;
                double across = acrossX * acrossX + acrossZ * acrossZ;
                if (across >= 1.0) continue;

                top = Math.Max(top, puff.Centre.Y + puff.Radii.Y * Math.Sqrt(1.0 - across));
            }

            return top;
        }

        // Every puff at its level, keeping only faces not collapsed and not buried inside another puff.
        private static FacetMesh BuildVisible(List<BlobPuff> puffs, int[] levels, ulong variantSeed) {
            var mesh = new FacetMesh();
            var blob = new FacetMesh(Icosphere.FaceCount(FineLevel));
            for (int index = 0; index < puffs.Count; index++) {
                blob.Clear();
                BlobPuff puff = puffs[index];
                BlobMesh.AppendTo(blob, PuffSpec(levels[index], puff), StableHash.Mix(StableHash.Mix(variantSeed, PuffSalt), (ulong)index), puff.Centre);
                AppendVisible(mesh, blob, puffs, index);
            }

            return mesh;
        }

        private static void AppendVisible(FacetMesh mesh, FacetMesh blob, List<BlobPuff> puffs, int owner) {
            List<Vector3> corners = blob.Positions;
            for (int face = 0; face < blob.FaceCount; face++) {
                Vector3 a = corners[face * 3];
                Vector3 b = corners[face * 3 + 1];
                Vector3 c = corners[face * 3 + 2];
                if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) continue;
                if (IsBuried(a, b, c, puffs, owner)) continue;

                mesh.AddFace(a, b, c);
            }
        }

        // All three corners strictly inside one other puff's least-noisy solid (a convex shell), never on the base.
        private static bool IsBuried(Vector3 a, Vector3 b, Vector3 c, List<BlobPuff> puffs, int owner) {
            for (int index = 0; index < puffs.Count; index++) {
                if (index == owner) continue;
                if (IsInside(a, puffs[index]) && IsInside(b, puffs[index]) && IsInside(c, puffs[index])) return true;
            }

            return false;
        }

        private static bool IsInside(Vector3 point, BlobPuff puff) {
            if (point.Y <= BaseEpsilon) return false;

            double below = point.Y < puff.Centre.Y ? 1.0 - SquashBelow : 1.0;
            double x = (point.X - puff.Centre.X) / puff.Radii.X;
            double y = (point.Y - puff.Centre.Y) / (puff.Radii.Y * below);
            double z = (point.Z - puff.Centre.Z) / puff.Radii.Z;
            return x * x + y * y + z * z < BuriedShare * BuriedShare;
        }

        // Puff indices, largest first, ties by index: an insertion sort, so every runtime orders them alike.
        private static int[] RankBySize(List<BlobPuff> puffs) {
            var order = new int[puffs.Count];
            for (int index = 0; index < order.Length; index++) {
                int slot = index;
                while (slot > 0 && IsLarger(puffs, index, order[slot - 1])) {
                    order[slot] = order[slot - 1];
                    slot--;
                }
                order[slot] = index;
            }

            return order;
        }

        private static bool IsLarger(List<BlobPuff> puffs, int left, int right) {
            float leftRadius = puffs[left].Radii.X;
            float rightRadius = puffs[right].Radii.X;
            return leftRadius > rightRadius || (leftRadius == rightRadius && left < right);
        }

        // Round, soft-bellied blobs sharing the cloud's flat base at y 0.
        private static BlobSpec PuffSpec(int level, BlobPuff puff) {
            return new BlobSpec {
                Subdivisions = level,
                Radii = puff.Radii,
                NoiseAmplitude = 0.05f,
                NoiseFrequency = 1.3f,
                FacetJitter = 0.02f,
                FlattenBelowY = -puff.Centre.Y,
                Squash = (float)SquashBelow,
            };
        }

        private static Vector2 CloudUv(CloudPalette palette, double t) {
            double height = 1.0 - t;
            double split = 1.0 - TopShare;
            if (height >= split) return palette.Top.At((1.0 - height) / TopShare);

            return palette.Underside.At(UndersideReach * (1.0 - height / split));
        }
    }
}
