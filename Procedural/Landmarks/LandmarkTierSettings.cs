using System;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>
    /// One tier of a <see cref="LandmarkField"/>: the world is cut into square blocks of
    /// <see cref="BlockCells"/> cells and each block holds at most one site, anchored on a cell drawn
    /// from [<see cref="AnchorMin"/>, <see cref="AnchorMax"/>] on both axes.
    /// </summary>
    /// <remarks>
    /// The anchor range must keep the whole footprint (anchor ± <see cref="FootprintRadiusCells"/>)
    /// inside the block, so footprints of neighbouring blocks never overlap. A tier with <see cref="Cluster"/> settings
    /// expands each block into a <see cref="LandmarkCluster"/> of member sites.
    /// </remarks>
    public sealed class LandmarkTierSettings {
        /// <summary>Single-cell mesas, about one per 4×4 block; anchors at least 2 cells apart.</summary>
        public static readonly LandmarkTierSettings Mesa = new LandmarkTierSettings("landmark/mesa", 4, 0, 0.9, 0, 2, 8.0);

        /// <summary>3×3-cell giants, about one per 8×8 block; at least 2 free cells between footprints.</summary>
        public static readonly LandmarkTierSettings Giant = new LandmarkTierSettings("landmark/giant", 8, 1, 0.8, 2, 5, 0.0);

        /// <summary>Landmark clusters, about one per 20×20 block (2.56 km at 128 m cells), footprint radius 7, 48 m centre jitter.</summary>
        public static readonly LandmarkTierSettings Clusters = new LandmarkTierSettings("landmark/cluster", 20, 7, 0.9, 7, 12, 48.0, 0.0, LandmarkClusterSettings.Clusters);

        /// <summary>Lonely landmarks, half the 8×8 blocks, footprint radius 2, 32 m jitter.</summary>
        public static readonly LandmarkTierSettings Lonely = new LandmarkTierSettings("landmark/lonely", 8, 2, 0.5, 2, 5, 32.0, 0.0, LandmarkClusterSettings.Lonely);

        /// <summary>Validates and stores a tier that never attracts its anchor.</summary>
        public LandmarkTierSettings(string name, int blockCells, int footprintRadiusCells, double presence,
                                    int anchorMin, int anchorMax, double subCellJitter)
            : this(name, blockCells, footprintRadiusCells, presence, anchorMin, anchorMax, subCellJitter, 0.0) { }

        /// <summary>
        /// Validates and stores a tier; with a field bias, <paramref name="attractChance"/> of its sites snap their anchor to
        /// the bias's best cell in the anchor window.
        /// </summary>
        public LandmarkTierSettings(string name, int blockCells, int footprintRadiusCells, double presence,
                                    int anchorMin, int anchorMax, double subCellJitter, double attractChance)
            : this(name, blockCells, footprintRadiusCells, presence, anchorMin, anchorMax, subCellJitter, attractChance, null) { }

        /// <summary>Validates and stores a tier; <paramref name="cluster"/> non-null makes each block a cluster of member sites.</summary>
        public LandmarkTierSettings(string name, int blockCells, int footprintRadiusCells, double presence,
                                    int anchorMin, int anchorMax, double subCellJitter, double attractChance, LandmarkClusterSettings cluster) {
            if (string.IsNullOrEmpty(name)) {
                throw new ArgumentException("Must be a non-empty stream name.", nameof(name));
            }
            if (blockCells < 2) {
                throw new ArgumentOutOfRangeException(nameof(blockCells), blockCells, "Must be at least 2.");
            }
            if (footprintRadiusCells < 0) {
                throw new ArgumentOutOfRangeException(nameof(footprintRadiusCells), footprintRadiusCells, "Must not be negative.");
            }
            if (!(presence >= 0.0 && presence <= 1.0)) {
                throw new ArgumentOutOfRangeException(nameof(presence), presence, "Must be in [0, 1].");
            }
            ValidateAnchorRange(blockCells, footprintRadiusCells, anchorMin, anchorMax);
            if (!(subCellJitter >= 0.0) || double.IsInfinity(subCellJitter)) {
                throw new ArgumentOutOfRangeException(nameof(subCellJitter), subCellJitter, "Must be finite and non-negative.");
            }
            if (!(attractChance >= 0.0 && attractChance <= 1.0)) {
                throw new ArgumentOutOfRangeException(nameof(attractChance), attractChance, "Must be in [0, 1].");
            }
            Name = name;
            Cluster = cluster;
            AttractChance = attractChance;
            BlockCells = blockCells;
            FootprintRadiusCells = footprintRadiusCells;
            Presence = presence;
            AnchorMin = anchorMin;
            AnchorMax = anchorMax;
            SubCellJitter = subCellJitter;
        }

        /// <summary>The stream name the tier's sites are derived from.</summary>
        public string Name { get; }

        /// <summary>Side of a block in cells; at most one site per block.</summary>
        public int BlockCells { get; }

        /// <summary>Chebyshev radius of the footprint around the anchor: 0 = one cell, 1 = 3×3, 7 = 15×15.</summary>
        public int FootprintRadiusCells { get; }

        /// <summary>Chance that a block holds a site.</summary>
        public double Presence { get; }

        /// <summary>Lowest anchor offset inside the block, inclusive, on each axis.</summary>
        public int AnchorMin { get; }

        /// <summary>Highest anchor offset inside the block, inclusive, on each axis.</summary>
        public int AnchorMax { get; }

        /// <summary>Largest sub-cell offset of the site from its anchor cell centre, metres, on each axis.</summary>
        public double SubCellJitter { get; }

        /// <summary>Chance a site snaps its anchor to the field bias's best candidate (0 = never; needs a bias).</summary>
        public double AttractChance { get; }

        /// <summary>Cluster settings; null for a classic one-site-per-block tier.</summary>
        public LandmarkClusterSettings Cluster { get; }

        /// <summary>A copy of this tier with <paramref name="attractChance"/>.</summary>
        public LandmarkTierSettings WithAttractChance(double attractChance) {
            return new LandmarkTierSettings(Name, BlockCells, FootprintRadiusCells, Presence, AnchorMin, AnchorMax, SubCellJitter, attractChance, Cluster);
        }

        private static void ValidateAnchorRange(int blockCells, int radius, int anchorMin, int anchorMax) {
            if (anchorMin > anchorMax) {
                throw new ArgumentOutOfRangeException(nameof(anchorMax), anchorMax, "Must not be below anchorMin.");
            }
            if (anchorMin - radius < 0) {
                throw new ArgumentOutOfRangeException(nameof(anchorMin), anchorMin, "Footprint would leave the block.");
            }
            if (anchorMax + radius > blockCells - 1) {
                throw new ArgumentOutOfRangeException(nameof(anchorMax), anchorMax, "Footprint would leave the block.");
            }
        }
    }
}
