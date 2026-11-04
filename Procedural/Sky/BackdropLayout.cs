using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Seeds the backdrop frame on a grid of sectors: each sector's features depend only on the two stream seeds
    /// and its coordinate, so every client sees the same skyline from the same absolute position.
    /// </summary>
    /// <remarks>
    /// Placement, kind and size draw from <c>Mix(Mix(layoutSeed, sx), sz)</c>; mesh variant and yaw from the same
    /// mix of <c>variantSeed</c>, so the look of a skyline can be re-rolled without moving it.
    /// </remarks>
    public sealed class BackdropLayout {
        private readonly ulong _layoutSeed;
        private readonly ulong _variantSeed;

        /// <summary>A layout from the layout and variant stream seeds and validated <paramref name="settings"/>.</summary>
        public BackdropLayout(ulong layoutSeed, ulong variantSeed, BackdropSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Validate();
            _layoutSeed = layoutSeed;
            _variantSeed = variantSeed;
            Settings = settings;
        }

        public BackdropSettings Settings { get; }

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
            bool isCloud = kind == BackdropFeatureKind.CloudBank;
            double width = Lerp(Settings.MinWidth, Settings.MaxWidth, layout.NextDouble());
            double height = isCloud
                ? Lerp(Settings.CloudBankMinHeight, Settings.CloudBankMaxHeight, layout.NextDouble())
                : Lerp(Settings.MinHeight, Settings.MaxHeight, layout.NextDouble());
            double altitude = isCloud ? Lerp(Settings.CloudBankMinAltitude, Settings.CloudBankMaxAltitude, layout.NextDouble()) : 0.0;
            int variant = looks.NextInt(BackdropSettings.VariantsPerKind);
            double yaw = looks.NextDouble() * 2.0 * Math.PI;
            return new BackdropFeature(sectorX, sectorZ, localX, localZ, kind, variant, yaw, width, height, altitude);
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
