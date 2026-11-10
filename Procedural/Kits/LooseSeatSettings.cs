using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Kits {
    /// <summary>How <see cref="LooseSeat"/> samples a rock: its bottom grid, its ellipsoid's low lattice and how far a low point may stand proud.</summary>
    public readonly struct LooseSeatSettings {
        private readonly double[] _lowLattice;
        private readonly IReadOnlyList<double> _readOnlyLattice;

        /// <summary>
        /// A <paramref name="bottomGrid"/> × <paramref name="bottomGrid"/> grid over the turned bottom face; the low lattice
        /// is every pair of <paramref name="lowLattice"/> shares (a² + b² ≤ 1) of the half extents, copied; a low point may
        /// stand <paramref name="lowPointSlack"/> over the surface.
        /// </summary>
        public LooseSeatSettings(int bottomGrid, IReadOnlyList<double> lowLattice, double lowPointSlack) {
            if (bottomGrid < 2) throw new ArgumentOutOfRangeException(nameof(bottomGrid), bottomGrid, "The bottom grid needs at least two rows.");
            if (lowLattice == null) throw new ArgumentNullException(nameof(lowLattice));
            if (!(lowPointSlack >= 0.0)) throw new ArgumentOutOfRangeException(nameof(lowPointSlack), lowPointSlack, "Slack must be a number at least zero.");

            _lowLattice = new double[lowLattice.Count];
            for (int index = 0; index < _lowLattice.Length; index++) _lowLattice[index] = CheckedShare(lowLattice[index]);
            _readOnlyLattice = Array.AsReadOnly(_lowLattice);
            BottomGrid = bottomGrid;
            LowPointSlack = lowPointSlack;
        }

        /// <summary>Rows (and columns) of the bottom face grid.</summary>
        public int BottomGrid { get; }

        /// <summary>Shares of the half extents along X and Z whose pairs give the ellipsoid's low points.</summary>
        public IReadOnlyList<double> LowLattice => _readOnlyLattice;

        /// <summary>How far an ellipsoid low point may stand over the surface once seated, metres.</summary>
        public double LowPointSlack { get; }

        /// <summary>False for <c>default</c> settings, which <see cref="LooseSeat.Seated"/> refuses.</summary>
        public bool IsBuilt => _lowLattice != null;

        /// <summary>The lattice shares, read without allocating.</summary>
        internal double LatticeAt(int index) => _lowLattice[index];

        /// <summary>How many lattice shares there are.</summary>
        internal int LatticeCount => _lowLattice.Length;

        // Every pair (a, b) of shares lies on the ellipsoid only when 2·max(|share|)² ≤ 1.
        private static double CheckedShare(double share) {
            if (double.IsNaN(share) || 2.0 * share * share > 1.0) throw new ArgumentOutOfRangeException("lowLattice", share, "Every lattice pair must lie on the ellipsoid (a² + b² ≤ 1).");

            return share;
        }
    }
}
