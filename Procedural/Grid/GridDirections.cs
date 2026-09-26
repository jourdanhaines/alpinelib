using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Grid {
    /// <summary>Arithmetic on <see cref="GridDirection"/>: offsets, quarter turns and opposites.</summary>
    public static class GridDirections {
        /// <summary>Number of directions; also the number of quarter turns in a full turn.</summary>
        public const int Count = 4;

        /// <summary>All directions in clockwise order starting at North.</summary>
        public static readonly IReadOnlyList<GridDirection> All = new[] {
            GridDirection.North, GridDirection.East, GridDirection.South, GridDirection.West,
        };

        /// <summary>The unit cell step towards <paramref name="direction"/>.</summary>
        public static CellCoord ToOffset(GridDirection direction) {
            switch (direction) {
                case GridDirection.North: return new CellCoord(0, 1);
                case GridDirection.East: return new CellCoord(1, 0);
                case GridDirection.South: return new CellCoord(0, -1);
                case GridDirection.West: return new CellCoord(-1, 0);
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        /// <summary>
        /// Turns <paramref name="direction"/> clockwise by <paramref name="quarterTurns"/> (negative turns
        /// counter-clockwise).
        /// </summary>
        public static GridDirection Rotate(GridDirection direction, int quarterTurns) {
            return (GridDirection)Wrap((int)direction + quarterTurns);
        }

        /// <summary>Clockwise quarter turns, in [0, 3], that take <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static int QuarterTurns(GridDirection from, GridDirection to) {
            return Wrap((int)to - (int)from);
        }

        /// <summary>The direction pointing the other way.</summary>
        public static GridDirection Opposite(GridDirection direction) {
            return Rotate(direction, 2);
        }

        private static int Wrap(int value) {
            int remainder = value % Count;
            return remainder < 0 ? remainder + Count : remainder;
        }
    }
}
