using System;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The broad phase: a uniform grid over the XZ plane that turns "which shapes could this capsule be
    /// touching" into a handful of array reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two metres per cell, and no vertical subdivision. A level's geometry is overwhelmingly floors and
    /// walls whose vertical extent is uninteresting, and the pawn queries are all small footprints, so
    /// bucketing in Y would add a dimension of bookkeeping to skip shapes the narrow phase rejects in a
    /// few subtractions anyway.
    /// </para>
    /// <para>
    /// <see cref="Collect"/> returns indices in <b>ascending order</b> and it is not free to stop doing
    /// so. The resolver's depenetration is order-dependent — pushing out of a wall and then a floor lands
    /// somewhere fractionally different from the other way round — so the iteration order is part of the
    /// determinism contract, not an implementation detail of the grid.
    /// </para>
    /// <para>
    /// The grid is built once when a world is loaded and never mutated. Movers are deliberately outside
    /// it: there are a handful of them, their poses change every tick, and rebucketing them would cost
    /// more than testing them all.
    /// </para>
    /// <para>
    /// Two kinds of shape refuse to be bucketed and are held in an always-candidate list instead: the
    /// infinite floor plane, which genuinely is in every cell, and anything whose footprint is wider than
    /// <see cref="MaxBucketedExtent"/> or further from the origin than <see cref="MaxBucketedCoordinate"/>,
    /// where the bucket rows would cost more memory than the scan they save. Those shapes are returned
    /// from every query. The same test is applied to the query rectangle, so an absurd or non-finite query
    /// degrades to "everything" rather than to an integer overflow.
    /// </para>
    /// <para>
    /// The grid never affects the outcome of a step, only the work done to reach it: the candidate set is
    /// a conservative superset that the narrow phase then tests exactly. What it does owe determinism is
    /// completeness — a shape it omits is a wall one end of the wire walks through — and the ascending
    /// order above.
    /// </para>
    /// </remarks>
    public sealed class CollisionGrid {
        /// <summary>Edge length of one bucket in metres.</summary>
        public const float CellSize = 2f;

        /// <summary>
        /// Widest XZ footprint, in metres, that is still worth bucketing. A shape spanning more than this
        /// would occupy thousands of cells and be a candidate almost everywhere anyway, so it joins the
        /// always-candidate list instead of being copied into every row it covers.
        /// </summary>
        public const float MaxBucketedExtent = 128f;

        /// <summary>
        /// How far from the origin a footprint may sit and still be bucketed. Levels live well inside
        /// this; the bound exists so cell coordinates always fit an <see cref="int"/> with room to
        /// subtract the grid origin.
        /// </summary>
        public const float MaxBucketedCoordinate = 1000000f;

        /// <summary>
        /// Ceiling on the number of cells the grid will allocate. Two shapes a very long way apart would
        /// otherwise span a table with nothing in the middle of it; past this the grid gives up and
        /// becomes a linear scan, which is the right answer for a scene that sparse.
        /// </summary>
        public const int MaxCells = 1 << 20;

        private readonly CollisionShape[] shapes;
        private readonly int[] alwaysCandidates;
        private readonly int[] cellStart;
        private readonly int[] cellShapes;
        private readonly int originCellX;
        private readonly int originCellZ;
        private readonly int cellCountX;
        private readonly int cellCountZ;

        /// <summary>Buckets the shapes by their XZ footprint.</summary>
        /// <param name="shapes">The static shape array this grid indexes. Held, not copied.</param>
        public CollisionGrid(CollisionShape[] shapes) {
            this.shapes = shapes ?? Array.Empty<CollisionShape>();

            var cellRects = new int[this.shapes.Length * 4];
            var bucketed = new bool[this.shapes.Length];
            int bucketedCount = ClassifyShapes(this.shapes, cellRects, bucketed);
            bool planned = TryPlanGrid(
                cellRects,
                bucketed,
                bucketedCount,
                out int plannedOriginX,
                out int plannedOriginZ,
                out int plannedCountX,
                out int plannedCountZ);

            if (!planned) {
                alwaysCandidates = AllIndices(this.shapes.Length);
                cellStart = new int[1];
                cellShapes = Array.Empty<int>();
                return;
            }

            originCellX = plannedOriginX;
            originCellZ = plannedOriginZ;
            cellCountX = plannedCountX;
            cellCountZ = plannedCountZ;
            alwaysCandidates = BuildAlwaysCandidates(bucketed, bucketedCount);
            cellStart = BuildCellStart(cellRects, bucketed, plannedOriginX, plannedOriginZ, plannedCountX, plannedCountZ);
            cellShapes = BuildCellShapes(cellRects, bucketed, cellStart, plannedOriginX, plannedOriginZ, plannedCountX);
        }

        /// <summary>How many shapes this grid indexes.</summary>
        public int ShapeCount => shapes.Length;

        /// <summary>
        /// Writes the indices of every shape whose footprint could overlap the given XZ rectangle into
        /// <paramref name="results"/>, in ascending order and without duplicates.
        /// </summary>
        /// <remarks>
        /// When the span fills up the highest indices are the ones dropped, so a short span holds the
        /// lowest-numbered candidates rather than an arbitrary prefix of the cell walk. Both ends of the
        /// wire therefore drop the same shapes, which keeps even an over-subscribed query deterministic.
        /// </remarks>
        /// <returns>How many indices were written, capped by the span's length.</returns>
        public int Collect(float minX, float maxX, float minZ, float maxZ, Span<int> results) {
            if (!IsBucketable(minX, maxX, minZ, maxZ)) {
                return CollectAll(results);
            }

            int written = 0;
            for (int index = 0; index < alwaysCandidates.Length; index++) {
                written = Insert(results, written, alwaysCandidates[index]);
            }

            if (cellCountX == 0 || cellCountZ == 0) {
                return written;
            }

            int queryMinX = CellCoordinate(minX) - originCellX;
            int queryMaxX = CellCoordinate(maxX) - originCellX;
            int queryMinZ = CellCoordinate(minZ) - originCellZ;
            int queryMaxZ = CellCoordinate(maxZ) - originCellZ;
            if (queryMaxX < 0 || queryMinX >= cellCountX || queryMaxZ < 0 || queryMinZ >= cellCountZ) {
                return written;
            }

            queryMinX = Math.Max(queryMinX, 0);
            queryMaxX = Math.Min(queryMaxX, cellCountX - 1);
            queryMinZ = Math.Max(queryMinZ, 0);
            queryMaxZ = Math.Min(queryMaxZ, cellCountZ - 1);

            for (int cellZ = queryMinZ; cellZ <= queryMaxZ; cellZ++) {
                written = CollectRow(cellZ, queryMinX, queryMaxX, results, written);
            }

            return written;
        }

        /// <summary>Walks one row of cells, appending each cell's shapes.</summary>
        private int CollectRow(int cellZ, int queryMinX, int queryMaxX, Span<int> results, int written) {
            int rowBase = cellZ * cellCountX;
            for (int cellX = queryMinX; cellX <= queryMaxX; cellX++) {
                written = CollectCell(rowBase + cellX, results, written);
            }

            return written;
        }

        /// <summary>Appends one cell's shapes, keeping the results sorted and unique.</summary>
        private int CollectCell(int cell, Span<int> results, int written) {
            int end = cellStart[cell + 1];
            for (int entry = cellStart[cell]; entry < end; entry++) {
                written = Insert(results, written, cellShapes[entry]);
            }

            return written;
        }

        /// <summary>Everything, in index order — the answer to a query the grid cannot bucket.</summary>
        private int CollectAll(Span<int> results) {
            int count = shapes.Length;
            if (count > results.Length) {
                count = results.Length;
            }

            for (int shapeIndex = 0; shapeIndex < count; shapeIndex++) {
                results[shapeIndex] = shapeIndex;
            }

            return count;
        }

        /// <summary>
        /// Inserts one index into a sorted, duplicate-free prefix, dropping the largest when the span is
        /// already full. Insertion sort rather than a merge because the candidate counts are single digits
        /// in practice and this needs no scratch of its own.
        /// </summary>
        private static int Insert(Span<int> results, int written, int shapeIndex) {
            int position = written;
            while (position > 0 && results[position - 1] > shapeIndex) {
                position--;
            }

            if (position > 0 && results[position - 1] == shapeIndex) {
                return written;
            }

            if (written >= results.Length) {
                return InsertIntoFull(results, position, shapeIndex);
            }

            for (int shift = written; shift > position; shift--) {
                results[shift] = results[shift - 1];
            }

            results[position] = shapeIndex;
            return written + 1;
        }

        /// <summary>Pushes the largest index off the end to make room for a smaller one.</summary>
        private static int InsertIntoFull(Span<int> results, int position, int shapeIndex) {
            if (position >= results.Length) {
                return results.Length;
            }

            for (int shift = results.Length - 1; shift > position; shift--) {
                results[shift] = results[shift - 1];
            }

            results[position] = shapeIndex;
            return results.Length;
        }

        /// <summary>
        /// Decides which shapes can be bucketed and records the cell rectangle of the ones that can, four
        /// ints per shape: min X, max X, min Z, max Z.
        /// </summary>
        private static int ClassifyShapes(CollisionShape[] shapes, int[] cellRects, bool[] bucketed) {
            int bucketedCount = 0;
            for (int shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++) {
                shapes[shapeIndex].GetBoundsXZ(out float minX, out float maxX, out float minZ, out float maxZ);
                if (!IsBucketable(minX, maxX, minZ, maxZ)) {
                    continue;
                }

                bucketed[shapeIndex] = true;
                bucketedCount++;
                int rectBase = shapeIndex * 4;
                cellRects[rectBase] = CellCoordinate(minX);
                cellRects[rectBase + 1] = CellCoordinate(maxX);
                cellRects[rectBase + 2] = CellCoordinate(minZ);
                cellRects[rectBase + 3] = CellCoordinate(maxZ);
            }

            return bucketedCount;
        }

        /// <summary>
        /// Works out the grid's origin and dimensions from the bucketable shapes, and reports whether a
        /// grid is worth building at all.
        /// </summary>
        private static bool TryPlanGrid(
            int[] cellRects,
            bool[] bucketed,
            int bucketedCount,
            out int originX,
            out int originZ,
            out int countX,
            out int countZ) {
            originX = 0;
            originZ = 0;
            countX = 0;
            countZ = 0;
            if (bucketedCount == 0) {
                return false;
            }

            int minCellX = int.MaxValue;
            int maxCellX = int.MinValue;
            int minCellZ = int.MaxValue;
            int maxCellZ = int.MinValue;
            for (int shapeIndex = 0; shapeIndex < bucketed.Length; shapeIndex++) {
                if (!bucketed[shapeIndex]) {
                    continue;
                }

                int rectBase = shapeIndex * 4;
                minCellX = Math.Min(minCellX, cellRects[rectBase]);
                maxCellX = Math.Max(maxCellX, cellRects[rectBase + 1]);
                minCellZ = Math.Min(minCellZ, cellRects[rectBase + 2]);
                maxCellZ = Math.Max(maxCellZ, cellRects[rectBase + 3]);
            }

            long spanX = (long)maxCellX - minCellX + 1L;
            long spanZ = (long)maxCellZ - minCellZ + 1L;
            if (spanX * spanZ > MaxCells) {
                return false;
            }

            originX = minCellX;
            originZ = minCellZ;
            countX = (int)spanX;
            countZ = (int)spanZ;
            return true;
        }

        /// <summary>The shapes that could not be bucketed, ascending — candidates for every query.</summary>
        private static int[] BuildAlwaysCandidates(bool[] bucketed, int bucketedCount) {
            var always = new int[bucketed.Length - bucketedCount];
            int written = 0;
            for (int shapeIndex = 0; shapeIndex < bucketed.Length; shapeIndex++) {
                if (bucketed[shapeIndex]) {
                    continue;
                }

                always[written] = shapeIndex;
                written++;
            }

            return always;
        }

        /// <summary>Every index, ascending — used when no grid was built.</summary>
        private static int[] AllIndices(int count) {
            var all = new int[count];
            for (int shapeIndex = 0; shapeIndex < count; shapeIndex++) {
                all[shapeIndex] = shapeIndex;
            }

            return all;
        }

        /// <summary>
        /// Counts each cell's occupancy and turns the counts into start offsets, one longer than the cell
        /// table so a cell's entries are always <c>[start[cell], start[cell + 1])</c>.
        /// </summary>
        private static int[] BuildCellStart(
            int[] cellRects,
            bool[] bucketed,
            int originX,
            int originZ,
            int countX,
            int countZ) {
            var starts = new int[countX * countZ + 1];
            for (int shapeIndex = 0; shapeIndex < bucketed.Length; shapeIndex++) {
                if (!bucketed[shapeIndex]) {
                    continue;
                }

                CountShapeCells(cellRects, shapeIndex, originX, originZ, countX, countZ, starts);
            }

            int running = 0;
            for (int cell = 0; cell < starts.Length; cell++) {
                int occupancy = starts[cell];
                starts[cell] = running;
                running += occupancy;
            }

            return starts;
        }

        /// <summary>Bumps the occupancy of every cell one shape's footprint covers.</summary>
        private static void CountShapeCells(
            int[] cellRects,
            int shapeIndex,
            int originX,
            int originZ,
            int countX,
            int countZ,
            int[] starts) {
            int rectBase = shapeIndex * 4;
            int minCellX = cellRects[rectBase] - originX;
            int maxCellX = cellRects[rectBase + 1] - originX;
            int minCellZ = cellRects[rectBase + 2] - originZ;
            int maxCellZ = cellRects[rectBase + 3] - originZ;
            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++) {
                CountShapeRow(cellZ * countX, minCellX, maxCellX, starts);
            }
        }

        /// <summary>Bumps one row of a shape's footprint.</summary>
        private static void CountShapeRow(int rowBase, int minCellX, int maxCellX, int[] starts) {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++) {
                starts[rowBase + cellX]++;
            }
        }

        /// <summary>
        /// Fills the cell table. Shapes are visited in ascending index order and each cell's cursor only
        /// moves forward, so every cell's run comes out ascending for free — which is what lets
        /// <see cref="Collect"/> merge rather than sort.
        /// </summary>
        private static int[] BuildCellShapes(
            int[] cellRects,
            bool[] bucketed,
            int[] cellStart,
            int originX,
            int originZ,
            int countX) {
            var entries = new int[cellStart[cellStart.Length - 1]];
            var cursor = new int[cellStart.Length - 1];
            for (int shapeIndex = 0; shapeIndex < bucketed.Length; shapeIndex++) {
                if (!bucketed[shapeIndex]) {
                    continue;
                }

                FillShapeCells(cellRects, shapeIndex, originX, originZ, countX, cellStart, cursor, entries);
            }

            return entries;
        }

        /// <summary>Writes one shape's index into every cell its footprint covers.</summary>
        private static void FillShapeCells(
            int[] cellRects,
            int shapeIndex,
            int originX,
            int originZ,
            int countX,
            int[] cellStart,
            int[] cursor,
            int[] entries) {
            int rectBase = shapeIndex * 4;
            int minCellX = cellRects[rectBase] - originX;
            int maxCellX = cellRects[rectBase + 1] - originX;
            int minCellZ = cellRects[rectBase + 2] - originZ;
            int maxCellZ = cellRects[rectBase + 3] - originZ;
            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++) {
                FillShapeRow(cellZ * countX, minCellX, maxCellX, shapeIndex, cellStart, cursor, entries);
            }
        }

        /// <summary>Writes one row of a shape's footprint.</summary>
        private static void FillShapeRow(
            int rowBase,
            int minCellX,
            int maxCellX,
            int shapeIndex,
            int[] cellStart,
            int[] cursor,
            int[] entries) {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++) {
                int cell = rowBase + cellX;
                entries[cellStart[cell] + cursor[cell]] = shapeIndex;
                cursor[cell]++;
            }
        }

        /// <summary>
        /// Whether an XZ rectangle is small enough, close enough and finite enough to be worth turning
        /// into cell coordinates. Applied to shapes at build time and to query rectangles at read time,
        /// because the failure modes are the same either way.
        /// </summary>
        private static bool IsBucketable(float minX, float maxX, float minZ, float maxZ) {
            if (!IsFinite(minX) || !IsFinite(maxX) || !IsFinite(minZ) || !IsFinite(maxZ)) {
                return false;
            }

            if (minX < -MaxBucketedCoordinate || maxX > MaxBucketedCoordinate) {
                return false;
            }

            if (minZ < -MaxBucketedCoordinate || maxZ > MaxBucketedCoordinate) {
                return false;
            }

            return maxX - minX <= MaxBucketedExtent && maxZ - minZ <= MaxBucketedExtent;
        }

        /// <summary>Neither infinite nor a NaN.</summary>
        private static bool IsFinite(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Which cell a world coordinate falls in, floored so negatives bucket correctly.</summary>
        private static int CellCoordinate(float value) {
            return (int)MathF.Floor(value / CellSize);
        }
    }
}
