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
    /// Every puff starts as a round level-1 blob; faces buried inside another puff's real faceted solid (and collapsed
    /// ones) are dropped, so no hole opens where puffs meet, and only if the mesh is still over its class's budget are
    /// the smallest puffs coarsened, one at a time. The
    /// upper <see cref="TopShare"/> of its height is painted from the top strip, the belly from the light end of the
    /// underside strip, so it reads soft from below. The base lies at y 0, centred on x and z.
    /// </remarks>
    public static class CloudHeap {
        /// <summary>Upper share of the height painted from the top strip.</summary>
        public const double TopShare = 0.65;

        /// <summary>How far down the underside strip (from its light end) the base reaches.</summary>
        public const double UndersideReach = 0.15;

        /// <summary>
        /// Margin under a puff's inner share (the radius share its faceted, noisy surface never dips below) inside which
        /// another puff's faces count as buried.
        /// </summary>
        public const double BuriedMargin = 0.98;

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
        /// Bound on a heap's horizontal reach from its centre over its target diameter, any heading: what the field
        /// spaces clouds by (the footprint is fitted to the diameter on its wider axis, puffs keep inside a disc of it).
        /// </summary>
        public const double ReachShare = 0.65;

        /// <summary>
        /// Footprint diameter of mesh <paramref name="variant"/> of a class, metres: spread geometrically across the
        /// class's diameter range (instances scale it on top).
        /// </summary>
        public static double TargetDiameter(CloudClassSettings cloudClass, int variant) {
            if (cloudClass == null) throw new ArgumentNullException(nameof(cloudClass));

            double share = CloudFieldSettings.VariantsPerClass > 1 ? variant / (double)(CloudFieldSettings.VariantsPerClass - 1) : 0.5;
            return cloudClass.MinDiameter * Math.Pow(cloudClass.MaxDiameter / cloudClass.MinDiameter, share);
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
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
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
            double centreX = Math.Cos(angle) * distance;
            double centreZ = Math.Sin(angle) * distance * oval;
            var radii = new Vector3((float)radius, (float)(radius * rise), (float)radius);
            return new BlobPuff(new Vector3((float)centreX, (float)(radius * 0.15), (float)centreZ), radii);
        }

        // Round puffs heaped on the middle of the base, sunk a third into it, so the cloud domes up.
        private static BlobPuff CrownPuff(List<BlobPuff> puffs, int baseCount, double footprint, double largest, SplitMix64 random) {
            double radius = largest * (0.55 + 0.25 * random.NextDouble());
            double angle = random.NextDouble() * 2.0 * Math.PI;
            double distance = footprint * 0.3 * Math.Sqrt(random.NextDouble());
            double centreX = Math.Cos(angle) * distance;
            double centreZ = Math.Sin(angle) * distance;
            double rise = 1.0 + 0.15 * random.NextDouble();
            double top = TopAt(puffs, baseCount, centreX, centreZ);
            var radii = new Vector3((float)radius, (float)(radius * rise), (float)radius);
            return new BlobPuff(new Vector3((float)centreX, (float)(top - radii.Y * 0.35), (float)centreZ), radii);
        }

        private static double TopAt(List<BlobPuff> puffs, int baseCount, double pointX, double pointZ) {
            double top = 0.0;
            for (int index = 0; index < baseCount; index++) {
                BlobPuff puff = puffs[index];
                double acrossX = (pointX - puff.Centre.X) / puff.Radii.X;
                double acrossZ = (pointZ - puff.Centre.Z) / puff.Radii.Z;
                double across = acrossX * acrossX + acrossZ * acrossZ;
                if (across >= 1.0) continue;

                top = Math.Max(top, puff.Centre.Y + puff.Radii.Y * Math.Sqrt(1.0 - across));
            }

            return top;
        }

        // Every puff at its level, keeping only faces not collapsed and not buried inside another puff's real solid.
        private static FacetMesh BuildVisible(List<BlobPuff> puffs, int[] levels, ulong variantSeed) {
            var blobs = new FacetMesh[puffs.Count];
            var shares = new double[puffs.Count];
            for (int index = 0; index < puffs.Count; index++) {
                BlobPuff puff = puffs[index];
                blobs[index] = new FacetMesh(Icosphere.FaceCount(levels[index]));
                BlobMesh.AppendTo(blobs[index], PuffSpec(levels[index], puff), StableHash.Mix(StableHash.Mix(variantSeed, PuffSalt), (ulong)index), puff.Centre);
                shares[index] = InnerShare(blobs[index], puff) * BuriedMargin;
            }

            var mesh = new FacetMesh();
            for (int index = 0; index < puffs.Count; index++) AppendVisible(mesh, blobs[index], puffs, shares, index);
            return mesh;
        }

        /// <summary>
        /// The share of <paramref name="puff"/>'s radii (squashed below its centre like its blob) its faceted surface
        /// <paramref name="blob"/> never dips below: the least distance from the centre to any face plane, in the
        /// puff's unit space. Faces lying on the flat base are skipped (points under the base never count as buried).
        /// </summary>
        public static double InnerShare(FacetMesh blob, BlobPuff puff) {
            if (blob == null) throw new ArgumentNullException(nameof(blob));

            List<Vector3> corners = blob.Positions;
            double least = double.MaxValue;
            for (int face = 0; face < blob.FaceCount; face++) {
                if (IsOnBase(corners, face)) continue;

                Vector3 a = ToUnit(corners[face * 3], puff);
                Vector3 b = ToUnit(corners[face * 3 + 1], puff);
                Vector3 c = ToUnit(corners[face * 3 + 2], puff);

                Vector3 normal = Vector3.Cross(b - a, c - a);
                float length = normal.Length();
                if (length < 1e-12f) continue;

                least = Math.Min(least, Math.Abs(Vector3.Dot(normal / length, a)));
            }

            return least == double.MaxValue ? 0.0 : least;
        }

        private static bool IsOnBase(List<Vector3> corners, int face) {
            return corners[face * 3].Y <= BaseEpsilon && corners[face * 3 + 1].Y <= BaseEpsilon && corners[face * 3 + 2].Y <= BaseEpsilon;
        }

        // A point in the puff's unit space: centred, divided by its radii, the squashed half below stretched back.
        private static Vector3 ToUnit(Vector3 point, BlobPuff puff) {
            double below = point.Y < puff.Centre.Y ? 1.0 - SquashBelow : 1.0;
            return new Vector3(
                (point.X - puff.Centre.X) / puff.Radii.X,
                (float)((point.Y - puff.Centre.Y) / (puff.Radii.Y * below)),
                (point.Z - puff.Centre.Z) / puff.Radii.Z);
        }

        private static void AppendVisible(FacetMesh mesh, FacetMesh blob, List<BlobPuff> puffs, double[] shares, int owner) {
            List<Vector3> corners = blob.Positions;
            for (int face = 0; face < blob.FaceCount; face++) {
                Vector3 a = corners[face * 3];
                Vector3 b = corners[face * 3 + 1];
                Vector3 c = corners[face * 3 + 2];
                if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) continue;
                if (IsBuried(a, b, c, puffs, shares, owner)) continue;

                mesh.AddFace(a, b, c);
            }
        }

        // All three corners strictly inside one other puff's inner ball (inside its real solid), never on the base.
        private static bool IsBuried(Vector3 a, Vector3 b, Vector3 c, List<BlobPuff> puffs, double[] shares, int owner) {
            for (int index = 0; index < puffs.Count; index++) {
                if (index == owner) continue;
                if (IsInside(a, puffs[index], shares[index]) && IsInside(b, puffs[index], shares[index]) && IsInside(c, puffs[index], shares[index])) return true;
            }

            return false;
        }

        private static bool IsInside(Vector3 point, BlobPuff puff, double share) {
            if (point.Y <= BaseEpsilon) return false;

            return ToUnit(point, puff).LengthSquared() < share * share;
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

        private static Vector2 CloudUv(CloudPalette palette, double heightShare) {
            double height = 1.0 - heightShare;
            double split = 1.0 - TopShare;
            if (height >= split) return palette.Top.At((1.0 - height) / TopShare);

            return palette.Underside.At(UndersideReach * (1.0 - height / split));
        }
    }
}
