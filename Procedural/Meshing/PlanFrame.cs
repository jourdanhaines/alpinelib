using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A square plan grid turned about +Y by whole degrees (local x along right (cos, −sin), z along forward
    /// (sin, cos), like <see cref="EnvelopeBox"/>): cell size, origin and extent in its own frame.
    /// </summary>
    internal sealed class PlanFrame {
        private const double DegreesToRadians = Math.PI / 180.0;

        private PlanFrame(int yawDegrees, double cell, double originX, double originZ, int columns, int rows) {
            YawDegrees = yawDegrees;
            Sine = Math.Sin(yawDegrees * DegreesToRadians);
            Cosine = Math.Cos(yawDegrees * DegreesToRadians);
            Cell = cell;
            OriginX = originX;
            OriginZ = originZ;
            Columns = columns;
            Rows = rows;
        }

        public int YawDegrees { get; }
        public double Sine { get; }
        public double Cosine { get; }
        public double Cell { get; }
        public double OriginX { get; }
        public double OriginZ { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int Count => Columns * Rows;

        /// <summary>A grid at <paramref name="yawDegrees"/> holding the world plan box [minX, maxX] × [minZ, maxZ].</summary>
        public static PlanFrame Around(int yawDegrees, double cell, double minX, double maxX, double minZ, double maxZ) {
            var probe = new PlanFrame(yawDegrees, cell, 0.0, 0.0, 0, 0);
            double lowX = double.PositiveInfinity, highX = double.NegativeInfinity, lowZ = double.PositiveInfinity, highZ = double.NegativeInfinity;
            foreach ((double x, double z) in new[] { (minX, minZ), (maxX, minZ), (maxX, maxZ), (minX, maxZ) }) {
                probe.ToLocal(x, z, out double localX, out double localZ);
                lowX = Math.Min(lowX, localX);
                highX = Math.Max(highX, localX);
                lowZ = Math.Min(lowZ, localZ);
                highZ = Math.Max(highZ, localZ);
            }
            double originX = Math.Floor(lowX / cell) * cell - cell;
            double originZ = Math.Floor(lowZ / cell) * cell - cell;
            int columns = (int)Math.Ceiling((highX - originX) / cell) + 1;
            int rows = (int)Math.Ceiling((highZ - originZ) / cell) + 1;
            return new PlanFrame(yawDegrees, cell, originX, originZ, columns, rows);
        }

        public void ToLocal(double x, double z, out double localX, out double localZ) {
            localX = x * Cosine - z * Sine;
            localZ = x * Sine + z * Cosine;
        }

        public void ToWorld(double localX, double localZ, out double x, out double z) {
            x = localX * Cosine + localZ * Sine;
            z = -localX * Sine + localZ * Cosine;
        }

        /// <summary>The cell index holding world point (x, z), or −1 off the grid.</summary>
        public int IndexAt(double x, double z) {
            ToLocal(x, z, out double localX, out double localZ);
            int column = (int)Math.Floor((localX - OriginX) / Cell);
            int row = (int)Math.Floor((localZ - OriginZ) / Cell);
            return column < 0 || row < 0 || column >= Columns || row >= Rows ? -1 : row * Columns + column;
        }

        /// <summary>World position of the point at fractions (u, v) across cell (column, row).</summary>
        public void PointIn(int column, int row, double u, double v, out double x, out double z) {
            ToWorld(OriginX + (column + u) * Cell, OriginZ + (row + v) * Cell, out x, out z);
        }
    }
}
