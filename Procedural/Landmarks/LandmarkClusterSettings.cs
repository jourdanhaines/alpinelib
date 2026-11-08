using System;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>
    /// What a cluster tier's block holds: the kind shares (lonely, group, the rest multi), the size range of each member
    /// class with its skew (size = min + (max − min)·u^skew, so skew &gt; 1 favours small; skews are multiples of 0.5 up to 4,
    /// evaluated with products and one square root so every runtime agrees) and how many small members a group or multi has.
    /// </summary>
    public sealed class LandmarkClusterSettings {
        /// <summary>Fixed member slots per block: two large, then up to five small.</summary>
        public const int Slots = 7;

        /// <summary>First small slot.</summary>
        public const int FirstSmallSlot = 2;

        /// <summary>
        /// Lonely 35 % (180–600 m, skewed small), group 40 %, multi 25 % (large 400–600 m, 3–5 small of 180–300 m); 60 % of the
        /// blocks whose anchor window the bias finds a candidate in (the route crosses it) become multi.
        /// </summary>
        public static readonly LandmarkClusterSettings Clusters = new LandmarkClusterSettings(0.35, 0.40, 180.0, 600.0, 2.0, 400.0, 600.0, 1.0, 180.0, 300.0, 1.5, 3, 5, 0.6);

        /// <summary>Always lonely, 180–450 m skewed small.</summary>
        public static readonly LandmarkClusterSettings Lonely = new LandmarkClusterSettings(1.0, 0.0, 180.0, 450.0, 2.0, 400.0, 600.0, 1.0, 180.0, 300.0, 1.5, 3, 5);

        /// <summary>Validates and stores the settings (no route promotion).</summary>
        public LandmarkClusterSettings(double lonelyShare, double groupShare, double lonelyMin, double lonelyMax, double lonelySkew, double largeMin, double largeMax,
                                       double largeSkew, double smallMin, double smallMax, double smallSkew, int smallCountMin, int smallCountMax)
            : this(lonelyShare, groupShare, lonelyMin, lonelyMax, lonelySkew, largeMin, largeMax, largeSkew, smallMin, smallMax, smallSkew, smallCountMin, smallCountMax, 0.0) { }

        /// <summary>
        /// Validates and stores the settings; <paramref name="routeMultiShare"/> of the blocks whose anchor window holds a
        /// bias candidate are promoted to multi (a draw taken after every other, so other blocks keep every value).
        /// </summary>
        public LandmarkClusterSettings(double lonelyShare, double groupShare, double lonelyMin, double lonelyMax, double lonelySkew, double largeMin, double largeMax,
                                       double largeSkew, double smallMin, double smallMax, double smallSkew, int smallCountMin, int smallCountMax, double routeMultiShare) {
            if (!(routeMultiShare >= 0.0 && routeMultiShare <= 1.0)) {
                throw new ArgumentOutOfRangeException(nameof(routeMultiShare), routeMultiShare, "Must be in [0, 1].");
            }
            RouteMultiShare = routeMultiShare;
            if (!(lonelyShare >= 0.0 && groupShare >= 0.0 && lonelyShare + groupShare <= 1.0)) {
                throw new ArgumentOutOfRangeException(nameof(groupShare), groupShare, "Shares must be non-negative and sum to at most 1.");
            }
            ValidateRange(lonelyMin, lonelyMax, lonelySkew, nameof(lonelyMin));
            ValidateRange(largeMin, largeMax, largeSkew, nameof(largeMin));
            ValidateRange(smallMin, smallMax, smallSkew, nameof(smallMin));
            if (smallCountMin < 0 || smallCountMax < smallCountMin || smallCountMax > Slots - FirstSmallSlot) {
                throw new ArgumentOutOfRangeException(nameof(smallCountMax), smallCountMax, $"Small counts must lie in [0, {Slots - FirstSmallSlot}] and be ordered.");
            }
            LonelyShare = lonelyShare;
            GroupShare = groupShare;
            LonelyMin = lonelyMin;
            LonelyMax = lonelyMax;
            LonelySkew = lonelySkew;
            LargeMin = largeMin;
            LargeMax = largeMax;
            LargeSkew = largeSkew;
            SmallMin = smallMin;
            SmallMax = smallMax;
            SmallSkew = smallSkew;
            SmallCountMin = smallCountMin;
            SmallCountMax = smallCountMax;
        }

        /// <summary>Share of blocks with a bias candidate in their anchor window promoted to multi.</summary>
        public double RouteMultiShare { get; }

        /// <summary>Share of lonely blocks.</summary>
        public double LonelyShare { get; }

        /// <summary>Share of group blocks (multi takes the rest).</summary>
        public double GroupShare { get; }

        /// <summary>Lonely size range and skew, metres.</summary>
        public double LonelyMin { get; }

        /// <inheritdoc cref="LonelyMin" />
        public double LonelyMax { get; }

        /// <inheritdoc cref="LonelyMin" />
        public double LonelySkew { get; }

        /// <summary>Large size range and skew, metres.</summary>
        public double LargeMin { get; }

        /// <inheritdoc cref="LargeMin" />
        public double LargeMax { get; }

        /// <inheritdoc cref="LargeMin" />
        public double LargeSkew { get; }

        /// <summary>Small size range and skew, metres.</summary>
        public double SmallMin { get; }

        /// <inheritdoc cref="SmallMin" />
        public double SmallMax { get; }

        /// <inheritdoc cref="SmallMin" />
        public double SmallSkew { get; }

        /// <summary>Small members of a group or multi, inclusive range.</summary>
        public int SmallCountMin { get; }

        /// <inheritdoc cref="SmallCountMin" />
        public int SmallCountMax { get; }

        /// <summary>The kind of a uniform <paramref name="draw"/>.</summary>
        public LandmarkClusterKind KindOf(double draw) {
            if (draw < LonelyShare) {
                return LandmarkClusterKind.Lonely;
            }
            return draw < LonelyShare + GroupShare ? LandmarkClusterKind.Group : LandmarkClusterKind.Multi;
        }

        /// <summary>A size in [min, max] from a uniform draw with a skew exponent, rounded to 1e-4.</summary>
        public static double Size(double min, double max, double skew, double draw) {
            double shaped = 1.0;
            int halves = (int)Math.Round(skew * 2.0);
            for (int step = 0; step < halves / 2; step++) {
                shaped *= draw;
            }
            if ((halves & 1) != 0) {
                shaped *= Math.Sqrt(draw);
            }
            return Math.Round((min + (max - min) * shaped) * 10000.0) / 10000.0;
        }

        private static void ValidateRange(double min, double max, double skew, string name) {
            if (!(min > 0.0 && max >= min && !double.IsInfinity(max))) {
                throw new ArgumentOutOfRangeException(name, min, "Size ranges must be positive, finite and ordered.");
            }
            if (!(skew >= 0.5 && skew <= 4.0) || skew * 2.0 != Math.Round(skew * 2.0)) {
                throw new ArgumentOutOfRangeException(name, skew, "Skew must be a multiple of 0.5 in [0.5, 4].");
            }
        }
    }
}
