using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Kits {
    /// <summary>How <see cref="LooseSeat"/> samples a rock: its bottom grid, its ellipsoid's low lattice and how far a low point may stand proud.</summary>
    public readonly struct LooseSeatSettings {
        /// <summary>
        /// A <paramref name="bottomGrid"/> × <paramref name="bottomGrid"/> grid over the turned bottom face; the low lattice
        /// is every pair of <paramref name="lowLattice"/> shares (a² + b² ≤ 1) of the half extents; a low point may stand
        /// <paramref name="lowPointSlack"/> over the surface.
        /// </summary>
        public LooseSeatSettings(int bottomGrid, IReadOnlyList<double> lowLattice, double lowPointSlack) {
            if (bottomGrid < 2) throw new ArgumentOutOfRangeException(nameof(bottomGrid), bottomGrid, "The bottom grid needs at least two rows.");
            if (lowLattice == null) throw new ArgumentNullException(nameof(lowLattice));
            if (!(lowPointSlack >= 0.0)) throw new ArgumentOutOfRangeException(nameof(lowPointSlack), lowPointSlack, "Slack must not be negative.");

            double widest = 0.0;
            foreach (double share in lowLattice) widest = Math.Max(widest, Math.Abs(share));
            if (2.0 * widest * widest > 1.0) throw new ArgumentOutOfRangeException(nameof(lowLattice), "Every lattice pair must lie on the ellipsoid (a² + b² ≤ 1).");

            BottomGrid = bottomGrid;
            LowLattice = lowLattice;
            LowPointSlack = lowPointSlack;
        }

        /// <summary>Rows (and columns) of the bottom face grid.</summary>
        public int BottomGrid { get; }

        /// <summary>Shares of the half extents along X and Z whose pairs give the ellipsoid's low points.</summary>
        public IReadOnlyList<double> LowLattice { get; }

        /// <summary>How far an ellipsoid low point may stand over the surface once seated, metres.</summary>
        public double LowPointSlack { get; }
    }
}
