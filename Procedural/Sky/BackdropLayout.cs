using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Seeds the backdrop frame on a grid of sectors: each sector's features depend only on the two stream seeds
    /// and its coordinate, so every client sees the same skyline from the same absolute position.
    /// </summary>
    /// <remarks>
    /// Placement, kind and size draw from <c>Mix(Mix(layoutSeed, sx), sz)</c>; mesh variant and yaw (and a cloud
    /// bank's depth) from the same mix of <c>variantSeed</c>, so the look of a skyline can be re-rolled without
    /// moving it. A cloud bank is as wide as its height times its mesh's own aspect (jittered a little, capped at
    /// <see cref="BackdropSettings.CloudBankMaxAspect"/>), so its puffs keep the shape they were built with.
    /// </remarks>
    public sealed class BackdropLayout {
        private readonly ulong _layoutSeed;
        private readonly ulong _variantSeed;
        private readonly double[] _cloudBankAspects;

        /// <summary>
        /// A layout from the layout and variant stream seeds, validated <paramref name="settings"/> and the width over
        /// height of each cloud bank mesh, by variant (<see cref="BackdropSilhouette.CloudBankAspects"/>).
        /// </summary>
        public BackdropLayout(ulong layoutSeed, ulong variantSeed, BackdropSettings settings, IReadOnlyList<double> cloudBankAspects) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Validate();
            _cloudBankAspects = CheckAspects(cloudBankAspects, settings);
            _layoutSeed = layoutSeed;
            _variantSeed = variantSeed;
            Settings = settings;
        }

        public BackdropSettings Settings { get; }

        /// <summary>Width over height of cloud bank mesh <paramref name="variant"/>.</summary>
        public double CloudBankAspect(int variant) {
            return _cloudBankAspects[variant];
        }

        /// <summary>The seed sector (<paramref name="sectorX"/>, <paramref name="sectorZ"/>) draws from in <paramref name="streamSeed"/>.</summary>
        public static ulong SectorSeed(ulong streamSeed, int sectorX, int sectorZ) {
            return StableHash.Mix(StableHash.Mix(streamSeed, (ulong)(uint)sectorX), (ulong)(uint)sectorZ);
        }

        /// <summary>Appends sector (<paramref name="sectorX"/>, <paramref name="sectorZ"/>)'s features to <paramref name="features"/>, in draw order.</summary>
        public void AppendSector(int sectorX, int sectorZ, List<BackdropFeature> features) {
            var layout = new SplitMix64(SectorSeed(_layoutSeed, sectorX, sectorZ));
            var looks = new SplitMix64(SectorSeed(_variantSeed, sectorX, sectorZ));
            int count = layout.NextInt(Settings.MinFeaturesPerSector, Settings.MaxFeaturesPerSector + 1);
            for (int index = 0; index < count; index++) {
                features.Add(CreateFeature(sectorX, sectorZ, layout, looks));
            }
        }

        /// <summary>The features of sector (<paramref name="sectorX"/>, <paramref name="sectorZ"/>).</summary>
        public BackdropFeature[] Sector(int sectorX, int sectorZ) {
            var features = new List<BackdropFeature>(Settings.MaxFeaturesPerSector);
            AppendSector(sectorX, sectorZ, features);
            return features.ToArray();
        }

        /// <summary>The sector holding backdrop-frame (<paramref name="frameX"/>, <paramref name="frameZ"/>).</summary>
        public void SectorOf(double frameX, double frameZ, out int sectorX, out int sectorZ) {
            sectorX = (int)Math.Floor(frameX / Settings.SectorSize);
            sectorZ = (int)Math.Floor(frameZ / Settings.SectorSize);
        }

        /// <summary>
        /// Appends every feature of the sectors overlapping the square of half-size <paramref name="radius"/> around
        /// backdrop-frame (<paramref name="frameX"/>, <paramref name="frameZ"/>), sectors row-major (z, then x).
        /// </summary>
        public void AppendAround(double frameX, double frameZ, double radius, List<BackdropFeature> features) {
            SectorOf(frameX - radius, frameZ - radius, out int minX, out int minZ);
            SectorOf(frameX + radius, frameZ + radius, out int maxX, out int maxZ);
            for (int sectorZ = minZ; sectorZ <= maxZ; sectorZ++) {
                for (int sectorX = minX; sectorX <= maxX; sectorX++) AppendSector(sectorX, sectorZ, features);
            }
        }

        private BackdropFeature CreateFeature(int sectorX, int sectorZ, SplitMix64 layout, SplitMix64 looks) {
            double localX = layout.NextDouble() * Settings.SectorSize;
            double localZ = layout.NextDouble() * Settings.SectorSize;
            BackdropFeatureKind kind = PickKind(layout.NextDouble(), layout.NextDouble());
            double widthRoll = layout.NextDouble();
            double heightRoll = layout.NextDouble();
            int variant = looks.NextInt(BackdropSettings.VariantsPerKind);
            double yaw = looks.NextDouble() * 2.0 * Math.PI;
            if (kind != BackdropFeatureKind.CloudBank) {
                double height = Lerp(Settings.MinHeight, Settings.MaxHeight, heightRoll);
                double width = Math.Min(Lerp(Settings.MinWidth, Settings.MaxWidth, widthRoll), height * Settings.MaxRockAspect);
                return new BackdropFeature(sectorX, sectorZ, localX, localZ, kind, variant, yaw, width, height, width, 0.0);
            }

            double bankHeight = Lerp(Settings.CloudBankMinHeight, Settings.CloudBankMaxHeight, heightRoll);
            double altitude = Lerp(Settings.CloudBankMinAltitude, Settings.CloudBankMaxAltitude, layout.NextDouble());
            double aspect = Math.Min(_cloudBankAspects[variant] * Jitter(Settings.CloudBankWidthJitter, widthRoll), Settings.CloudBankMaxAspect);
            double bankWidth = bankHeight * aspect;
            double depth = bankHeight * Jitter(Settings.CloudBankDepthJitter, looks.NextDouble());
            return new BackdropFeature(sectorX, sectorZ, localX, localZ, kind, variant, yaw, bankWidth, bankHeight, depth, altitude);
        }

        private static double[] CheckAspects(IReadOnlyList<double> aspects, BackdropSettings settings) {
            if (aspects == null) throw new ArgumentNullException(nameof(aspects));
            if (aspects.Count != BackdropSettings.VariantsPerKind) throw new ArgumentException($"Need one aspect per cloud bank variant ({BackdropSettings.VariantsPerKind}).", nameof(aspects));

            var copy = new double[aspects.Count];
            for (int variant = 0; variant < copy.Length; variant++) {
                double aspect = aspects[variant];
                if (!(aspect > 0.0) || double.IsInfinity(aspect)) throw new ArgumentOutOfRangeException(nameof(aspects), aspect, $"Cloud bank {variant}'s aspect must be positive and finite.");

                copy[variant] = aspect;
            }

            return copy;
        }

        // 1 ± share across the roll.
        private static double Jitter(double share, double roll) {
            return 1.0 + share * (2.0 * roll - 1.0);
        }

        private BackdropFeatureKind PickKind(double cloudRoll, double rockRoll) {
            if (cloudRoll < Settings.CloudBankShare) return BackdropFeatureKind.CloudBank;

            double total = Settings.MesaWeight + Settings.ButteWeight + Settings.RidgeWeight;
            double pick = rockRoll * total;
            if (pick < Settings.MesaWeight) return BackdropFeatureKind.Mesa;

            return pick < Settings.MesaWeight + Settings.ButteWeight ? BackdropFeatureKind.Butte : BackdropFeatureKind.Ridge;
        }

        private static double Lerp(double low, double high, double t) {
            return low + (high - low) * t;
        }
    }
}
