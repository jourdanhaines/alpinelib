using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Fits yawed boxes to a look in the band a walking capsule meets, tight in plan so no invisible wall stands
    /// off the look and no box top floats over sand. The look below <see cref="WalkingBandSettings.Top"/> is
    /// rasterised per plan cell (exact height range of the faces in each cell's column). Pieces reaching the band
    /// top are walls: their cells above the step are covered by full-height columns (ground to the band top, so
    /// no top is reachable) drawn from turned grids within <see cref="WalkingBandSettings.Reach"/> of the wall.
    /// Cells no higher than the step get boxes no taller than the step; the rest (low lumps) get flat steps whose
    /// top leaves at most <see cref="WalkingBandSettings.LedgeTolerance"/> of air over the look or ground beneath.
    /// Above the band top a coarse <see cref="BoxEnvelopeFitter"/> fit holds the rest.
    /// </summary>
    /// <remarks>
    /// Every occupied cell is held whole in plan by a box whose height range holds the cell's faces, so the
    /// union holds the look by construction; rounding only grows boxes.
    /// </remarks>
    public static class WalkingBandFitter {
        private const int QuarterTurn = 90;

        /// <summary>The boxes around the faces in <paramref name="corners"/> (three per face).</summary>
        public static WalkingBandFit Fit(IReadOnlyList<Vector3> corners, WalkingBandSettings settings) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (corners.Count % 3 != 0) throw new ArgumentException("Corners must come three per face.", nameof(corners));

            settings.Validate();
            var fit = new WalkingBandFit();
            SplitWalls(corners, double.IsNaN(settings.WallHeight) ? settings.Top : settings.WallHeight, out List<Vector3> walls, out List<Vector3> lumps);
            if (!TryFrame(corners, settings, out PlanFrame frame)) {
                AddUpper(corners, settings, fit);
                return fit;
            }

            var band = new Band(frame, walls, lumps, corners, settings);
            band.CoverColumns(fit);
            band.CoverFeet(fit);
            band.CoverSteps(fit);
            AddUpper(corners, settings, fit);
            return fit;
        }

        // Pieces whose highest point reaches the band top are walls; the others are lumps.
        private static void SplitWalls(IReadOnlyList<Vector3> corners, double wallHeight, out List<Vector3> walls, out List<Vector3> lumps) {
            walls = new List<Vector3>();
            lumps = new List<Vector3>();
            foreach (List<int> piece in TrianglePieces.Split(corners)) {
                List<Vector3> pieceCorners = TrianglePieces.Corners(corners, piece);
                (Highest(pieceCorners) >= wallHeight ? walls : lumps).AddRange(pieceCorners);
            }
        }

        private static double Highest(List<Vector3> corners) {
            double highest = double.NegativeInfinity;
            foreach (Vector3 corner in corners) highest = Math.Max(highest, corner.Y);
            return highest;
        }

        // The base grid at the band's least-area yaw around everything below the band top, padded for the reach.
        private static bool TryFrame(IReadOnlyList<Vector3> corners, WalkingBandSettings settings, out PlanFrame frame) {
            frame = null;
            var low = new List<Vector3>();
            foreach (Vector3 corner in corners) {
                if (corner.Y <= settings.Top) low.Add(corner);
            }
            if (low.Count == 0) return false;

            double pad = settings.Reach + settings.Cell * 2.0;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (Vector3 corner in corners) {
                minX = Math.Min(minX, corner.X);
                maxX = Math.Max(maxX, corner.X);
                minZ = Math.Min(minZ, corner.Z);
                maxZ = Math.Max(maxZ, corner.Z);
            }
            frame = PlanFrame.Around(BestYaw(low), settings.Cell, minX - pad, maxX + pad, minZ - pad, maxZ + pad);
            return true;
        }

        private static int BestYaw(List<Vector3> points) {
            int best = 0;
            double bestArea = double.PositiveInfinity;
            for (int yaw = 0; yaw < QuarterTurn; yaw++) {
                double area = PlanArea(points, yaw);
                if (area >= bestArea) continue;

                bestArea = area;
                best = yaw;
            }
            return best;
        }

        private static double PlanArea(List<Vector3> points, int yaw) {
            var box = new BoxEnvelopeCandidate(yaw);
            foreach (Vector3 point in points) box.Include(point.X, point.Y, point.Z);
            return (box.MaxX - box.MinX) * (box.MaxZ - box.MinZ);
        }

        private static void AddUpper(IReadOnlyList<Vector3> corners, WalkingBandSettings settings, WalkingBandFit fit) {
            List<Vector3> upper = ClipAbove(corners, settings.Top);
            if (upper.Count == 0) return;

            var upperSettings = new BoxEnvelopeSettings { BandEdges = settings.UpperBandEdges, MaxBoxes = settings.UpperMaxBoxes, Snap = settings.Snap };
            fit.Upper.AddRange(BoxEnvelopeFitter.Fit(upper, upperSettings));
        }

        // The faces' parts above height level, fanned back into triangles.
        private static List<Vector3> ClipAbove(IReadOnlyList<Vector3> corners, double level) {
            var result = new List<Vector3>();
            var face = new List<Vector3>(3);
            var clipped = new List<Vector3>(8);
            for (int index = 0; index + 2 < corners.Count; index += 3) {
                face.Clear();
                face.Add(corners[index]);
                face.Add(corners[index + 1]);
                face.Add(corners[index + 2]);
                PolygonClip.Clip(face, clipped, 1, level, true);
                for (int fan = 1; fan + 1 < clipped.Count; fan++) {
                    result.Add(clipped[0]);
                    result.Add(clipped[fan]);
                    result.Add(clipped[fan + 1]);
                }
            }
            return result;
        }

        /// <summary>The rasterised band and the cover steps over it.</summary>
        private sealed class Band {
            private readonly PlanFrame _frame;
            private readonly PlanRaster _walls;
            private readonly PlanRaster _lumps;
            private readonly double[] _floors;
            private readonly WalkingBandSettings _settings;
            private readonly PlanCoverage _coverage;
            private readonly bool[] _covered;
            private readonly bool[] _roofed;

            public Band(PlanFrame frame, List<Vector3> walls, List<Vector3> lumps, IReadOnlyList<Vector3> all, WalkingBandSettings settings) {
                _frame = frame;
                _settings = settings;
                _walls = PlanRaster.Build(walls, frame, settings.Top, true);
                _lumps = PlanRaster.Build(lumps, frame, settings.Top, false);
                _floors = PlanRaster.Floors(all, frame, settings.Ground, settings.Top);
                _coverage = new PlanCoverage(frame, settings.YawStepDegrees);
                _covered = new bool[frame.Count];
                _roofed = new bool[frame.Count];
            }

            private bool IsOccupied(int index) => _walls.IsOccupied(index) || _lumps.IsOccupied(index);

            private double High(int index) => Math.Max(_walls.High[index], _lumps.High[index]);

            private double Low(int index) => Math.Min(_walls.Low[index], _lumps.Low[index]);

            // Wall cells above the step, in full-height columns that may reach past the wall by the reach.
            public void CoverColumns(WalkingBandFit fit) {
                var target = new bool[_frame.Count];
                for (int index = 0; index < _frame.Count; index++) target[index] = _walls.IsOccupied(index) && _walls.High[index] > _settings.Step;

                List<PlanRect> rects = _coverage.Cover(target, Dilate(target, _settings.Reach), _covered);
                foreach (PlanRect rect in rects) _coverage.TakeHeld(rect, IsOccupied, _roofed);
                for (int index = 0; index < _frame.Count; index++) _covered[index] |= _roofed[index];
                foreach (PlanRect rect in rects) fit.Columns.Add(ToBox(rect, _settings.Top));
            }

            // Cells no higher than the step, in boxes no taller than their cells that may reach past the look by the reach.
            public void CoverFeet(WalkingBandFit fit) {
                var target = new bool[_frame.Count];
                var occupied = new bool[_frame.Count];
                for (int index = 0; index < _frame.Count; index++) {
                    occupied[index] = IsOccupied(index);
                    target[index] = occupied[index] && !_covered[index] && High(index) <= _settings.Step;
                }
                foreach (PlanRect rect in _coverage.Cover(target, Dilate(occupied, double.IsNaN(_settings.FootReach) ? _settings.Reach : _settings.FootReach), _covered)) fit.Feet.Add(ToBox(rect, HighestOf(rect)));
            }

            // The remaining low lumps, highest first, in flat steps in the base grid.
            public void CoverSteps(WalkingBandFit fit) {
                while (TryHighestOpen(out int seed)) {
                    double top = High(seed);
                    PlanRect rect = IsUnderLedge(seed, top) ? GrowStep(seed, top) : new PlanRect(_frame, seed % _frame.Columns, seed % _frame.Columns, seed / _frame.Columns, seed / _frame.Columns);
                    if (!IsUnderLedge(seed, top)) fit.SteepCells++;
                    TakeStepCells(rect, top);
                    fit.Steps.Add(ToBox(rect, top));
                }
            }

            private bool TryHighestOpen(out int seed) {
                seed = -1;
                double highest = double.NegativeInfinity;
                for (int index = 0; index < _frame.Count; index++) {
                    if (!IsOccupied(index) || _covered[index] || High(index) <= highest) continue;

                    highest = High(index);
                    seed = index;
                }
                return seed >= 0;
            }

            // A cell may lie under a step top when a column roofs it or the look or ground under it is near enough.
            private bool IsUnderLedge(int index, double top) {
                return _roofed[index] || _floors[index] >= top - _settings.LedgeTolerance;
            }

            private PlanRect GrowStep(int seed, double top) {
                int minColumn = seed % _frame.Columns, maxColumn = minColumn, minRow = seed / _frame.Columns, maxRow = minRow;
                long gain = StepGain(minColumn, maxColumn, minRow, maxRow, top);
                while (true) {
                    int side = BestStepSide(minColumn, maxColumn, minRow, maxRow, top, ref gain);
                    if (side < 0) return new PlanRect(_frame, minColumn, maxColumn, minRow, maxRow);

                    StepSide(side, ref minColumn, ref maxColumn, ref minRow, ref maxRow);
                }
            }

            private int BestStepSide(int minColumn, int maxColumn, int minRow, int maxRow, double top, ref long gain) {
                int side = -1;
                for (int candidate = 0; candidate < 4; candidate++) {
                    long candidateGain = TryStepSide(candidate, minColumn, maxColumn, minRow, maxRow, top);
                    if (candidateGain <= gain) continue;

                    gain = candidateGain;
                    side = candidate;
                }
                return side;
            }

            // The gain of growing one side, or −1 when the new row or column holds a cell that may not lie under the top.
            private long TryStepSide(int side, int minColumn, int maxColumn, int minRow, int maxRow, double top) {
                StepSide(side, ref minColumn, ref maxColumn, ref minRow, ref maxRow);
                if (minColumn < 0 || minRow < 0 || maxColumn >= _frame.Columns || maxRow >= _frame.Rows) return -1;

                int width = maxColumn - minColumn + 1;
                for (int cell = 0; cell < width * (maxRow - minRow + 1); cell++) {
                    if (!IsUnderLedge((minRow + cell / width) * _frame.Columns + minColumn + cell % width, top)) return -1;
                }
                return StepGain(minColumn, maxColumn, minRow, maxRow, top);
            }

            private static void StepSide(int side, ref int minColumn, ref int maxColumn, ref int minRow, ref int maxRow) {
                if (side == 0) minColumn--;
                else if (side == 1) maxColumn++;
                else if (side == 2) minRow--;
                else maxRow++;
            }

            private long StepGain(int minColumn, int maxColumn, int minRow, int maxRow, double top) {
                long gain = 0;
                for (int row = minRow; row <= maxRow; row++) {
                    for (int column = minColumn; column <= maxColumn; column++) gain += IsOpenUnder(row * _frame.Columns + column, top) ? 1 : 0;
                }
                return gain;
            }

            private bool IsOpenUnder(int index, double top) => IsOccupied(index) && !_covered[index] && High(index) <= top;

            private void TakeStepCells(PlanRect rect, double top) {
                for (int row = rect.MinRow; row <= rect.MaxRow; row++) {
                    for (int column = rect.MinColumn; column <= rect.MaxColumn; column++) TakeStepCell(rect, row * _frame.Columns + column, top);
                }
            }

            private void TakeStepCell(PlanRect rect, int index, double top) {
                if (!IsOpenUnder(index, top)) return;

                _covered[index] = true;
                rect.Cells.Add(index);
            }

            private double HighestOf(PlanRect rect) {
                double highest = double.NegativeInfinity;
                foreach (int index in rect.Cells) highest = Math.Max(highest, High(index));
                return highest;
            }

            // Cells within the reach of a set cell (centre to centre), the set included.
            private bool[] Dilate(bool[] cells, double reach) {
                var result = new bool[_frame.Count];
                int radius = (int)Math.Ceiling(reach / _frame.Cell);
                double limit = reach * reach + 1e-9;
                for (int index = 0; index < _frame.Count; index++) {
                    if (cells[index]) Spread(index, radius, limit, result);
                }
                return result;
            }

            private void Spread(int index, int radius, double limit, bool[] result) {
                int column = index % _frame.Columns;
                int row = index / _frame.Columns;
                for (int rowOffset = -radius; rowOffset <= radius; rowOffset++) {
                    for (int columnOffset = -radius; columnOffset <= radius; columnOffset++) SpreadTo(column + columnOffset, row + rowOffset, columnOffset, rowOffset, limit, result);
                }
            }

            private void SpreadTo(int column, int row, int columnOffset, int rowOffset, double limit, bool[] result) {
                if (column < 0 || row < 0 || column >= _frame.Columns || row >= _frame.Rows) return;
                if ((columnOffset * columnOffset + rowOffset * rowOffset) * _frame.Cell * _frame.Cell > limit) return;

                result[row * _frame.Columns + column] = true;
            }

            // The rectangle from the lowest point of its cells up to top, rounded outward.
            private EnvelopeBox ToBox(PlanRect rect, double top) {
                double bottom = double.PositiveInfinity;
                foreach (int index in rect.Cells) bottom = Math.Min(bottom, Low(index));
                if (double.IsPositiveInfinity(bottom)) bottom = _settings.Ground;

                var box = new BoxEnvelopeCandidate(rect.Frame.YawDegrees);
                for (int corner = 0; corner < 4; corner++) {
                    rect.Corner(corner, out double x, out double z);
                    box.Include(x, bottom, z);
                    box.Include(x, Math.Max(top, bottom), z);
                }
                return box.ToBox(_settings.Snap);
            }
        }
    }
}
