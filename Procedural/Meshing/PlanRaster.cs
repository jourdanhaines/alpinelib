using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A look below a height cap rasterised onto a <see cref="PlanFrame"/>: per cell the lowest and highest point
    /// of the faces clipped to that cell's column (exact), and whether the cell is touched, enclosed or empty.
    /// </summary>
    internal sealed class PlanRaster {
        private const byte Empty = 0;
        private const byte Surface = 1;
        private const byte Interior = 2;
        private static readonly double[] SampleU = { 0.02, 0.98, 0.02, 0.98, 0.5 };
        private static readonly double[] SampleV = { 0.02, 0.02, 0.98, 0.98, 0.5 };

        private readonly byte[] _state;

        private PlanRaster(PlanFrame frame) {
            Frame = frame;
            Low = new double[frame.Count];
            High = new double[frame.Count];
            _state = new byte[frame.Count];
            for (int index = 0; index < frame.Count; index++) {
                Low[index] = double.PositiveInfinity;
                High[index] = double.NegativeInfinity;
            }
        }

        public PlanFrame Frame { get; }

        /// <summary>Lowest look point in the cell's column.</summary>
        public double[] Low { get; }

        /// <summary>Highest look point in the cell's column (the cap for enclosed cells).</summary>
        public double[] High { get; }

        public bool IsOccupied(int index) => _state[index] != Empty;

        /// <summary>
        /// The faces of <paramref name="corners"/> below <paramref name="top"/>; with <paramref name="fillInterior"/>
        /// the cells walled in on every side count as solid from the lowest point found to <paramref name="top"/>.
        /// </summary>
        public static PlanRaster Build(IReadOnlyList<Vector3> corners, PlanFrame frame, double top, bool fillInterior) {
            var raster = new PlanRaster(frame);
            var face = new List<Vector3>(8);
            var column = new List<Vector3>(8);
            var scratch = new List<Vector3>(8);
            for (int index = 0; index + 2 < corners.Count; index += 3) {
                face.Clear();
                for (int corner = 0; corner < 3; corner++) face.Add(raster.ToLocal(corners[index + corner]));
                PolygonClip.Clip(face, scratch, 1, top, false);
                if (scratch.Count > 0) raster.AddPolygon(scratch, column, face);
            }
            if (fillInterior) raster.FillInterior(top);
            return raster;
        }

        /// <summary>Per cell the lowest look top over five samples (corners and centre), at least <paramref name="ground"/> and at most <paramref name="top"/>.</summary>
        public static double[] Floors(IReadOnlyList<Vector3> corners, PlanFrame frame, double ground, double top) {
            var buckets = new List<int>[frame.Count];
            for (int index = 0; index + 2 < corners.Count; index += 3) AddToBuckets(corners, index, frame, buckets);

            var floors = new double[frame.Count];
            for (int cell = 0; cell < frame.Count; cell++) floors[cell] = LowestTop(corners, frame, buckets, cell, ground, top);
            return floors;
        }

        private Vector3 ToLocal(Vector3 point) {
            Frame.ToLocal(point.X, point.Z, out double localX, out double localZ);
            return new Vector3((float)localX, point.Y, (float)localZ);
        }

        // Clips the polygon to every cell column of its plan bounds, keeping each piece's height range.
        private void AddPolygon(List<Vector3> polygon, List<Vector3> column, List<Vector3> scratch) {
            Bounds(polygon, out int minColumn, out int maxColumn, out int minRow, out int maxRow);
            var cell = new List<Vector3>(8);
            for (int columnIndex = minColumn; columnIndex <= maxColumn; columnIndex++) {
                PolygonClip.Clip(polygon, scratch, 0, Frame.OriginX + columnIndex * Frame.Cell, true);
                PolygonClip.Clip(scratch, column, 0, Frame.OriginX + (columnIndex + 1) * Frame.Cell, false);
                if (column.Count == 0) continue;

                for (int row = minRow; row <= maxRow; row++) AddCell(column, columnIndex, row, scratch, cell);
            }
        }

        private void AddCell(List<Vector3> column, int columnIndex, int row, List<Vector3> scratch, List<Vector3> cell) {
            PolygonClip.Clip(column, scratch, 2, Frame.OriginZ + row * Frame.Cell, true);
            PolygonClip.Clip(scratch, cell, 2, Frame.OriginZ + (row + 1) * Frame.Cell, false);
            if (cell.Count == 0) return;

            int index = row * Frame.Columns + columnIndex;
            _state[index] = Surface;
            foreach (Vector3 point in cell) {
                Low[index] = Math.Min(Low[index], point.Y);
                High[index] = Math.Max(High[index], point.Y);
            }
        }

        private void Bounds(List<Vector3> polygon, out int minColumn, out int maxColumn, out int minRow, out int maxRow) {
            double lowX = double.PositiveInfinity, highX = double.NegativeInfinity, lowZ = double.PositiveInfinity, highZ = double.NegativeInfinity;
            foreach (Vector3 point in polygon) {
                lowX = Math.Min(lowX, point.X);
                highX = Math.Max(highX, point.X);
                lowZ = Math.Min(lowZ, point.Z);
                highZ = Math.Max(highZ, point.Z);
            }
            minColumn = Math.Max(0, (int)Math.Floor((lowX - Frame.OriginX) / Frame.Cell));
            maxColumn = Math.Min(Frame.Columns - 1, (int)Math.Floor((highX - Frame.OriginX) / Frame.Cell));
            minRow = Math.Max(0, (int)Math.Floor((lowZ - Frame.OriginZ) / Frame.Cell));
            maxRow = Math.Min(Frame.Rows - 1, (int)Math.Floor((highZ - Frame.OriginZ) / Frame.Cell));
        }

        // Empty cells the grid edge cannot reach through other empty cells are inside the walls.
        private void FillInterior(double top) {
            var outside = new bool[Frame.Count];
            var queue = new Queue<int>();
            for (int column = 0; column < Frame.Columns; column++) {
                Reach(column, 0, outside, queue);
                Reach(column, Frame.Rows - 1, outside, queue);
            }
            for (int row = 0; row < Frame.Rows; row++) {
                Reach(0, row, outside, queue);
                Reach(Frame.Columns - 1, row, outside, queue);
            }
            while (queue.Count > 0) Spread(queue.Dequeue(), outside, queue);

            double lowest = LowestFound();
            for (int index = 0; index < Frame.Count; index++) {
                if (_state[index] != Empty || outside[index]) continue;

                _state[index] = Interior;
                Low[index] = lowest;
                High[index] = top;
            }
        }

        private void Spread(int index, bool[] outside, Queue<int> queue) {
            int column = index % Frame.Columns;
            int row = index / Frame.Columns;
            if (column > 0) Reach(column - 1, row, outside, queue);
            if (column < Frame.Columns - 1) Reach(column + 1, row, outside, queue);
            if (row > 0) Reach(column, row - 1, outside, queue);
            if (row < Frame.Rows - 1) Reach(column, row + 1, outside, queue);
        }

        private void Reach(int column, int row, bool[] outside, Queue<int> queue) {
            int index = row * Frame.Columns + column;
            if (_state[index] != Empty || outside[index]) return;

            outside[index] = true;
            queue.Enqueue(index);
        }

        private double LowestFound() {
            double lowest = double.PositiveInfinity;
            foreach (double low in Low) lowest = Math.Min(lowest, low);
            return double.IsPositiveInfinity(lowest) ? 0.0 : lowest;
        }

        private static void AddToBuckets(IReadOnlyList<Vector3> corners, int face, PlanFrame frame, List<int>[] buckets) {
            double lowX = double.PositiveInfinity, highX = double.NegativeInfinity, lowZ = double.PositiveInfinity, highZ = double.NegativeInfinity;
            for (int corner = 0; corner < 3; corner++) {
                frame.ToLocal(corners[face + corner].X, corners[face + corner].Z, out double localX, out double localZ);
                lowX = Math.Min(lowX, localX);
                highX = Math.Max(highX, localX);
                lowZ = Math.Min(lowZ, localZ);
                highZ = Math.Max(highZ, localZ);
            }
            int minColumn = Math.Max(0, (int)Math.Floor((lowX - frame.OriginX) / frame.Cell));
            int maxColumn = Math.Min(frame.Columns - 1, (int)Math.Floor((highX - frame.OriginX) / frame.Cell));
            int minRow = Math.Max(0, (int)Math.Floor((lowZ - frame.OriginZ) / frame.Cell));
            int maxRow = Math.Min(frame.Rows - 1, (int)Math.Floor((highZ - frame.OriginZ) / frame.Cell));
            for (int row = minRow; row <= maxRow; row++) {
                for (int column = minColumn; column <= maxColumn; column++) (buckets[row * frame.Columns + column] ??= new List<int>()).Add(face);
            }
        }

        private static double LowestTop(IReadOnlyList<Vector3> corners, PlanFrame frame, List<int>[] buckets, int cell, double ground, double top) {
            List<int> faces = buckets[cell];
            if (faces == null) return ground;

            double lowest = top;
            for (int sample = 0; sample < SampleU.Length; sample++) {
                frame.PointIn(cell % frame.Columns, cell / frame.Columns, SampleU[sample], SampleV[sample], out double x, out double z);
                lowest = Math.Min(lowest, TopAt(corners, faces, x, z, ground, top));
            }
            return lowest;
        }

        private static double TopAt(IReadOnlyList<Vector3> corners, List<int> faces, double x, double z, double ground, double top) {
            double highest = ground;
            foreach (int face in faces) {
                if (TryHeight(corners[face], corners[face + 1], corners[face + 2], x, z, out double height)) highest = Math.Max(highest, height);
            }
            return Math.Min(highest, top);
        }

        /// <summary>Height of the plane of triangle (a, b, c) at (x, z) when (x, z) lies over the triangle.</summary>
        public static bool TryHeight(Vector3 a, Vector3 b, Vector3 c, double x, double z, out double height) {
            height = 0.0;
            double denominator = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (Math.Abs(denominator) < 1e-12) return false;

            double first = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / denominator;
            double second = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / denominator;
            double third = 1.0 - first - second;
            if (first < -1e-9 || second < -1e-9 || third < -1e-9) return false;

            height = first * a.Y + second * b.Y + third * c.Y;
            return true;
        }
    }
}
