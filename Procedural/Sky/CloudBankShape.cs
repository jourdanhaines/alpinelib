using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How a backdrop cloud bank is heaped: a row of overlapping rounded puffs on one flat base, biggest in the
    /// middle and trailing off thin at both ends, with optional crown puffs on top, all within a triangle budget.
    /// </summary>
    /// <remarks>Lengths are shares of the bank's height; the bank is built <see cref="Aspect"/> heights wide, then scaled to unit size.</remarks>
    public sealed class CloudBankShape {
        /// <summary>Puffs along the base row.</summary>
        public int RowPuffs { get; set; } = 7;

        /// <summary>Smaller puffs heaped on the middle of the row.</summary>
        public int CrownPuffs { get; set; } = 2;

        /// <summary>Width the bank is built at, in heights (match the layout's aspect so puffs stay round in the world).</summary>
        public double Aspect { get; set; } = 5.5;

        /// <summary>Smallest and largest puff radius, as shares of the bank's height.</summary>
        public double MinRadius { get; set; } = 0.35;
        public double MaxRadius { get; set; } = 1.0;

        /// <summary>Vertical stretch of a puff over its horizontal radius: above about 0.8 the tops dome up like cumulus.</summary>
        public double Rise { get; set; } = 1.15;

        /// <summary>Shift of the tallest puff toward +x, as a share of the half width, so the heap leans instead of sitting like a saucer.</summary>
        public double Lean { get; set; } = 0.3;

        /// <summary>Most faces the bank may have; the largest puffs get the round level-1 blob until it is spent.</summary>
        public int FaceBudget { get; set; } = 600;

        /// <summary>The shipped bank.</summary>
        public static CloudBankShape Default => new CloudBankShape();

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (RowPuffs < 2 || CrownPuffs < 0) throw new ArgumentOutOfRangeException(nameof(RowPuffs), RowPuffs, "Need at least two row puffs and no negative crown.");
            if (!(Aspect >= 1.0)) throw new ArgumentOutOfRangeException(nameof(Aspect), Aspect, "Must be at least 1.");
            if (!(MinRadius > 0.0 && MaxRadius >= MinRadius)) throw new ArgumentOutOfRangeException(nameof(MaxRadius), MaxRadius, "Need 0 < min <= max radius.");
            if (!(Rise > 0.0)) throw new ArgumentOutOfRangeException(nameof(Rise), Rise, "Must be positive.");
            if (FaceBudget < (RowPuffs + CrownPuffs) * 20) throw new ArgumentOutOfRangeException(nameof(FaceBudget), FaceBudget, "Too small for one coarse blob per puff.");
        }
    }
}
