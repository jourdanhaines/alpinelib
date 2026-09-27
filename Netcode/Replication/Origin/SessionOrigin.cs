using System;
using System.Globalization;
using System.Numerics;

namespace AlpineLib.Netcode.Replication.Origin {
    /// <summary>
    /// The shared floating origin of one session: which whole cell the session frame's zero sits on, and
    /// the epoch that names this particular placement of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A world-frame position <c>p</c> in epoch <c>e</c> is the absolute position minus
    /// <c>(CellX, 0, CellZ) * CellSize</c>. Moving the origin therefore moves every world-frame position
    /// by the same <see cref="Delta"/>; carrier-relative positions are measured from their carrier and do
    /// not move at all.
    /// </para>
    /// <para>
    /// The epoch is what every position-bearing message is stamped with, so a receiver can tell which
    /// frame a number was written in. It wraps at 65536 and is compared with
    /// <see cref="IsEpochAfter"/>, never with plain <c>&gt;</c>.
    /// </para>
    /// </remarks>
    public readonly struct SessionOrigin : IEquatable<SessionOrigin> {
        /// <summary>Creates an origin placement.</summary>
        /// <param name="cellSize">Cell edge in metres; zero only for the initial, never-shifted origin.</param>
        public SessionOrigin(ushort epoch, int cellX, int cellZ, double cellSize) {
            if (double.IsNaN(cellSize) || double.IsInfinity(cellSize) || cellSize < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and not negative.");
            }

            Epoch = epoch;
            CellX = cellX;
            CellZ = cellZ;
            CellSize = cellSize;
        }

        /// <summary>The origin every session starts at: epoch zero on cell (0, 0), cell size not yet known.</summary>
        public static SessionOrigin Initial => default;

        /// <summary>Names this placement; bumped by one on every rebase.</summary>
        public ushort Epoch { get; }

        /// <summary>Cell column the session frame's zero sits on.</summary>
        public int CellX { get; }

        /// <summary>Cell row (world +Z) the session frame's zero sits on.</summary>
        public int CellZ { get; }

        /// <summary>Cell edge in metres; zero while the session has never rebased.</summary>
        public double CellSize { get; }

        /// <summary>The next epoch's origin, on another cell.</summary>
        /// <param name="cellSize">Cell edge of the new placement; must agree with this one's once known.</param>
        public SessionOrigin Next(int cellX, int cellZ, double cellSize) {
            if (!(cellSize > 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "A rebased origin needs a positive cell size.");
            }

            if (CellSize > 0.0 && CellSize != cellSize) {
                throw new ArgumentException("Cell size cannot change between origin epochs.", nameof(cellSize));
            }

            return new SessionOrigin(unchecked((ushort)(Epoch + 1)), cellX, cellZ, cellSize);
        }

        /// <summary>
        /// What to add to a world-frame position written in <paramref name="from"/> to express it in
        /// <paramref name="to"/>. Computed in double and cast once, so a far cell costs no precision.
        /// </summary>
        public static Vector3 Delta(in SessionOrigin from, in SessionOrigin to) {
            double cellSize = ResolveCellSize(in from, in to);
            double deltaX = ((long)from.CellX - to.CellX) * cellSize;
            double deltaZ = ((long)from.CellZ - to.CellZ) * cellSize;
            return new Vector3((float)deltaX, 0f, (float)deltaZ);
        }

        /// <summary>Epoch ordering that survives the counter wrapping past 65535.</summary>
        public static bool IsEpochAfter(ushort epoch, ushort reference) {
            return (short)(epoch - reference) > 0;
        }

        /// <inheritdoc />
        public bool Equals(SessionOrigin other) {
            return Epoch == other.Epoch && CellX == other.CellX && CellZ == other.CellZ && CellSize.Equals(other.CellSize);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is SessionOrigin other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                int hash = Epoch;
                hash = (hash * 397) ^ CellX;
                hash = (hash * 397) ^ CellZ;
                return (hash * 397) ^ CellSize.GetHashCode();
            }
        }

        /// <inheritdoc />
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "epoch {0} @ cell ({1}, {2}) x {3} m", Epoch, CellX, CellZ, CellSize);
        }

        /// <summary>
        /// The cell size the pair agrees on. The initial origin has none yet, so the other side's is used.
        /// </summary>
        private static double ResolveCellSize(in SessionOrigin from, in SessionOrigin to) {
            if (from.CellSize > 0.0 && to.CellSize > 0.0 && from.CellSize != to.CellSize) {
                throw new ArgumentException("Origins with different cell sizes cannot be compared.");
            }

            return from.CellSize > 0.0 ? from.CellSize : to.CellSize;
        }
    }
}
