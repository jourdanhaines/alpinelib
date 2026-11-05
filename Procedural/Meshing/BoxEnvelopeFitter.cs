using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Fits at most <see cref="BoxEnvelopeSettings.MaxBoxes"/> yawed boxes whose union holds a triangle soup, for
    /// colliders a look never pokes out of. Each connected piece of the soup gets its best yaw (least plan
    /// area) and one box per height band of its triangles clipped to that band; then the pair whose merge adds
    /// the least volume merges, again and again, refusing any merge that would swallow a keep-out point.
    /// </summary>
    /// <remarks>
    /// Every box holds the clipped triangles of its band and every merge holds both parts, so the union holds
    /// the whole soup by construction; the final rounding only grows boxes. Double maths, editor or bake time:
    /// results are data, not something every runtime must reproduce.
    /// </remarks>
    public static class BoxEnvelopeFitter {
        private const int QuarterTurn = 90;

        /// <summary>The boxes around the faces in <paramref name="corners"/> (three per face).</summary>
        public static List<EnvelopeBox> Fit(IReadOnlyList<Vector3> corners, BoxEnvelopeSettings settings) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (corners.Count % 3 != 0) throw new ArgumentException("Corners must come three per face.", nameof(corners));

            settings.Validate();
            var boxes = new List<BoxEnvelopeCandidate>();
            foreach (List<int> piece in TrianglePieces.Split(corners)) {
                AddBands(corners, piece, BestYaw(corners, piece, settings.YawStepDegrees), settings.BandEdges, boxes);
            }
            Merge(boxes, KeepOutCeilings(boxes, settings), settings);

            var result = new List<EnvelopeBox>(boxes.Count);
            foreach (BoxEnvelopeCandidate box in boxes) result.Add(box.ToBox(settings.Snap));
            return result;
        }

        /// <summary>How far <paramref name="point"/> lies outside every box of <paramref name="boxes"/>; 0 inside any.</summary>
        public static double DistanceOutside(IReadOnlyList<EnvelopeBox> boxes, Vector3 point) {
            double best = double.PositiveInfinity;
            foreach (EnvelopeBox box in boxes) best = Math.Min(best, box.DistanceOutside(point));
            return best;
        }

        // The yaw in [0, 90) whose plan box around the piece is smallest; ties keep the lower yaw.
        private static int BestYaw(IReadOnlyList<Vector3> corners, List<int> piece, int step) {
            int best = 0;
            double bestArea = double.PositiveInfinity;
            for (int yaw = 0; yaw < QuarterTurn; yaw += step) {
                var box = new BoxEnvelopeCandidate(yaw);
                foreach (int face in piece) IncludeFace(box, corners, face);
                double area = (box.MaxX - box.MinX) * (box.MaxZ - box.MinZ);
                if (area >= bestArea) continue;

                bestArea = area;
                best = yaw;
            }
            return best;
        }

        private static void IncludeFace(BoxEnvelopeCandidate box, IReadOnlyList<Vector3> corners, int face) {
            for (int corner = 0; corner < 3; corner++) Include(box, corners[face * 3 + corner]);
        }

        // One box per band the piece reaches, holding the piece's faces clipped to that band.
        private static void AddBands(IReadOnlyList<Vector3> corners, List<int> piece, int yaw, IReadOnlyList<double> edges, List<BoxEnvelopeCandidate> boxes) {
            var clipped = new List<Vector3>(8);
            for (int band = 0; band <= edges.Count; band++) {
                double bottom = band == 0 ? double.NegativeInfinity : edges[band - 1];
                double top = band == edges.Count ? double.PositiveInfinity : edges[band];
                var box = new BoxEnvelopeCandidate(yaw);
                foreach (int face in piece) IncludeClipped(box, corners, face, bottom, top, clipped);
                if (!box.IsEmpty) boxes.Add(box);
            }
        }

        private static void IncludeClipped(BoxEnvelopeCandidate box, IReadOnlyList<Vector3> corners, int face, double bottom, double top, List<Vector3> clipped) {
            ClipToBand(corners[face * 3], corners[face * 3 + 1], corners[face * 3 + 2], bottom, top, clipped);
            if (LiesOnBottom(clipped, bottom)) return;

            foreach (Vector3 point in clipped) Include(box, point);
        }

        // A polygon flat on the band's bottom edge already belongs to the band below.
        private static bool LiesOnBottom(List<Vector3> polygon, double bottom) {
            if (polygon.Count == 0 || double.IsNegativeInfinity(bottom)) return false;

            foreach (Vector3 point in polygon) {
                if (point.Y > bottom) return false;
            }
            return true;
        }

        // The polygon of triangle (first, second, third) between heights bottom and top (empty when it misses the band).
        private static void ClipToBand(Vector3 first, Vector3 second, Vector3 third, double bottom, double top, List<Vector3> output) {
            output.Clear();
            float low = Math.Min(first.Y, Math.Min(second.Y, third.Y));
            float high = Math.Max(first.Y, Math.Max(second.Y, third.Y));
            if (high < bottom || low > top) return;

            output.Add(first);
            output.Add(second);
            output.Add(third);
            if (low >= bottom && high <= top) return;

            var input = new List<Vector3>(output);
            ClipAgainst(input, output, bottom, true);
            input.Clear();
            input.AddRange(output);
            ClipAgainst(input, output, top, false);
        }

        // Sutherland–Hodgman against the plane y = level, keeping the side above it (or below it).
        private static void ClipAgainst(List<Vector3> input, List<Vector3> output, double level, bool keepAbove) {
            output.Clear();
            for (int index = 0; index < input.Count; index++) {
                Vector3 current = input[index];
                Vector3 next = input[(index + 1) % input.Count];
                bool currentIn = keepAbove ? current.Y >= level : current.Y <= level;
                bool nextIn = keepAbove ? next.Y >= level : next.Y <= level;
                if (currentIn) output.Add(current);
                if (currentIn == nextIn) continue;

                double share = (level - current.Y) / (next.Y - current.Y);
                output.Add(new Vector3((float)(current.X + (next.X - current.X) * share), (float)level, (float)(current.Z + (next.Z - current.Z) * share)));
            }
        }

        private static void Include(BoxEnvelopeCandidate box, Vector3 point) {
            box.Include(point.X, point.Y, point.Z);
        }

        // Each keep-out point with the highest top a box holding it may reach: its own height, or the top of the
        // tallest starting box that already holds it (the geometry needs that much, a merge may not add more).
        private static List<Vector4> KeepOutCeilings(List<BoxEnvelopeCandidate> boxes, BoxEnvelopeSettings settings) {
            var ceilings = new List<Vector4>(settings.KeepOut.Count);
            foreach (Vector3 point in settings.KeepOut) ceilings.Add(new Vector4(point, (float)CeilingOf(point, boxes, settings.KeepOutTolerance)));
            return ceilings;
        }

        private static double CeilingOf(Vector3 point, List<BoxEnvelopeCandidate> boxes, double tolerance) {
            double ceiling = point.Y;
            foreach (BoxEnvelopeCandidate box in boxes) {
                if (box.Holds(point, tolerance)) ceiling = Math.Max(ceiling, box.MaxY);
            }
            return ceiling;
        }

        // Greedy: merge the cheapest pair until the count fits. A pair that would swallow a keep-out point is refused
        // while any other pair may merge; the count is a hard limit, so only then do refused pairs merge.
        private static void Merge(List<BoxEnvelopeCandidate> boxes, List<Vector4> keepOut, BoxEnvelopeSettings settings) {
            int count = boxes.Count;
            var merged = new BoxEnvelopeCandidate[count, count];
            var cost = new double[count, count];
            for (int first = 0; first < count; first++) {
                for (int second = first + 1; second < count; second++) Price(boxes, settings, merged, cost, first, second);
            }

            var alive = new bool[count];
            for (int index = 0; index < count; index++) alive[index] = true;
            int live = count;
            var refused = new bool[count, count];
            while (live > settings.MaxBoxes) {
                bool honour = TryCheapest(alive, cost, refused, out int keep, out int drop);
                if (!honour && !TryCheapest(alive, cost, null, out keep, out drop)) break;

                if (honour && SwallowsAny(merged[keep, drop], keepOut, settings.KeepOutTolerance)) {
                    refused[keep, drop] = true;
                    continue;
                }

                boxes[keep] = merged[keep, drop];
                alive[drop] = false;
                live--;
                Reprice(boxes, settings, merged, cost, refused, alive, keep);
            }

            for (int index = count - 1; index >= 0; index--) {
                if (!alive[index]) boxes.RemoveAt(index);
            }
        }

        // Every live pair with the grown box gets a fresh price and loses any earlier refusal.
        private static void Reprice(List<BoxEnvelopeCandidate> boxes, BoxEnvelopeSettings settings, BoxEnvelopeCandidate[,] merged, double[,] cost, bool[,] refused,
                                    bool[] alive, int keep) {
            for (int other = 0; other < alive.Length; other++) {
                if (!alive[other] || other == keep) continue;

                refused[Math.Min(keep, other), Math.Max(keep, other)] = false;
                Price(boxes, settings, merged, cost, Math.Min(keep, other), Math.Max(keep, other));
            }
        }

        // The cheapest live pair, skipping refused pairs unless refused is null.
        private static bool TryCheapest(bool[] alive, double[,] cost, bool[,] refused, out int keep, out int drop) {
            keep = -1;
            drop = -1;
            double best = double.PositiveInfinity;
            int count = alive.Length;
            for (int first = 0; first < count; first++) {
                if (alive[first]) CheapestWith(first, alive, cost, refused, ref best, ref keep, ref drop);
            }
            return keep >= 0;
        }

        private static void CheapestWith(int first, bool[] alive, double[,] cost, bool[,] refused, ref double best, ref int keep, ref int drop) {
            for (int second = first + 1; second < alive.Length; second++) {
                if (!alive[second] || cost[first, second] >= best || (refused != null && refused[first, second])) continue;

                best = cost[first, second];
                keep = first;
                drop = second;
            }
        }

        // The cheaper of the two yaw frames for the pair's merge, priced by the volume it adds, volume below the
        // weighted height counting extra (keep-out is checked once picked).
        private static void Price(List<BoxEnvelopeCandidate> boxes, BoxEnvelopeSettings settings, BoxEnvelopeCandidate[,] merged, double[,] cost, int first, int second) {
            BoxEnvelopeCandidate firstBox = boxes[first];
            BoxEnvelopeCandidate secondBox = boxes[second];
            BoxEnvelopeCandidate inFirst = Union(firstBox, secondBox, firstBox.YawDegrees);
            BoxEnvelopeCandidate union = inFirst;
            if (secondBox.YawDegrees != firstBox.YawDegrees) {
                BoxEnvelopeCandidate inSecond = Union(firstBox, secondBox, secondBox.YawDegrees);
                if (inSecond.Volume < inFirst.Volume) union = inSecond;
            }
            merged[first, second] = union;
            double added = union.Volume - firstBox.Volume - secondBox.Volume + firstBox.Overlap(secondBox);
            double height = settings.WeightedBelow;
            double addedLow = union.VolumeBelow(height) - firstBox.VolumeBelow(height) - secondBox.VolumeBelow(height) + firstBox.OverlapBelow(secondBox, height);
            cost[first, second] = added + (settings.WeightBelow - 1.0) * Math.Max(0.0, addedLow);
        }

        private static BoxEnvelopeCandidate Union(BoxEnvelopeCandidate first, BoxEnvelopeCandidate second, int yaw) {
            var union = new BoxEnvelopeCandidate(yaw);
            union.IncludeBox(first);
            union.IncludeBox(second);
            return union;
        }

        // True when the box holds a keep-out point and rises above that point's ceiling.
        private static bool SwallowsAny(BoxEnvelopeCandidate box, List<Vector4> keepOut, double tolerance) {
            foreach (Vector4 entry in keepOut) {
                if (box.MaxY > entry.W + tolerance && box.Holds(new Vector3(entry.X, entry.Y, entry.Z), tolerance)) return true;
            }
            return false;
        }
    }
}
