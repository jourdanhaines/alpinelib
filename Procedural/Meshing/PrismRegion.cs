using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The part of a look a capsule can reach, from its lowest point to the jump reach, cut into thin
    /// <see cref="PrismLayer"/>s and fitted with <see cref="PrismPiece"/>s that each span a run of layers. A piece's
    /// plan is the hull of its fragments and of the walled-in cells of its region, enclosed to the vertex budget.
    /// Its standoff is the farthest exposed plan sample from the look, measured layer by layer, so a column over a
    /// leaning wall shows the lean. A piece over target is split either in plan (at its worst notch, or on a line
    /// joining two notches) or in height (on a layer edge), whichever leaves the smaller worst excess. Forced cuts go
    /// through ledges and sides the final metric finds.
    /// </summary>
    /// <remarks>Every fragment lands in some piece and every plan holds its fragments, so the pieces hold the region.</remarks>
    internal sealed class PrismRegion {
        private const double DistanceCap = 10.0;
        private const int OverSampleCount = 48;
        private const int TwoNotchTries = 4;

        // A split leaving a rectangle fallback is chosen only when nothing else divides the piece.
        private const double FallbackPenalty = 100.0;

        private readonly PrismEnvelopeSettings _settings;
        private double _target;
        private int _maxKind = PrismLayer.Reach;
        private readonly List<int> _groups = new List<int>();

        // Pairs whose merge missed target; targets only tighten, so they would miss again.
        private readonly HashSet<(PrismPiece, PrismPiece)> _failedMerges = new HashSet<(PrismPiece, PrismPiece)>();

        // A sample in a touched cell lies within a cell diagonal of the look.
        private readonly double _occupiedReach;

        public PrismRegion(IReadOnlyList<Vector3> corners, List<(double Bottom, double Top, int Kind, int Group)> slabs, PrismEnvelopeSettings settings) {
            _settings = settings;
            _target = settings.StandoffMaxTarget * settings.PieceStandoffShare;
            _occupiedReach = settings.Cell * 1.5;
            var all = new List<List<List<SlabPoint>>>();
            var everything = new List<List<SlabPoint>>();
            foreach ((double bottom, double top, int _, int _) in slabs) {
                List<List<SlabPoint>> fragments = PrismBandDecomposer.SlabFragments(corners, bottom, top);
                all.Add(fragments);
                everything.AddRange(fragments);
            }
            if (everything.Count == 0) return;

            Frame = PlanFootprint.FrameAround(everything, settings.Cell);
            for (int layer = 0; layer < slabs.Count; layer++) Layers.Add(new PrismLayer(slabs[layer].Bottom, slabs[layer].Top, slabs[layer].Kind, all[layer], Frame));
            foreach ((double _, double _, int _, int group) in slabs) _groups.Add(group);
        }

        public PlanFrame Frame { get; }
        public List<PrismLayer> Layers { get; } = new List<PrismLayer>();
        public List<PrismPiece> Pieces { get; } = new List<PrismPiece>();

        /// <summary>True when the region holds look.</summary>
        public bool HasLook => Layers.Count > 0;

        /// <summary>
        /// One piece per band group (feet, each fine band, the reach band), then merges where they stay within target;
        /// when the groups alone would pass <paramref name="budget"/>, one piece for the feet and one for the rest.
        /// </summary>
        public void Start(int budget) {
            List<List<PlanPoint>> whole = WholeRegion();
            int groups = 1;
            for (int layer = 1; layer < Layers.Count; layer++) groups += _groups[layer] != _groups[layer - 1] ? 1 : 0;
            if (groups * 2 > budget) {
                StartCoarsely(whole, budget);
                return;
            }

            int first = 0;
            for (int layer = 1; layer <= Layers.Count; layer++) {
                if (layer < Layers.Count && _groups[layer] == _groups[first]) continue;

                AddStartPiece(first, layer - 1, whole);
                first = layer;
            }
            Merge();
        }

        // Feet, walking band and reach band as one piece each, as far as the budget allows.
        private void StartCoarsely(List<List<PlanPoint>> whole, int budget) {
            int first = 0;
            if (budget >= 2 && Layers.Count > 1 && Layers[0].Kind == PrismLayer.Feet) {
                AddStartPiece(0, 0, whole);
                first = 1;
            }
            int reach = FirstReachLayer();
            if (budget >= 3 && reach > first && reach < Layers.Count) {
                AddStartPiece(first, reach - 1, whole);
                first = reach;
            }
            if (first < Layers.Count) AddStartPiece(first, Layers.Count - 1, whole);
        }

        private int FirstReachLayer() {
            for (int layer = 0; layer < Layers.Count; layer++) {
                if (Layers[layer].Kind == PrismLayer.Reach) return layer;
            }
            return Layers.Count;
        }

        private void AddStartPiece(int first, int last, List<List<PlanPoint>> whole) {
            var fragments = new List<List<List<SlabPoint>>>();
            for (int layer = first; layer <= last; layer++) fragments.Add(new List<List<SlabPoint>>(Layers[layer].Fragments));
            var piece = new PrismPiece(first, fragments, CopyRegion(whole));
            if (Evaluate(piece)) Pieces.Add(piece);
        }

        /// <summary>Drops every piece so the region can be fitted again from its layers.</summary>
        public void Reset() {
            Pieces.Clear();
            _failedMerges.Clear();
            _target = _settings.StandoffMaxTarget * _settings.PieceStandoffShare;
            _maxKind = PrismLayer.Reach;
        }

        /// <summary>
        /// Sets the standoff a piece may keep and the least important layer kind the phase answers for (layers of a
        /// higher kind are not measured), and re-measures every piece.
        /// </summary>
        public void SetPhase(double target, int maxKind) {
            // A merge that missed a target misses any tighter one, so failures are kept unless the target loosens.
            if (target > _target) _failedMerges.Clear();
            _target = target;
            _maxKind = maxKind;
            foreach (PrismPiece piece in Pieces) {
                piece.Unsplittable = false;
                Evaluate(piece);
            }
        }

        /// <summary>The piece furthest over target in the most important kind, or null when all meet it.</summary>
        public PrismPiece WorstOver() {
            PrismPiece worst = null;
            foreach (PrismPiece piece in Pieces) {
                if (piece.Unsplittable || !(piece.Excess > 0.0)) continue;
                if (worst == null || piece.Kind < worst.Kind || (piece.Kind == worst.Kind && piece.Excess > worst.Excess)) worst = piece;
            }
            return worst;
        }

        /// <summary>Splits the piece the way that leaves the smaller worst excess; false when no split divides it.</summary>
        public bool TrySplit(PrismPiece piece) {
            PrismPiece bestFirst = null, bestSecond = null;
            double bestScore = double.PositiveInfinity, bestTotal = double.PositiveInfinity;
            foreach ((PrismPiece first, PrismPiece second) in Candidates(piece)) {
                double score = Math.Max(first.Excess, second.Excess) + (first.FellBack || second.FellBack ? FallbackPenalty : 0.0);
                double total = Math.Max(0.0, first.Excess) + Math.Max(0.0, second.Excess);
                if (score > bestScore || (score == bestScore && !(total < bestTotal))) continue;

                bestScore = score;
                bestTotal = total;
                bestFirst = first;
                bestSecond = second;
            }
            if (bestFirst == null) {
                piece.Unsplittable = true;
                return false;
            }

            int index = Pieces.IndexOf(piece);
            Pieces[index] = bestFirst;
            Pieces.Insert(index + 1, bestSecond);
            return true;
        }

        /// <summary>
        /// Cuts the piece whose cap holds <paramref name="spot"/> (a ledge the final metric found) through that spot;
        /// false when no piece owns it or the cut divides nothing.
        /// </summary>
        public bool ForceCapCut(Vector3 spot) {
            foreach (PrismPiece piece in Pieces) {
                if (Math.Abs(piece.Top - spot.Y) > 0.05 || ConvexPlan.Distance(piece.Plan, spot.X, spot.Z) > 1e-3) continue;

                piece.CapPoint = new PlanPoint(spot.X, spot.Z);
                if (!TryForcedSides(piece, piece.CapPoint, piece.LastLayer, out PrismPiece first, out PrismPiece second)) return false;

                int index = Pieces.IndexOf(piece);
                Pieces[index] = first;
                Pieces.Insert(index + 1, second);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Steps the piece whose cap holds <paramref name="spot"/> down the rock: splits off its top layer so the lower part
        /// caps where its own layers' rock ends. False when no piece owns the cap or it spans a single layer.
        /// </summary>
        public bool ForceCapStep(Vector3 spot) {
            foreach (PrismPiece piece in Pieces) {
                if (Math.Abs(piece.Top - spot.Y) > 0.05 || ConvexPlan.Distance(piece.Plan, spot.X, spot.Z) > 1e-3) continue;
                if (piece.LastLayer == piece.FirstLayer || !TryHeightSides(piece, piece.LastLayer, out PrismPiece lower, out PrismPiece upper)) return false;

                int index = Pieces.IndexOf(piece);
                Pieces[index] = lower;
                Pieces.Insert(index + 1, upper);
                return true;
            }
            return false;
        }

        // At the notch under the spot (inward from its nearest edge), else across the edge through it, else below the top layer.
        private bool TryForcedSides(PrismPiece piece, PlanPoint spot, int layer, out PrismPiece first, out PrismPiece second) {
            first = second = null;
            if (TryCapLine(piece, out double normalX, out double normalZ, out double offset)) {
                var inward = new PlanPoint(normalZ, -normalX);
                if (ConvexPlan.Distance(piece.Plan, spot.X + inward.X * 0.01, spot.Z + inward.Z * 0.01) > 0.0) inward = new PlanPoint(-normalZ, normalX);
                if (TryNotchHit(piece, spot, inward, layer, out PlanPoint hit)) {
                    double lineX = -inward.Z, lineZ = inward.X;
                    if (TryPlanSides(piece, lineX, lineZ, lineX * hit.X + lineZ * hit.Z, out first, out second)) return true;
                }
                if (TryPlanSides(piece, normalX, normalZ, offset, out first, out second)) return true;
            }
            if (piece.LastLayer == piece.FirstLayer) return false;

            return TryHeightSides(piece, Math.Max(piece.FirstLayer + 1, Math.Min(piece.LastLayer, layer == piece.LastLayer ? layer : layer + 1)), out first, out second);
        }

        /// <summary>
        /// Cuts the piece whose side holds <paramref name="point"/> (the worst standoff the final metric found) at the
        /// notch inward of it; false when no piece owns it or no cut divides the piece.
        /// </summary>
        public bool ForceSideCut(Vector3 point) {
            foreach (PrismPiece piece in Pieces) {
                if (point.Y < piece.Bottom - 1e-3 || point.Y > piece.Top + 1e-3 || !IsOnRim(piece.Plan, point.X, point.Z)) continue;

                piece.CapPoint = new PlanPoint(point.X, point.Z);
                if (!TryForcedSides(piece, piece.CapPoint, LayerAt(piece, point.Y), out PrismPiece first, out PrismPiece second)) return false;

                int index = Pieces.IndexOf(piece);
                Pieces[index] = first;
                Pieces.Insert(index + 1, second);
                return true;
            }
            return false;
        }

        private static bool IsOnRim(List<PlanPoint> plan, double x, double z) {
            if (ConvexPlan.Distance(plan, x, z) > 1e-3) return false;

            for (int edge = 0; edge < plan.Count; edge++) {
                PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                if (Math.Abs((x - start.X) * edgeZ - (z - start.Z) * edgeX) / length <= 1e-3) return true;
            }
            return false;
        }

        private int LayerAt(PrismPiece piece, double y) {
            for (int layer = piece.FirstLayer; layer <= piece.LastLayer; layer++) {
                if (y <= Layers[layer].Top) return layer;
            }
            return piece.LastLayer;
        }

        /// <summary>Merges pairs of touching pieces, earliest pair first, while the merged piece meets target.</summary>
        public int Merge() {
            int merges = 0;
            for (int first = 0; first < Pieces.Count; first++) {
                for (int second = first + 1; second < Pieces.Count; second++) {
                    if (!TryMerge(first, second)) continue;

                    second = first;
                    merges++;
                }
            }
            return merges;
        }

        // Replaces pieces first and second by their merge when it meets target; failed pairs are remembered.
        private bool TryMerge(int first, int second) {
            PrismPiece one = Pieces[first], other = Pieces[second];
            if (!AreNear(one, other) || _failedMerges.Contains((one, other)) || !IsMeasured(one) || !IsMeasured(other)) return false;

            PrismPiece merged = BridgesHold(one, other) ? Merged(one, other) : null;
            if (merged == null || !Evaluate(merged) || merged.Excess > 0.0 || merged.FellBack) {
                _failedMerges.Add((one, other));
                return false;
            }

            Pieces[first] = merged;
            Pieces.RemoveAt(second);
            return true;
        }

        // True when every layer the piece spans is measured in this phase, so a merge with it can be judged whole.
        private bool IsMeasured(PrismPiece piece) {
            for (int layer = piece.FirstLayer; layer <= piece.LastLayer; layer++) {
                if (Layers[layer].Kind > _maxKind) return false;
            }
            return true;
        }

        // A child that kept the parent's layers and plan divided nothing.
        private static bool IsSame(PrismPiece child, PrismPiece parent) {
            if (child.FirstLayer != parent.FirstLayer || child.LastLayer != parent.LastLayer || child.Plan.Count != parent.Plan.Count) return false;

            for (int index = 0; index < child.Plan.Count; index++) {
                if (!child.Plan[index].Equals(parent.Plan[index])) return false;
            }
            return true;
        }

        // Cheap merge check: the edges the joint hull adds between the two plans must stay within target of the
        // look in every layer either piece spans (samples in walled-in cells excepted).
        private bool BridgesHold(PrismPiece first, PrismPiece second) {
            var points = new List<PlanPoint>(first.Plan.Count + second.Plan.Count);
            points.AddRange(first.Plan);
            points.AddRange(second.Plan);
            List<PlanPoint> hull = ConvexPlan.Hull(points);
            int low = Math.Min(first.FirstLayer, second.FirstLayer), high = Math.Max(first.LastLayer, second.LastLayer);
            for (int edge = 0; edge < hull.Count; edge++) {
                PlanPoint start = hull[edge], end = hull[(edge + 1) % hull.Count];
                if (IsPlanEdge(first.Plan, start, end) || IsPlanEdge(second.Plan, start, end)) continue;

                for (int sample = 1; sample < 4; sample++) {
                    double x = start.X + (end.X - start.X) * sample / 4.0, z = start.Z + (end.Z - start.Z) * sample / 4.0;
                    for (int layer = low; layer <= high; layer++) {
                        PlanFootprint footprint = Layers[layer].Footprint;
                        if (!footprint.IsInteriorAt(x, z) && footprint.Distance(x, z, _target + 1.0) > _target) return false;
                    }
                }
            }
            return true;
        }

        private static bool IsPlanEdge(List<PlanPoint> plan, PlanPoint start, PlanPoint end) {
            for (int index = 0; index < plan.Count; index++) {
                if (plan[index].Equals(start) && plan[(index + 1) % plan.Count].Equals(end)) return true;
            }
            return false;
        }

        // Notch cut, two-notch cuts, layer splits around the worst layer, and the axis cut when nothing else divides.
        private IEnumerable<(PrismPiece First, PrismPiece Second)> Candidates(PrismPiece piece) {
            bool found = false;
            if (piece.Standoff > _target && TryNotchLine(piece, out double normalX, out double normalZ, out double offset)) {
                if (TryPlanSides(piece, normalX, normalZ, offset, out PrismPiece first, out PrismPiece second)) {
                    found = true;
                    yield return (first, second);
                }
            }

            foreach ((double lineX, double lineZ, double lineOffset) in TwoNotchLines(piece)) {
                if (!TryPlanSides(piece, lineX, lineZ, lineOffset, out PrismPiece first, out PrismPiece second)) continue;

                found = true;
                yield return (first, second);
            }

            foreach (int edge in LayerEdges(piece)) {
                if (!TryHeightSides(piece, edge, out PrismPiece lower, out PrismPiece upper)) continue;

                found = true;
                yield return (lower, upper);
            }
            if (found) yield break;

            AxisLine(piece.Plan, out normalX, out normalZ, out offset);
            if (TryPlanSides(piece, normalX, normalZ, offset, out PrismPiece axisFirst, out PrismPiece axisSecond)) yield return (axisFirst, axisSecond);
        }

        // Layer edges worth splitting on: either side of the worst layer, and the middle of the run.
        private static List<int> LayerEdges(PrismPiece piece) {
            var edges = new List<int>();
            if (piece.LastLayer == piece.FirstLayer) return edges;

            foreach (int edge in new[] { piece.WorstLayer, piece.WorstLayer + 1, (piece.FirstLayer + piece.LastLayer + 1) / 2 }) {
                if (edge > piece.FirstLayer && edge <= piece.LastLayer && !edges.Contains(edge)) edges.Add(edge);
            }
            return edges;
        }

        private bool TryHeightSides(PrismPiece piece, int edge, out PrismPiece lower, out PrismPiece upper) {
            lower = new PrismPiece(piece.FirstLayer, piece.LayerFragments.GetRange(0, edge - piece.FirstLayer), CopyRegion(piece.Region));
            upper = new PrismPiece(edge, piece.LayerFragments.GetRange(edge - piece.FirstLayer, piece.LastLayer - edge + 1), CopyRegion(piece.Region));
            return Evaluate(lower) && Evaluate(upper) && !IsSame(lower, piece) && !IsSame(upper, piece);
        }

        private bool TryPlanSides(PrismPiece piece, double normalX, double normalZ, double offset, out PrismPiece first, out PrismPiece second) {
            first = Side(piece, normalX, normalZ, offset);
            second = Side(piece, -normalX, -normalZ, -offset);
            return first != null && second != null && Evaluate(first) && Evaluate(second) && !IsSame(first, piece) && !IsSame(second, piece);
        }

        // The piece's part on the side normal·p ≤ offset, or null when its region has nothing there.
        private static PrismPiece Side(PrismPiece piece, double normalX, double normalZ, double offset) {
            var region = new List<List<PlanPoint>>();
            foreach (List<PlanPoint> part in piece.Region) {
                List<PlanPoint> clipped = ConvexPlan.ClipHalfPlane(part, normalX, normalZ, offset);
                if (clipped.Count >= 3) region.Add(clipped);
            }
            var fragments = new List<List<List<SlabPoint>>>();
            bool any = false;
            foreach (List<List<SlabPoint>> layer in piece.LayerFragments) {
                var clippedLayer = new List<List<SlabPoint>>();
                foreach (List<SlabPoint> fragment in layer) {
                    List<SlabPoint> clipped = PrismBandDecomposer.Clip(fragment, normalX, 0.0, normalZ, offset);
                    if (clipped.Count > 0) clippedLayer.Add(clipped);
                }
                any |= clippedLayer.Count > 0;
                fragments.Add(clippedLayer);
            }
            if (!any && region.Count == 0) return null;

            return new PrismPiece(piece.FirstLayer, fragments, region);
        }

        private static PrismPiece Merged(PrismPiece first, PrismPiece second) {
            int low = Math.Min(first.FirstLayer, second.FirstLayer), high = Math.Max(first.LastLayer, second.LastLayer);
            var fragments = new List<List<List<SlabPoint>>>();
            for (int layer = low; layer <= high; layer++) {
                var merged = new List<List<SlabPoint>>();
                if (layer >= first.FirstLayer && layer <= first.LastLayer) merged.AddRange(first.Fragments(layer));
                if (layer >= second.FirstLayer && layer <= second.LastLayer) merged.AddRange(second.Fragments(layer));
                fragments.Add(merged);
            }
            var region = CopyRegion(first.Region);
            region.AddRange(CopyRegion(second.Region));
            return new PrismPiece(low, fragments, region);
        }

        private static bool AreNear(PrismPiece first, PrismPiece second) {
            if (second.FirstLayer > first.LastLayer + 1 || first.FirstLayer > second.LastLayer + 1) return false;

            Bounds(first.Plan, out double minX, out double maxX, out double minZ, out double maxZ);
            Bounds(second.Plan, out double otherMinX, out double otherMaxX, out double otherMinZ, out double otherMaxZ);
            const double margin = 0.05;
            return otherMinX <= maxX + margin && minX <= otherMaxX + margin && otherMinZ <= maxZ + margin && minZ <= otherMaxZ + margin;
        }

        /// <summary>Plans the piece, sets its heights, standoff, cap and excess; false when it holds nothing.</summary>
        public bool Evaluate(PrismPiece piece) {
            List<PlanPoint> points = new List<PlanPoint>();
            double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
            int firstFull = int.MaxValue, lastFull = int.MinValue;
            var spans = new Dictionary<(int Part, int Row), (int First, int Last)>();
            for (int layer = piece.FirstLayer; layer <= piece.LastLayer; layer++) {
                int before = points.Count;
                foreach (List<SlabPoint> fragment in piece.Fragments(layer)) {
                    foreach (SlabPoint point in fragment) {
                        points.Add(new PlanPoint(point.X, point.Z));
                        bottom = Math.Min(bottom, point.Y);
                        top = Math.Max(top, point.Y);
                    }
                }
                bool interior = FoldInteriorSpans(piece.Region, Layers[layer].Footprint, spans);
                if (points.Count > before || interior) {
                    firstFull = Math.Min(firstFull, layer);
                    lastFull = Math.Max(lastFull, layer);
                }
                if (!interior) continue;

                bottom = Math.Min(bottom, Layers[layer].Bottom);
                top = Math.Max(top, Layers[layer].Top);
            }
            AddSpanCorners(spans, points);
            if (points.Count == 0) return false;

            piece.Trim(firstFull, lastFull);
            piece.Plan = PrismBandDecomposer.FinishPlan(points, _settings, out bool fellBack);
            piece.FellBack = fellBack;
            piece.Bottom = Math.Floor(bottom / ConvexPlan.Grid) * ConvexPlan.Grid;
            piece.Top = Math.Ceiling(top / ConvexPlan.Grid) * ConvexPlan.Grid;
            MeasureStandoff(piece);
            piece.Excess = piece.Standoff - _target;
            return true;
        }

        // Per row of each convex part, the first and last walled-in cell inside it over all layers: enough for the hull.
        private bool FoldInteriorSpans(List<List<PlanPoint>> region, PlanFootprint footprint, Dictionary<(int Part, int Row), (int First, int Last)> spans) {
            bool added = false;
            for (int partIndex = 0; partIndex < region.Count; partIndex++) {
                List<PlanPoint> part = region[partIndex];
                Bounds(part, out double _, out double _, out double minZ, out double maxZ);
                int firstRow = Math.Max(0, (int)Math.Floor((minZ - Frame.OriginZ) / Frame.Cell));
                int lastRow = Math.Min(Frame.Rows - 1, (int)Math.Floor((maxZ - Frame.OriginZ) / Frame.Cell));
                for (int row = firstRow; row <= lastRow; row++) added |= FoldRow(part, partIndex, row, footprint, spans);
            }
            return added;
        }

        private bool FoldRow(List<PlanPoint> part, int partIndex, int row, PlanFootprint footprint, Dictionary<(int Part, int Row), (int First, int Last)> spans) {
            double z = Frame.OriginZ + (row + 0.5) * Frame.Cell;
            if (!TrySpanAt(part, z, out double fromX, out double toX)) return false;

            int fromColumn = Math.Max(0, (int)Math.Ceiling((fromX - Frame.OriginX) / Frame.Cell - 0.5));
            int toColumn = Math.Min(Frame.Columns - 1, (int)Math.Floor((toX - Frame.OriginX) / Frame.Cell - 0.5));
            if (fromColumn > toColumn || !footprint.TryInteriorSpan(row, fromColumn, toColumn, out int first, out int last)) return false;

            if (spans.TryGetValue((partIndex, row), out (int First, int Last) span)) {
                first = Math.Min(first, span.First);
                last = Math.Max(last, span.Last);
            }
            spans[(partIndex, row)] = (first, last);
            return true;
        }

        // Corners of the folded span ends, in part and row order so the hull input is the same on every run.
        private void AddSpanCorners(Dictionary<(int Part, int Row), (int First, int Last)> spans, List<PlanPoint> points) {
            var keys = new List<(int Part, int Row)>(spans.Keys);
            keys.Sort();
            foreach ((int Part, int Row) key in keys) {
                (int first, int last) = spans[key];
                AddCellCorners(first, key.Row, points);
                if (last != first) AddCellCorners(last, key.Row, points);
            }
        }

        // Where the horizontal line at z crosses a convex part.
        private static bool TrySpanAt(List<PlanPoint> part, double z, out double fromX, out double toX) {
            fromX = double.PositiveInfinity;
            toX = double.NegativeInfinity;
            for (int edge = 0; edge < part.Count; edge++) {
                PlanPoint start = part[edge], end = part[(edge + 1) % part.Count];
                if ((start.Z - z) * (end.Z - z) > 0.0 || start.Z == end.Z) continue;

                double x = start.X + (end.X - start.X) * (z - start.Z) / (end.Z - start.Z);
                fromX = Math.Min(fromX, x);
                toX = Math.Max(toX, x);
            }
            return fromX <= toX;
        }

        private void AddCellCorners(int column, int row, List<PlanPoint> points) {
            for (int corner = 0; corner < 4; corner++) {
                Frame.PointIn(column, row, corner == 1 || corner == 2 ? 1.0 : 0.0, corner >= 2 ? 1.0 : 0.0, out double x, out double z);
                points.Add(new PlanPoint(x, z));
            }
        }

        // The farthest plan sample from the look over the spanned layers, skipping samples walled in by a layer.
        private void MeasureStandoff(PrismPiece piece) {
            List<PlanPoint> plan = piece.Plan;
            piece.OverSamples.Clear();
            double worst = -1.0;
            int kind = int.MaxValue;
            for (int edge = 0; edge < plan.Count; edge++) {
                PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                int samples = (int)Math.Ceiling(length / _settings.StandoffSpacing) + 1;
                for (int sample = 0; sample < samples; sample++) {
                    double share = (double)sample / (samples - 1);
                    double x = start.X + edgeX * share, z = start.Z + edgeZ * share;
                    for (int layer = piece.FirstLayer; layer <= piece.LastLayer; layer++) {
                        PlanFootprint footprint = Layers[layer].Footprint;
                        if (footprint.IsOccupiedAt(x, z) && (worst >= _occupiedReach || footprint.IsInteriorAt(x, z))) continue;

                        double distance = footprint.Distance(x, z, DistanceCap);
                        if (distance > _target) {
                            kind = Math.Min(kind, Layers[layer].Kind);
                            KeepOverSample(piece, new PlanPoint(x, z), new PlanPoint(-edgeZ / length, edgeX / length), layer, distance);
                        }
                        if (!(distance > worst)) continue;

                        worst = distance;
                        piece.WorstLayer = layer;
                        piece.WorstPoint = new PlanPoint(x, z);
                        piece.WorstInward = new PlanPoint(-edgeZ / length, edgeX / length);
                    }
                }
            }
            piece.Standoff = Math.Max(0.0, worst);
            piece.Kind = kind == int.MaxValue ? Layers[piece.LastLayer].Kind : kind;
        }

        // Keeps the deepest over-target samples, one per edge sample point (its deepest layer), deepest first.
        private static void KeepOverSample(PrismPiece piece, PlanPoint point, PlanPoint inward, int layer, double distance) {
            List<(PlanPoint Point, PlanPoint Inward, int Layer, double Distance)> samples = piece.OverSamples;
            int count = samples.Count;
            if (count > 0 && samples[count - 1].Point.Equals(point)) {
                if (distance <= samples[count - 1].Distance) return;

                samples.RemoveAt(count - 1);
            }
            int index = samples.Count;
            while (index > 0 && samples[index - 1].Distance < distance) index--;
            if (index >= OverSampleCount) return;

            samples.Insert(index, (point, inward, layer, distance));
            if (samples.Count > OverSampleCount) samples.RemoveAt(samples.Count - 1);
        }

        // From the worst sample, inward along its edge normal to the first footprint cell of its layer; the line runs along the march.
        private bool TryNotchLine(PrismPiece piece, out double normalX, out double normalZ, out double offset) {
            double inX = piece.WorstInward.X, inZ = piece.WorstInward.Z;
            PlanFootprint footprint = Layers[piece.WorstLayer].Footprint;
            double step = Frame.Cell * 0.5;
            int maxSteps = (int)Math.Ceiling(PrismBandDecomposer.Diameter(piece.Plan) / step) + 1;
            double hitX = piece.WorstPoint.X, hitZ = piece.WorstPoint.Z;
            for (int index = 1; index < maxSteps; index++) {
                double x = piece.WorstPoint.X + inX * step * index, z = piece.WorstPoint.Z + inZ * step * index;
                if (ConvexPlan.Distance(piece.Plan, x, z) > 0.0) break;

                hitX = x;
                hitZ = z;
                if (footprint.IsOccupiedAt(x, z)) break;
            }
            normalX = -inZ;
            normalZ = inX;
            offset = normalX * hitX + normalZ * hitZ;
            return !(hitX == piece.WorstPoint.X && hitZ == piece.WorstPoint.Z);
        }

        // Lines from the worst notch to the notch of a deep sample on an edge facing it, so one cut serves two notches.
        private List<(double NormalX, double NormalZ, double Offset)> TwoNotchLines(PrismPiece piece) {
            var lines = new List<(double, double, double)>();
            if (!TryNotchHit(piece, piece.WorstPoint, piece.WorstInward, piece.WorstLayer, out PlanPoint start)) return lines;

            foreach ((PlanPoint point, PlanPoint inward, int layer, double _) in piece.OverSamples) {
                if (lines.Count >= TwoNotchTries) break;
                if (inward.X * piece.WorstInward.X + inward.Z * piece.WorstInward.Z > -0.3) continue;
                if (!TryNotchHit(piece, point, inward, layer, out PlanPoint end)) continue;

                double alongX = end.X - start.X, alongZ = end.Z - start.Z;
                double length = Math.Sqrt(alongX * alongX + alongZ * alongZ);
                if (length < Frame.Cell * 4.0) continue;

                double normalX = -alongZ / length, normalZ = alongX / length;
                lines.Add((normalX, normalZ, normalX * start.X + normalZ * start.Z));
            }
            return lines;
        }

        // The first footprint cell met marching inward from a boundary sample, within the plan.
        private bool TryNotchHit(PrismPiece piece, PlanPoint from, PlanPoint inward, int layer, out PlanPoint hit) {
            PlanFootprint footprint = Layers[layer].Footprint;
            double step = Frame.Cell * 0.5;
            int maxSteps = (int)Math.Ceiling(PrismBandDecomposer.Diameter(piece.Plan) / step) + 1;
            hit = from;
            for (int index = 1; index < maxSteps; index++) {
                double x = from.X + inward.X * step * index, z = from.Z + inward.Z * step * index;
                if (ConvexPlan.Distance(piece.Plan, x, z) > 0.0) return false;

                hit = new PlanPoint(x, z);
                if (footprint.IsOccupiedAt(x, z)) return true;
            }
            return false;
        }

        // The line through the cap's worst spot along the inward normal of the plan edge nearest it.
        private static bool TryCapLine(PrismPiece piece, out double normalX, out double normalZ, out double offset) {
            List<PlanPoint> plan = piece.Plan;
            PlanPoint spot = piece.CapPoint;
            double nearest = double.PositiveInfinity;
            normalX = 1.0;
            normalZ = 0.0;
            for (int edge = 0; edge < plan.Count; edge++) {
                PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                double distance = Math.Abs((spot.X - start.X) * edgeZ - (spot.Z - start.Z) * edgeX) / length;
                if (!(distance < nearest)) continue;

                nearest = distance;
                normalX = edgeX / length;
                normalZ = edgeZ / length;
            }
            offset = normalX * spot.X + normalZ * spot.Z;
            return nearest < double.PositiveInfinity;
        }

        // The line through the plan's vertex centroid across its longest vertex-to-vertex axis.
        private static void AxisLine(List<PlanPoint> plan, out double normalX, out double normalZ, out double offset) {
            double bestLength = -1.0;
            normalX = 1.0;
            normalZ = 0.0;
            for (int first = 0; first < plan.Count; first++) {
                for (int second = first + 1; second < plan.Count; second++) {
                    double axisX = plan[second].X - plan[first].X, axisZ = plan[second].Z - plan[first].Z;
                    double length = Math.Sqrt(axisX * axisX + axisZ * axisZ);
                    if (!(length > bestLength)) continue;

                    bestLength = length;
                    normalX = axisX / length;
                    normalZ = axisZ / length;
                }
            }
            double centroidX = 0.0, centroidZ = 0.0;
            foreach (PlanPoint point in plan) {
                centroidX += point.X;
                centroidZ += point.Z;
            }
            offset = normalX * centroidX / plan.Count + normalZ * centroidZ / plan.Count;
        }

        private List<List<PlanPoint>> WholeRegion() {
            var corners = new List<PlanPoint>(4);
            for (int corner = 0; corner < 4; corner++) {
                Frame.PointIn(corner == 1 || corner == 2 ? Frame.Columns : 0, corner >= 2 ? Frame.Rows : 0, 0.0, 0.0, out double x, out double z);
                corners.Add(new PlanPoint(x, z));
            }
            return new List<List<PlanPoint>> { corners };
        }

        private static List<List<PlanPoint>> CopyRegion(List<List<PlanPoint>> region) {
            var copy = new List<List<PlanPoint>>(region.Count);
            foreach (List<PlanPoint> part in region) copy.Add(new List<PlanPoint>(part));
            return copy;
        }

        private static void Bounds(List<PlanPoint> plan, out double minX, out double maxX, out double minZ, out double maxZ) {
            minX = minZ = double.PositiveInfinity;
            maxX = maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in plan) {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
        }
    }
}
