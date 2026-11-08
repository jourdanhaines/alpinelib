using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>
    /// Sparse landmark sites on per-tier block lattices: a pure function of the world seed, the tier
    /// settings and the coordinates, so any cell can ask which sites touch it in any order.
    /// </summary>
    /// <remarks>
    /// Immutable and thread-safe (given a pure bias). A block's site uses one <see cref="SplitMix64"/> seeded from
    /// <c>Mix(Derive(worldSeed, tier.Name), pack(block))</c> with a fixed draw order. An optional
    /// <see cref="ILandmarkAnchorBias"/> may move an attracting tier's anchor inside the same anchor window, so block
    /// maths and <see cref="SitesTouching"/> are unchanged.
    /// Tiers do not avoid each other: a mesa may sit inside or next to a giant footprint, and the
    /// consumer resolves overlaps. Cell and block coordinates must stay well inside int range (no overflow checks).
    /// </remarks>
    public sealed class LandmarkField {
        private const double OffsetScale = 10000.0;
        private const double DrawScale = 1000000.0;

        private readonly LandmarkTierSettings[] _tiers;
        private readonly ulong[] _tierStreams;
        private readonly ILandmarkAnchorBias _bias;

        /// <summary>A field over <paramref name="tiers"/>, tier index = list index.</summary>
        public LandmarkField(ulong worldSeed, IReadOnlyList<LandmarkTierSettings> tiers) : this(worldSeed, tiers, null) { }

        /// <summary>
        /// A field over <paramref name="tiers"/> whose tiers with an attract chance let <paramref name="bias"/> pick their
        /// anchors (null = never).
        /// </summary>
        public LandmarkField(ulong worldSeed, IReadOnlyList<LandmarkTierSettings> tiers, ILandmarkAnchorBias bias) {
            if (tiers == null) {
                throw new ArgumentNullException(nameof(tiers));
            }
            _tiers = new LandmarkTierSettings[tiers.Count];
            var names = new HashSet<string>(StringComparer.Ordinal);
            _tierStreams = new ulong[tiers.Count];
            for (int index = 0; index < tiers.Count; index++) {
                LandmarkTierSettings tier = tiers[index] ?? throw new ArgumentException($"Tier {index} is null.", nameof(tiers));
                if (!names.Add(tier.Name)) {
                    throw new ArgumentException($"Tier name '{tier.Name}' is used twice.", nameof(tiers));
                }
                _tiers[index] = tier;
                _tierStreams[index] = new SplitMix64(worldSeed).Derive(tier.Name).Seed;
            }
            WorldSeed = worldSeed;
            _bias = bias;
        }

        /// <summary>The seed every site derives from.</summary>
        public ulong WorldSeed { get; }

        /// <summary>How many tiers the field has.</summary>
        public int TierCount => _tiers.Length;

        /// <summary>The settings of tier <paramref name="tier"/>.</summary>
        public LandmarkTierSettings Tier(int tier) {
            return _tiers[CheckTier(tier)];
        }

        /// <summary>The site in <paramref name="block"/> of tier <paramref name="tier"/>, if the block holds one.</summary>
        public bool TryGetSite(int tier, CellCoord block, out LandmarkSite site) {
            LandmarkTierSettings settings = _tiers[CheckTier(tier)];
            ulong id = StableHash.Mix(_tierStreams[tier], Pack(block));
            var random = new SplitMix64(id);
            if (!random.Chance(settings.Presence)) {
                site = default;
                return false;
            }
            int anchorX = random.NextInt(settings.AnchorMin, settings.AnchorMax + 1);
            int anchorZ = random.NextInt(settings.AnchorMin, settings.AnchorMax + 1);
            double offsetX = Jitter(random.NextDouble(), settings.SubCellJitter);
            double offsetZ = Jitter(random.NextDouble(), settings.SubCellJitter);
            ulong shapeSeed = random.NextULong();
            var draws = new LandmarkDraws(Quantise(random.NextDouble()), Quantise(random.NextDouble()),
                                          Quantise(random.NextDouble()), Quantise(random.NextDouble()));
            var anchor = new CellCoord(block.X * settings.BlockCells + anchorX, block.Y * settings.BlockCells + anchorZ);
            bool isAttracted = TryAttract(tier, settings, block, random, ref anchor);
            site = new LandmarkSite(tier, block, anchor, offsetX, offsetZ, shapeSeed, draws, id, isAttracted);
            return true;
        }

        // Two extra draws after all the others (attract roll, then the scorer's preference), so unattracted sites keep
        // every value they had; the best-scoring window cell wins, row-major first on a tie.
        private bool TryAttract(int tier, LandmarkTierSettings settings, CellCoord block, SplitMix64 random, ref CellCoord anchor) {
            if (_bias == null || settings.AttractChance <= 0.0) {
                return false;
            }
            double roll = random.NextDouble();
            double preference = Quantise(random.NextDouble());
            if (roll >= settings.AttractChance) {
                return false;
            }
            int best = int.MaxValue;
            CellCoord chosen = anchor;
            for (int offsetY = settings.AnchorMin; offsetY <= settings.AnchorMax; offsetY++) {
                for (int offsetX = settings.AnchorMin; offsetX <= settings.AnchorMax; offsetX++) {
                    var candidate = new CellCoord(block.X * settings.BlockCells + offsetX, block.Y * settings.BlockCells + offsetY);
                    Keep(_bias.Score(tier, block, candidate, preference), candidate, ref best, ref chosen);
                }
            }
            if (best == int.MaxValue) {
                return false;
            }
            bool moved = !chosen.Equals(anchor);
            anchor = chosen;
            return moved;
        }

        /// <summary>
        /// Appends every site whose anchor is within footprint radius + <paramref name="reachCells"/>
        /// (Chebyshev) of <paramref name="cell"/>, sorted by (tier, id). Reach 0 = footprint covers the cell.
        /// </summary>
        /// <remarks>
        /// Per axis only blocks whose anchor range meets [cell − d, cell + d] are visited, at most
        /// floor((2d + AnchorMax − AnchorMin) / BlockCells) + 1; with the presets and reach ≤ 1 that is ≤ 2×2.
        /// </remarks>
        public void SitesTouching(CellCoord cell, int reachCells, List<LandmarkSite> into) {
            if (reachCells < 0) {
                throw new ArgumentOutOfRangeException(nameof(reachCells), reachCells, "Must not be negative.");
            }
            if (into == null) {
                throw new ArgumentNullException(nameof(into));
            }
            int start = into.Count;
            for (int tier = 0; tier < _tiers.Length; tier++) {
                AppendTierSites(tier, cell, reachCells, into);
            }
            into.Sort(start, into.Count - start, LandmarkSiteOrder.Instance);
        }

        /// <summary>The block of tier <paramref name="tier"/> that contains <paramref name="cell"/>.</summary>
        public CellCoord BlockOf(int tier, CellCoord cell) {
            int blockCells = _tiers[CheckTier(tier)].BlockCells;
            return new CellCoord(FloorDiv(cell.X, blockCells), FloorDiv(cell.Y, blockCells));
        }

        /// <summary>The cells a site covers, row by row (Y then X ascending).</summary>
        /// <remarks>The radius comes from this field's tier at <c>site.Tier</c>; pass only this field's sites.</remarks>
        public IEnumerable<CellCoord> FootprintCells(LandmarkSite site) {
            int radius = _tiers[CheckTier(site.Tier)].FootprintRadiusCells;
            for (int cellY = site.Anchor.Y - radius; cellY <= site.Anchor.Y + radius; cellY++) {
                for (int cellX = site.Anchor.X - radius; cellX <= site.Anchor.X + radius; cellX++) {
                    yield return new CellCoord(cellX, cellY);
                }
            }
        }

        private void AppendTierSites(int tier, CellCoord cell, int reachCells, List<LandmarkSite> into) {
            LandmarkTierSettings settings = _tiers[tier];
            int distance = settings.FootprintRadiusCells + reachCells;
            int minBlockX = CeilDiv(cell.X - distance - settings.AnchorMax, settings.BlockCells);
            int maxBlockX = FloorDiv(cell.X + distance - settings.AnchorMin, settings.BlockCells);
            int minBlockY = CeilDiv(cell.Y - distance - settings.AnchorMax, settings.BlockCells);
            int maxBlockY = FloorDiv(cell.Y + distance - settings.AnchorMin, settings.BlockCells);
            for (int blockY = minBlockY; blockY <= maxBlockY; blockY++) {
                for (int blockX = minBlockX; blockX <= maxBlockX; blockX++) {
                    AppendIfTouching(tier, new CellCoord(blockX, blockY), cell, distance, into);
                }
            }
        }

        private void AppendIfTouching(int tier, CellCoord block, CellCoord cell, int distance, List<LandmarkSite> into) {
            if (!TryGetSite(tier, block, out LandmarkSite site)) {
                return;
            }
            int chebyshev = Math.Max(Math.Abs(site.Anchor.X - cell.X), Math.Abs(site.Anchor.Y - cell.Y));
            if (chebyshev <= distance) {
                into.Add(site);
            }
        }

        private static void Keep(int score, CellCoord candidate, ref int best, ref CellCoord chosen) {
            if (score >= best) {
                return;
            }
            best = score;
            chosen = candidate;
        }

        private int CheckTier(int tier) {
            if (tier < 0 || tier >= _tiers.Length) {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, $"Must be in [0, {_tiers.Length}).");
            }
            return tier;
        }

        private static double Jitter(double unit, double jitter) {
            double offset = (2.0 * unit - 1.0) * jitter;
            // Adding +0.0 turns a negative zero (zero jitter, tiny negatives) into +0.0.
            return Math.Round(offset * OffsetScale) / OffsetScale + 0.0;
        }

        // Floor keeps the draw strictly below 1.
        private static double Quantise(double unit) {
            return Math.Floor(unit * DrawScale) / DrawScale;
        }

        private static ulong Pack(CellCoord coord) {
            return ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
        }

        private static int FloorDiv(int value, int divisor) {
            int quotient = value / divisor;
            return (value % divisor != 0 && value < 0) ? quotient - 1 : quotient;
        }

        private static int CeilDiv(int value, int divisor) {
            return -FloorDiv(-value, divisor);
        }
    }
}
