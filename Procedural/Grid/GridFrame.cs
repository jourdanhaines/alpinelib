using System;
using System.Numerics;

namespace AlpineLib.Procedural.Grid {
    /// <summary>
    /// Places a cell grid in a kit's frame: x across, y along, z up. Cell (0,0)'s low corner sits at
    /// <see cref="Origin"/> and each cell is <see cref="CellSize"/> square.
    /// </summary>
    public readonly struct GridFrame {
        /// <summary>Builds a frame; <paramref name="cellSize"/> must be positive.</summary>
        public GridFrame(float cellSize, Vector3 origin) {
            if (!(cellSize > 0f)) {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Must be positive.");
            }
            CellSize = cellSize;
            Origin = origin;
        }

        /// <summary>Edge length of one cell in kit units.</summary>
        public float CellSize { get; }

        /// <summary>Kit-frame position of cell (0,0)'s low corner at height zero.</summary>
        public Vector3 Origin { get; }

        /// <summary>The low corner of <paramref name="cell"/>, lifted by <paramref name="height"/>.</summary>
        public Vector3 ToKit(CellCoord cell, float height) {
            return ToKit(cell.X, cell.Y, height);
        }

        /// <summary>The centre of <paramref name="cell"/>, lifted by <paramref name="height"/>.</summary>
        public Vector3 CellCentre(CellCoord cell, float height) {
            return ToKit(cell.X + 0.5f, cell.Y + 0.5f, height);
        }

        /// <summary>
        /// A point given in cell units (<paramref name="u"/> across, <paramref name="v"/> along) and a
        /// height in kit units.
        /// </summary>
        public Vector3 ToKit(float u, float v, float h) {
            return new Vector3(Origin.X + u * CellSize, Origin.Y + v * CellSize, Origin.Z + h);
        }
    }
}
