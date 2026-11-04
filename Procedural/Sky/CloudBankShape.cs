using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How a backdrop cloud bank is heaped: a row of overlapping rounded puffs on one flat base, biggest in the
    /// middle and trailing off thin at both ends, with optional crown puffs on top, all within a triangle budget.
    /// </summary>
    /// <remarks>
    /// Puff radii are in heap units and <see cref="RowLength"/> in largest puff radii. The built heap's own width:height (crown and domed tops add
    /// height) is what the layout scales by, see <see cref="BackdropSilhouette.BuildCloudBank(ulong, BackdropPalette, CloudBankShape, out double)"/>.
    /// </remarks>
    public sealed class CloudBankShape {
        /// <summary>Puffs along the base row.</summary>
        public int RowPuffs { get; set; } = 10;

        /// <summary>Smaller puffs heaped on the middle of the row.</summary>
        public int CrownPuffs { get; set; } = 2;

        /// <summary>Length the row's puffs are spread over, in largest puff radii.</summary>
        public double RowLength { get; set; } = 8.5;

        /// <summary>Smallest and largest puff radius, in heap units (the built bank is scaled to unit height after).</summary>
        public double MinRadius { get; set; } = 0.35;
        public double MaxRadius { get; set; } = 1.0;

        /// <summary>Vertical stretch of a puff over its horizontal radius: above about 0.8 the tops dome up like cumulus.</summary>
        public double Rise { get; set; } = 1.15;

        /// <summary>Horizontal over vertical radius of the peak puff, before <see cref="Rise"/> (1 = round).</summary>
        public double PuffStretch { get; set; } = 1.0;

        /// <summary>How much wider the row's end puffs are stretched than the peak's (0 = every puff as round as the peak).</summary>
        public double EndStretch { get; set; } = 0.4;

        /// <summary>Shift of the tallest puff toward +x, as a share of the half width, so the heap leans instead of sitting like a saucer.</summary>
        public double Lean { get; set; } = 0.3;

        /// <summary>Most faces the bank may have; the largest puffs get the round level-1 blob until it is spent.</summary>
        public int FaceBudget { get; set; } = 600;

        /// <summary>The shipped bank.</summary>
        public static CloudBankShape Default => new CloudBankShape();

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (RowPuffs < 2 || CrownPuffs < 0) throw new ArgumentOutOfRangeException(nameof(RowPuffs), RowPuffs, "Need at least two row puffs and no negative crown.");
            if (!(RowLength >= 1.0)) throw new ArgumentOutOfRangeException(nameof(RowLength), RowLength, "Must be at least 1.");
            if (!(MinRadius > 0.0 && MaxRadius >= MinRadius)) throw new ArgumentOutOfRangeException(nameof(MaxRadius), MaxRadius, "Need 0 < min <= max radius.");
            if (!(PuffStretch > 0.0)) throw new ArgumentOutOfRangeException(nameof(PuffStretch), PuffStretch, "Must be positive.");
            if (!(EndStretch >= 0.0)) throw new ArgumentOutOfRangeException(nameof(EndStretch), EndStretch, "Must not be negative.");
            if (!(Rise > 0.0)) throw new ArgumentOutOfRangeException(nameof(Rise), Rise, "Must be positive.");
            if (FaceBudget < (RowPuffs + CrownPuffs) * 20) throw new ArgumentOutOfRangeException(nameof(FaceBudget), FaceBudget, "Too small for one coarse blob per puff.");
        }
    }
}
