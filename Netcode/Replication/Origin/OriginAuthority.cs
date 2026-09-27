using System;
using System.Numerics;

namespace AlpineLib.Netcode.Replication.Origin {
    /// <summary>
    /// Server-side rebase policy: once an anchor (the lead train, say) drifts past a threshold from the
    /// session origin, the origin moves to the whole cell the anchor stands in.
    /// </summary>
    /// <remarks>
    /// Snapping to the anchor's cell leaves the anchor at most one cell diagonal from the new zero, and the
    /// threshold is required to be at least two cells, so a rebase can never immediately ask for another:
    /// that gap is the hysteresis.
    /// </remarks>
    public sealed class OriginAuthority {
        /// <summary>Smallest threshold allowed, in cells; see the note on the type.</summary>
        public const double MinThresholdCells = 2.0;

        /// <summary>Creates a policy.</summary>
        /// <param name="thresholdMetres">Planar distance from the origin past which a rebase is due.</param>
        /// <param name="cellSize">Cell edge in metres; origins snap to whole cells of this size.</param>
        public OriginAuthority(double thresholdMetres, double cellSize) {
            if (!(cellSize > 0.0) || double.IsInfinity(cellSize)) {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive and finite.");
            }

            if (!(thresholdMetres >= cellSize * MinThresholdCells) || double.IsInfinity(thresholdMetres)) {
                throw new ArgumentOutOfRangeException(nameof(thresholdMetres), thresholdMetres,
                    "Rebase threshold must be finite and at least " + MinThresholdCells.ToString("0") + " cells.");
            }

            ThresholdMetres = thresholdMetres;
            CellSize = cellSize;
        }

        /// <summary>Planar distance from the origin past which a rebase is due.</summary>
        public double ThresholdMetres { get; }

        /// <summary>Cell edge in metres.</summary>
        public double CellSize { get; }

        /// <summary>
        /// Decides whether the anchor has drifted far enough, and if so where the origin goes next.
        /// </summary>
        /// <param name="current">The origin the anchor's position is written in.</param>
        /// <param name="anchorPosition">The anchor in the current session frame.</param>
        /// <param name="next">The next epoch's origin, on the anchor's cell.</param>
        public bool TryPlan(in SessionOrigin current, Vector3 anchorPosition, out SessionOrigin next) {
            next = current;

            if (!IsFinite(anchorPosition.X) || !IsFinite(anchorPosition.Z)) {
                return false;
            }

            double anchorX = anchorPosition.X;
            double anchorZ = anchorPosition.Z;

            if (anchorX * anchorX + anchorZ * anchorZ <= ThresholdMetres * ThresholdMetres) {
                return false;
            }

            long cellX = current.CellX + (long)Math.Floor(anchorX / CellSize);
            long cellZ = current.CellZ + (long)Math.Floor(anchorZ / CellSize);

            if (cellX < int.MinValue || cellX > int.MaxValue || cellZ < int.MinValue || cellZ > int.MaxValue) {
                return false;
            }

            next = current.Next((int)cellX, (int)cellZ, CellSize);
            return true;
        }

        private static bool IsFinite(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
