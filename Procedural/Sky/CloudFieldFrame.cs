using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Where a camera sits in the wind frame of a cloud field: its field cell and its offset within it, from its
    /// absolute xz and the wind's accumulated drift, without ever casting the unbounded drift to float.
    /// </summary>
    /// <remarks>
    /// The wind frame slides over the world with the drift D: wind position = absolute − D. D is reduced modulo the
    /// cell size in double first (D = k × cell + r), so the cell index is exact and the offset within it (metres,
    /// [0, cell)) stays small however far the air has moved. A cloud anchored at (cell, anchor) is then
    /// <see cref="OffsetX"/>/<see cref="OffsetZ"/> from the camera: small numbers, safe as float.
    /// </remarks>
    public readonly struct CloudFieldFrame {
        private CloudFieldFrame(double cellSize, long cellX, long cellZ, double localX, double localZ) {
            CellSize = cellSize;
            CellX = cellX;
            CellZ = cellZ;
            LocalX = localX;
            LocalZ = localZ;
        }

        public double CellSize { get; }

        /// <summary>The camera's field cell in the wind frame.</summary>
        public long CellX { get; }
        public long CellZ { get; }

        /// <summary>The camera's offset within <see cref="CellX"/>/<see cref="CellZ"/>, metres in [0, cell).</summary>
        public double LocalX { get; }
        public double LocalZ { get; }

        /// <summary>The frame of a camera at absolute (<paramref name="absoluteX"/>, <paramref name="absoluteZ"/>) after the air moved by <paramref name="displacement"/>.</summary>
        public static CloudFieldFrame Locate(double absoluteX, double absoluteZ, WindVector displacement, double cellSize) {
            if (!(cellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Must be positive.");

            Reduce(absoluteX, displacement.X, cellSize, out long cellX, out double localX);
            Reduce(absoluteZ, displacement.Z, cellSize, out long cellZ, out double localZ);
            return new CloudFieldFrame(cellSize, cellX, cellZ, localX, localZ);
        }

        /// <summary>East offset from the camera to an anchor <paramref name="anchorX"/> into field cell <paramref name="cellX"/>, metres.</summary>
        public double OffsetX(long cellX, double anchorX) {
            return (cellX - CellX) * CellSize + (anchorX - LocalX);
        }

        /// <summary>North offset from the camera to an anchor <paramref name="anchorZ"/> into field cell <paramref name="cellZ"/>, metres.</summary>
        public double OffsetZ(long cellZ, double anchorZ) {
            return (cellZ - CellZ) * CellSize + (anchorZ - LocalZ);
        }

        // wind = absolute − (k × cell + r): only r (small) meets the absolute, k shifts the cell index exactly.
        private static void Reduce(double absolute, double drift, double cellSize, out long cell, out double local) {
            double turns = Math.Floor(drift / cellSize);
            double remainder = drift - turns * cellSize;
            double wind = absolute - remainder;
            double windCell = Math.Floor(wind / cellSize);
            local = wind - windCell * cellSize;
            if (local >= cellSize) {
                local -= cellSize;
                windCell += 1.0;
            }
            if (local < 0.0) local = 0.0;
            cell = (long)windCell - (long)turns;
        }
    }
}
