using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Seeds world-fixed silhouettes on a grid of absolute-world sectors: each sector's features depend only on the two
    /// stream seeds and its coordinate, so every client sees the same skyline in the same place, and decides which of
    /// them a camera draws (<see cref="IsDrawn"/>).
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

        /// <summary>The sector holding absolute (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public void SectorOf(double x, double z, out int sectorX, out int sectorZ) {
            sectorX = (int)Math.Floor(x / Settings.SectorSize);
            sectorZ = (int)Math.Floor(z / Settings.SectorSize);
        }

        /// <summary>
        /// Appends every feature of the sectors overlapping the square of half-size <paramref name="radius"/> around
        /// absolute (<paramref name="x"/>, <paramref name="z"/>), sectors row-major (z, then x).
        /// </summary>
        public void AppendAround(double x, double z, double radius, List<BackdropFeature> features) {
            SectorOf(x - radius, z - radius, out int minX, out int minZ);
            SectorOf(x + radius, z + radius, out int maxX, out int maxZ);
            for (int sectorZ = minZ; sectorZ <= maxZ; sectorZ++) {
                for (int sectorX = minX; sectorX <= maxX; sectorX++) AppendSector(sectorX, sectorZ, features);
            }
        }

        /// <summary>Appends, in sector order, every feature a camera at absolute (<paramref name="cameraX"/>, <paramref name="cameraZ"/>) draws.</summary>
        public void AppendDrawn(double cameraX, double cameraZ, List<BackdropFeature> features) {
            var around = new List<BackdropFeature>();
            AppendAround(cameraX, cameraZ, Settings.OuterRadius, around);
            KeepOutCellOf(cameraX, cameraZ, out long cellX, out long cellZ);
            foreach (BackdropFeature feature in around) {
                if (IsDrawn(feature, cameraX, cameraZ, cellX, cellZ)) features.Add(feature);
            }
        }

        /// <summary>
        /// Appends, in sector order, every feature a camera at absolute (<paramref name="cameraX"/>, <paramref name="cameraZ"/>)
        /// with keep-out cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) keeps in its ring, clear of every
        /// <paramref name="loaded"/> cell too (null for none).
        /// </summary>
        public void AppendDrawn(double cameraX, double cameraZ, long cellX, long cellZ, BackdropKeepOut loaded, List<BackdropFeature> features) {
            var around = new List<BackdropFeature>();
            AppendAround(cameraX, cameraZ, Settings.OuterRadius, around);
            foreach (BackdropFeature feature in around) {
                if (IsDrawn(feature, cameraX, cameraZ, cellX, cellZ) && IsClearOf(feature, loaded)) features.Add(feature);
            }
        }

        /// <summary>
        /// Moves (<paramref name="cellX"/>, <paramref name="cellZ"/>) to the keep-out cell of absolute (<paramref name="x"/>,
        /// <paramref name="z"/>) only on an axis where the point stands more than <see cref="BackdropSettings.KeepOutHysteresis"/>
        /// outside the current cell; true when it moved.
        /// </summary>
        public bool FollowKeepOutCell(double x, double z, ref long cellX, ref long cellZ) {
            long nextX = FollowAxis(x, cellX);
            long nextZ = FollowAxis(z, cellZ);
            bool isMoved = nextX != cellX || nextZ != cellZ;
            cellX = nextX;
            cellZ = nextZ;
            return isMoved;
        }

        /// <summary>True when every part of <paramref name="feature"/> is at least <see cref="BackdropSettings.KeepOutMargin"/> from every <paramref name="loaded"/> cell.</summary>
        public bool IsClearOf(in BackdropFeature feature, BackdropKeepOut loaded) {
            if (loaded == null || loaded.CellCount == 0) return true;

            double x = feature.AbsoluteX(Settings.SectorSize);
            double z = feature.AbsoluteZ(Settings.SectorSize);
            return loaded.Gap(x, z) - feature.Reach >= Settings.KeepOutMargin;
        }

        /// <summary>
        /// True when <paramref name="feature"/>'s nearest point, seen from absolute (<paramref name="cameraX"/>,
        /// <paramref name="cameraZ"/>) after the camera closes <paramref name="slack"/> metres, is at least
        /// <see cref="BackdropSettings.FogCullShare"/> hidden by <paramref name="fog"/> at <paramref name="fogScale"/>.
        /// </summary>
        public bool IsFogHidden(in BackdropFeature feature, double cameraX, double cameraZ, BackdropFog fog, double fogScale, double slack) {
            return fog.HiddenShare(NearestDistance(feature, cameraX, cameraZ) - slack, fogScale) >= Settings.FogCullShare;
        }

        /// <summary>Flat distance from absolute (<paramref name="cameraX"/>, <paramref name="cameraZ"/>) to <paramref name="feature"/>'s footprint; 0 inside it.</summary>
        public double NearestDistance(in BackdropFeature feature, double cameraX, double cameraZ) {
            double deltaX = feature.AbsoluteX(Settings.SectorSize) - cameraX;
            double deltaZ = feature.AbsoluteZ(Settings.SectorSize) - cameraZ;
            return Math.Max(Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ) - feature.Reach, 0.0);
        }

        /// <summary>The keep-out cell holding absolute (<paramref name="x"/>, <paramref name="z"/>): cell i spans [i, i + 1) × cell size.</summary>
        public void KeepOutCellOf(double x, double z, out long cellX, out long cellZ) {
            cellX = (long)Math.Floor(x / Settings.KeepOutCellSize);
            cellZ = (long)Math.Floor(z / Settings.KeepOutCellSize);
        }

        /// <summary>
        /// True when a camera at absolute (<paramref name="cameraX"/>, <paramref name="cameraZ"/>) in keep-out cell
        /// (<paramref name="cellX"/>, <paramref name="cellZ"/>) draws <paramref name="feature"/>: its centre within
        /// <see cref="BackdropSettings.OuterRadius"/> of the camera and its whole footprint at least
        /// <see cref="BackdropSettings.KeepOutMargin"/> outside the keep-out square. The answer changes only when the
        /// keep-out cell moves (at <see cref="BackdropSettings.ClearDistance"/> or more) or at the outer radius.
        /// </summary>
        public bool IsDrawn(in BackdropFeature feature, double cameraX, double cameraZ, long cellX, long cellZ) {
            double x = feature.AbsoluteX(Settings.SectorSize);
            double z = feature.AbsoluteZ(Settings.SectorSize);
            double deltaX = x - cameraX;
            double deltaZ = z - cameraZ;
            double outer = Settings.OuterRadius;
            if (!(deltaX * deltaX + deltaZ * deltaZ <= outer * outer)) return false;

            return KeepOutGap(x, z, cellX, cellZ) - feature.Reach >= Settings.KeepOutMargin;
        }

        /// <summary>Distance from absolute (<paramref name="x"/>, <paramref name="z"/>) to the keep-out square of cell (<paramref name="cellX"/>, <paramref name="cellZ"/>); 0 inside it.</summary>
        public double KeepOutGap(double x, double z, long cellX, long cellZ) {
            double size = Settings.KeepOutCellSize;
            int radius = Settings.KeepOutRadiusCells;
            double gapX = AxisGap(x, (cellX - radius) * size, (cellX + radius + 1) * size);
            double gapZ = AxisGap(z, (cellZ - radius) * size, (cellZ + radius + 1) * size);
            return Math.Sqrt(gapX * gapX + gapZ * gapZ);
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

        private long FollowAxis(double value, long cell) {
            double size = Settings.KeepOutCellSize;
            double band = Settings.KeepOutHysteresis;
            if (value >= cell * size - band && value < (cell + 1L) * size + band) return cell;

            return (long)Math.Floor(value / size);
        }

        private static double AxisGap(double value, double low, double high) {
            if (value < low) return low - value;

            return value > high ? value - high : 0.0;
        }

        private static double Lerp(double low, double high, double t) {
            return low + (high - low) * t;
        }
    }
}
