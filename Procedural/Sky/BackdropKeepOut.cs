using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Every loaded cell of the streamed world (around the local player, remote players, the train), held as row runs:
    /// cell (x, y) spans absolute [x, x + 1) × [y, y + 1) cell sizes. Backdrop silhouettes keep clear of all of them.
    /// </summary>
    public sealed class BackdropKeepOut {
        private readonly List<CellCoord> _sorted = new List<CellCoord>();
        private readonly List<(long Row, long MinX, long MaxX)> _runs = new List<(long Row, long MinX, long MaxX)>();
        private readonly double _cellSize;

        public BackdropKeepOut(double cellSize) {
            if (!(cellSize > 0.0) || double.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Must be positive and finite.");

            _cellSize = cellSize;
        }

        /// <summary>Loaded cells as of the last <see cref="SetCells"/>.</summary>
        public int CellCount { get; private set; }

        /// <summary>Row runs the cells merged into.</summary>
        public int RunCount => _runs.Count;

        /// <summary>Replaces the loaded cells with <paramref name="cells"/> (any order, duplicates allowed).</summary>
        public void SetCells(IReadOnlyList<CellCoord> cells) {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            _sorted.Clear();
            for (int index = 0; index < cells.Count; index++) _sorted.Add(cells[index]);
            _sorted.Sort(CompareRowMajor);
            _runs.Clear();
            CellCount = 0;
            foreach (CellCoord cell in _sorted) Append(cell);
        }

        /// <summary>Distance from absolute (<paramref name="x"/>, <paramref name="z"/>) to the nearest loaded cell; 0 inside one, infinity with none.</summary>
        public double Gap(double x, double z) {
            double least = double.PositiveInfinity;
            foreach ((long Row, long MinX, long MaxX) run in _runs) {
                double gapX = AxisGap(x, run.MinX * _cellSize, (run.MaxX + 1L) * _cellSize);
                double gapZ = AxisGap(z, run.Row * _cellSize, (run.Row + 1L) * _cellSize);
                least = Math.Min(least, gapX * gapX + gapZ * gapZ);
            }

            return Math.Sqrt(least);
        }

        private void Append(CellCoord cell) {
            int last = _runs.Count - 1;
            if (last >= 0 && _runs[last].Row == cell.Y && cell.X <= _runs[last].MaxX + 1L) {
                if (cell.X > _runs[last].MaxX) {
                    _runs[last] = (cell.Y, _runs[last].MinX, cell.X);
                    CellCount++;
                }
                return;
            }

            _runs.Add((cell.Y, cell.X, cell.X));
            CellCount++;
        }

        private static int CompareRowMajor(CellCoord left, CellCoord right) {
            int byRow = left.Y.CompareTo(right.Y);
            return byRow != 0 ? byRow : left.X.CompareTo(right.X);
        }

        private static double AxisGap(double value, double low, double high) {
            if (value < low) return low - value;

            return value > high ? value - high : 0.0;
        }
    }
}
