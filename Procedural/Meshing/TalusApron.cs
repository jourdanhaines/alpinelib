using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A talus apron round one convex counter-clockwise foot as sloped prisms: from the wall foot at a rise down to a toe
    /// (rise + sink) / slope out. Each exposed edge stretch is one trapezoid; stretches meeting at a convex corner are
    /// joined by a sector fan; every other stretch end is closed by an end fan whose last ray runs back to the foot (so it
    /// lies in the rock). Fan steps keep each facet under the gradient cap. Pieces share their vertices at equal heights,
    /// so the apron is seamless. A piece the caller's clear test refuses breaks its chain there and the chains are laid
    /// again. Square roots only; vertices rounded to <see cref="ConvexPlan.Grid"/>.
    /// </summary>
    public sealed class TalusApron {
        private const double EndTolerance = 1e-9;
        private const double MergeReach = 0.1;
        private const double SliverCross = 0.01;
        private const double OpenTolerance = 1e-3;
        private const int CrossingSteps = 40;
        private const double SeamKeyScale = 1000.0;

        private readonly TalusApronSettings _settings;
        private readonly double _rise;
        private readonly double _slope;
        private readonly double _width;
        private readonly double _fanStepCosine;
        private readonly List<TalusBareStretch> _bare = new List<TalusBareStretch>();

        private TalusApron(double rise, double slope, in TalusApronSettings settings) {
            _settings = settings;
            _rise = rise;
            _slope = slope;
            _width = (rise + settings.ToeSink) / slope;
            // A triangle turning θ about the apex is slope / cos(θ / 2) steep: the widest turn that keeps it under the cap.
            double half = Math.Min(1.0, slope / settings.GradientMax * settings.FanGradientMargin);
            _fanStepCosine = 2.0 * half * half - 1.0;
        }

        /// <summary>
        /// Lays the apron round <paramref name="foot"/> at <paramref name="rise"/> and <paramref name="slope"/> (rise over
        /// width). Edge stretches covered by <paramref name="others"/> (probed just outside the foot) take none; edges
        /// <paramref name="isBareEdge"/> names (by index, edge i from vertex i to i + 1) and stretches near
        /// <paramref name="keepOffs"/> stay bare; a part with a piece <paramref name="isClear"/> refuses is dropped.
        /// </summary>
        public static TalusApronResult Lay(IReadOnlyList<PlanPoint> foot, IReadOnlyList<IReadOnlyList<PlanPoint>> others, double rise, double slope, Func<int, bool> isBareEdge,
                                           IReadOnlyList<IReadOnlyList<PlanPoint>> keepOffs, Func<SlopedPrismPiece, bool> isClear, in TalusApronSettings settings) {
            if (foot == null) throw new ArgumentNullException(nameof(foot));
            if (others == null) throw new ArgumentNullException(nameof(others));
            if (isBareEdge == null) throw new ArgumentNullException(nameof(isBareEdge));
            if (keepOffs == null) throw new ArgumentNullException(nameof(keepOffs));
            if (isClear == null) throw new ArgumentNullException(nameof(isClear));
            if (!(slope > 0.0)) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope must be positive.");
            if (!(rise >= 0.0)) throw new ArgumentOutOfRangeException(nameof(rise), rise, "Rise must be a number at least zero.");
            if (!settings.IsBuilt) throw new ArgumentException("Settings were never constructed.", nameof(settings));
            if (foot.Count < 3) return new TalusApronResult(true, new List<TalusApronPart>(), new List<TalusBareStretch>());

            return new TalusApron(rise, slope, settings).Lay(foot, others, isBareEdge, keepOffs, isClear);
        }

        // A piece that misses its clearances breaks the chains there (a run or its end fan: the run goes bare; a corner fan:
        // two end fans instead) and the chains are built again, at most once per run and corner.
        private TalusApronResult Lay(IReadOnlyList<PlanPoint> plan, IReadOnlyList<IReadOnlyList<PlanPoint>> others, Func<int, bool> isBareEdge,
                                     IReadOnlyList<IReadOnlyList<PlanPoint>> keepOffs, Func<SlopedPrismPiece, bool> isClear) {
            int count = plan.Count;
            var runs = new List<TalusRun>[count];
            for (int edge = 0; edge < count; edge++) runs[edge] = EdgeRuns(plan, others, isBareEdge, keepOffs, edge);
            var blocked = new bool[count];
            int passes = count * 4 + 4;
            for (int pass = 0; pass < passes; pass++) {
                var joined = new bool[count];
                for (int vertex = 0; vertex < count; vertex++) joined[vertex] = !blocked[vertex] && IsJoined(runs, plan, vertex);
                List<TalusDraft> drafts = Chains(plan, runs, joined);
                TalusDraft failed = drafts.Find((draft) => !IsClear(draft, isClear));
                if (failed == null && HasSeamStep(drafts, _settings.SeamStep)) return new TalusApronResult(false, new List<TalusApronPart>(), new List<TalusBareStretch>());
                if (failed == null) return new TalusApronResult(true, drafts.ConvertAll((draft) => draft.Part), _bare);
                if (failed.Vertex >= 0) {
                    blocked[failed.Vertex] = true;
                    continue;
                }
                DropRun(plan, runs, failed);
            }
            return new TalusApronResult(true, new List<TalusApronPart>(), _bare);
        }

        private void DropRun(IReadOnlyList<PlanPoint> plan, List<TalusRun>[] runs, TalusDraft failed) {
            int edgeIndex = failed.RunEdge;
            PlanPoint start = plan[edgeIndex], end = plan[(edgeIndex + 1) % plan.Count];
            TalusRun dropped = runs[edgeIndex][failed.RunIndex];
            runs[edgeIndex].RemoveAt(failed.RunIndex);
            _bare.Add(new TalusBareStretch(At(start, end, dropped.From), At(start, end, dropped.To)));
        }

        private static bool IsClear(TalusDraft draft, Func<SlopedPrismPiece, bool> isClear) {
            if (draft.IsOpen) return false;

            foreach (SlopedPrismPiece piece in draft.Part.Pieces) {
                if (!isClear(piece)) return false;
            }
            return true;
        }

        // True when two pieces share an edge but disagree on its height (wide end fans reaching round a short edge into
        // each other's ground cross there): the apron would show a step.
        private static bool HasSeamStep(List<TalusDraft> drafts, double seamStep) {
            var edges = new Dictionary<(long, long, long, long), (double, double)>();
            foreach (TalusDraft draft in drafts) {
                if (HasSeamStep(draft.Part.Pieces, edges, seamStep)) return true;
            }
            return false;
        }

        private static bool HasSeamStep(IReadOnlyList<SlopedPrismPiece> pieces, Dictionary<(long, long, long, long), (double, double)> edges, double seamStep) {
            foreach (SlopedPrismPiece piece in pieces) {
                if (HasSeamStep(piece, edges, seamStep)) return true;
            }
            return false;
        }

        private static bool HasSeamStep(SlopedPrismPiece piece, Dictionary<(long, long, long, long), (double, double)> edges, double seamStep) {
            for (int vertex = 0; vertex < piece.Plan.Count; vertex++) {
                PlanPoint one = piece.Plan[vertex], two = piece.Plan[(vertex + 1) % piece.Plan.Count];
                (long, long) keyOne = (Key(one.X), Key(one.Z)), keyTwo = (Key(two.X), Key(two.Z));
                bool isOrdered = keyOne.CompareTo(keyTwo) <= 0;
                (long, long, long, long) key = isOrdered ? (keyOne.Item1, keyOne.Item2, keyTwo.Item1, keyTwo.Item2) : (keyTwo.Item1, keyTwo.Item2, keyOne.Item1, keyOne.Item2);
                double heightOne = piece.HeightAt(one.X, one.Z), heightTwo = piece.HeightAt(two.X, two.Z);
                (double, double) heights = isOrdered ? (heightOne, heightTwo) : (heightTwo, heightOne);
                if (!edges.TryGetValue(key, out (double First, double Second) seen)) {
                    edges[key] = heights;
                    continue;
                }
                if (Math.Abs(seen.First - heights.Item1) > seamStep || Math.Abs(seen.Second - heights.Item2) > seamStep) return true;
            }
            return false;
        }

        private static long Key(double value) {
            return (long)Math.Round(value * SeamKeyScale);
        }

        // Every chain of the foot: from each run whose start is not joined, then what is left (rings joined all round).
        private List<TalusDraft> Chains(IReadOnlyList<PlanPoint> plan, List<TalusRun>[] runs, bool[] joined) {
            int count = plan.Count;
            var drafts = new List<TalusDraft>();
            var done = new HashSet<(int, int)>();
            for (int edge = 0; edge < count; edge++) AddChainsFrom(plan, runs, joined, edge, done, drafts);
            for (int edge = 0; edge < count; edge++) {
                if (runs[edge].Count > 0 && !done.Contains((edge, 0))) AddChain(plan, runs, joined, edge, 0, done, drafts);
            }
            return drafts;
        }

        // A chain from every run of `edge` not yet laid whose start is not joined to the edge before.
        private void AddChainsFrom(IReadOnlyList<PlanPoint> plan, List<TalusRun>[] runs, bool[] joined, int edge, HashSet<(int, int)> done, List<TalusDraft> drafts) {
            for (int index = 0; index < runs[edge].Count; index++) {
                bool isJoinedStart = index == 0 && runs[edge][0].ReachesStart && joined[edge];
                if (!done.Contains((edge, index)) && !isJoinedStart) AddChain(plan, runs, joined, edge, index, done, drafts);
            }
        }

        // Runs on both sides of a convex vertex reach it.
        private static bool IsJoined(List<TalusRun>[] runs, IReadOnlyList<PlanPoint> plan, int vertex) {
            int count = plan.Count;
            List<TalusRun> before = runs[(vertex + count - 1) % count], after = runs[vertex];
            if (before.Count == 0 || after.Count == 0 || !before[before.Count - 1].ReachesEnd || !after[0].ReachesStart) return false;

            Normal(plan[(vertex + count - 1) % count], plan[vertex], out double fromX, out double fromZ);
            Normal(plan[vertex], plan[(vertex + 1) % count], out double toX, out double toZ);
            return fromX * toZ - fromZ * toX > 1e-9;
        }

        // One chain from run (edge, index): an end fan unless its start is joined, the run, then while its end is joined the
        // corner fan and the next run, else an end fan.
        private void AddChain(IReadOnlyList<PlanPoint> plan, List<TalusRun>[] runs, bool[] joined, int edge, int index, HashSet<(int, int)> done, List<TalusDraft> drafts) {
            int count = plan.Count;
            TalusRun first = runs[edge][index];
            if (!(index == 0 && first.ReachesStart && joined[edge])) drafts.Add(EndFan(plan, edge, first.From, false).For(edge, index));
            int current = edge, slot = index;
            for (int step = 0; step <= count * 4; step++) {
                TalusRun run = runs[current][slot];
                done.Add((current, slot));
                drafts.Add(Run(plan[current], plan[(current + 1) % count], run).For(current, slot));
                int next = (current + 1) % count;
                bool goesOn = slot == runs[current].Count - 1 && run.ReachesEnd && joined[next];
                if (!goesOn) {
                    drafts.Add(EndFan(plan, current, run.To, true).For(current, slot));
                    return;
                }
                drafts.Add(CornerFan(plan[current], plan[next], plan[(next + 1) % count]).AtVertex(next));
                if (done.Contains((next, 0))) return;

                current = next;
                slot = 0;
            }
        }

        // The exposed stretches of one foot edge (outside every other footprint, off the keep-offs) that take an apron; the rest is bare.
        private List<TalusRun> EdgeRuns(IReadOnlyList<PlanPoint> plan, IReadOnlyList<IReadOnlyList<PlanPoint>> others, Func<int, bool> isBareEdge,
                                        IReadOnlyList<IReadOnlyList<PlanPoint>> keepOffs, int edge) {
            PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
            double length = PlanSegments.Length(start, end);
            var runs = new List<TalusRun>();
            if (length < _settings.MinInterval) return runs;

            Normal(start, end, out double normalX, out double normalZ);
            bool isBare = isBareEdge(edge);
            List<double[]> spans = Exposed(others, start, end, normalX, normalZ, length);
            foreach (IReadOnlyList<PlanPoint> keepOff in keepOffs) spans = OffKeepOff(spans, keepOff, start, end, length);
            foreach (double[] span in spans) {
                if (span[1] - span[0] < _settings.MinInterval) continue;
                if (isBare) {
                    _bare.Add(new TalusBareStretch(At(start, end, span[0]), At(start, end, span[1])));
                    continue;
                }
                runs.Add(new TalusRun(span[0], span[1], span[0] <= EndTolerance, span[1] >= length - EndTolerance));
            }
            return runs;
        }

        // The spans less the stretch of the edge within the gap of a keep-off region (its vertices projected on the edge).
        private List<double[]> OffKeepOff(List<double[]> spans, IReadOnlyList<PlanPoint> keepOff, PlanPoint start, PlanPoint end, double length) {
            if (PlanSegments.Distance(keepOff, start, end) >= _settings.KeepOffGap) return spans;

            double directionX = (end.X - start.X) / length, directionZ = (end.Z - start.Z) / length;
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (PlanPoint point in keepOff) {
                double along = (point.X - start.X) * directionX + (point.Z - start.Z) * directionZ;
                low = Math.Min(low, along);
                high = Math.Max(high, along);
            }
            return Remove(spans, low - _settings.KeepOffGap, high + _settings.KeepOffGap);
        }

        // The edge's spans (metres from its start) whose probe line just outside lies outside every other footprint.
        private List<double[]> Exposed(IReadOnlyList<IReadOnlyList<PlanPoint>> others, PlanPoint start, PlanPoint end, double normalX, double normalZ, double length) {
            var spans = new List<double[]> { new[] { 0.0, length } };
            double probe = _settings.ExposureProbe;
            var probeStart = new PlanPoint(start.X + normalX * probe, start.Z + normalZ * probe);
            var probeEnd = new PlanPoint(end.X + normalX * probe, end.Z + normalZ * probe);
            foreach (IReadOnlyList<PlanPoint> other in others) {
                if (other.Count < 3 || !PlanSegments.TryClip(other, probeStart, probeEnd, out double enter, out double leave)) continue;

                spans = Remove(spans, enter * length, leave * length);
            }
            return spans;
        }

        private static List<double[]> Remove(List<double[]> spans, double from, double to) {
            var kept = new List<double[]>(spans.Count + 1);
            foreach (double[] span in spans) {
                if (to <= span[0] || from >= span[1]) {
                    kept.Add(span);
                    continue;
                }
                if (from > span[0]) kept.Add(new[] { span[0], from });
                if (to < span[1]) kept.Add(new[] { to, span[1] });
            }
            return kept;
        }

        // A run as one trapezoid: the wall foot at the rise (reaching the inner depth into the rock), the toe a width out.
        private TalusDraft Run(PlanPoint start, PlanPoint end, TalusRun run) {
            Normal(start, end, out double normalX, out double normalZ);
            PlanPoint from = At(start, end, run.From), to = At(start, end, run.To);
            PlanPoint outFrom = Out(from, normalX, normalZ, _width), outTo = Out(to, normalX, normalZ, _width);
            var plan = new List<PlanPoint> { Out(from, normalX, normalZ, -_settings.InnerDepth), Out(to, normalX, normalZ, -_settings.InnerDepth), outTo, outFrom };
            SlopedPrismPiece piece = SlopedPrismPiece.Through(plan, from, _rise, to, _rise, outFrom, -_settings.ToeSink, false);
            return new TalusDraft(from, to, outFrom, outTo, ConvexPlan.Round(_rise), ConvexPlan.Round(_width), false, new List<SlopedPrismPiece> { piece });
        }

        // The sector fan over the convex corner `at` between the outward normals of its two edges.
        private TalusDraft CornerFan(PlanPoint before, PlanPoint at, PlanPoint after) {
            Normal(before, at, out double fromX, out double fromZ);
            Normal(at, after, out double toX, out double toZ);
            return Fan(at, fromX, fromZ, Out(at, toX, toZ, _width), -_settings.ToeSink, false);
        }

        // The fan closing a run end at `along` on edge `edge`: from the edge's outward normal round to the point of the foot
        // a width from the end, walking on past the end (`isEnd`) or back past the start (its last ray lies in the rock).
        private TalusDraft EndFan(IReadOnlyList<PlanPoint> plan, int edge, double along, bool isEnd) {
            PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
            Normal(start, end, out double normalX, out double normalZ);
            PlanPoint at = At(start, end, along);
            PlanPoint last = FootPointAt(plan, edge, at, isEnd, out double lastHeight);
            // Back past the start the sweep turns clockwise; a foot that never comes back within a width leaves the fan open.
            TalusDraft fan = Fan(at, normalX, normalZ, last, lastHeight, !isEnd);
            fan.IsOpen |= lastHeight > -_settings.ToeSink + OpenTolerance;
            return fan;
        }

        // The foot point a width from `at` (or the farthest one), walking the foot from `at` past the edge's end or start;
        // its height on the cone.
        private PlanPoint FootPointAt(IReadOnlyList<PlanPoint> plan, int edge, PlanPoint at, bool isEnd, out double height) {
            int count = plan.Count;
            PlanPoint previous = at, best = at;
            double bestDistance = 0.0;
            for (int step = 1; step <= count; step++) {
                PlanPoint vertex = isEnd ? plan[(edge + step) % count] : plan[((edge + 1 - step) % count + count) % count];
                double distance = PlanSegments.Length(at, vertex);
                if (distance >= _width) {
                    best = Crossing(at, previous, vertex, _width);
                    bestDistance = _width;
                    break;
                }
                if (distance > bestDistance) {
                    best = vertex;
                    bestDistance = distance;
                }
                previous = vertex;
            }
            height = ConvexPlan.Round(Math.Max(-_settings.ToeSink, _rise - _slope * bestDistance));
            return ConvexPlan.Round(best);
        }

        // The point on segment from–to at `radius` from `at` (from inside the circle, to outside), by fixed bisection steps.
        private static PlanPoint Crossing(PlanPoint at, PlanPoint from, PlanPoint to, double radius) {
            double low = 0.0, high = 1.0;
            for (int step = 0; step < CrossingSteps; step++) {
                double middle = (low + high) * 0.5;
                double pointX = from.X + (to.X - from.X) * middle, pointZ = from.Z + (to.Z - from.Z) * middle;
                if ((pointX - at.X) * (pointX - at.X) + (pointZ - at.Z) * (pointZ - at.Z) < radius * radius) low = middle;
                else high = middle;
            }
            return new PlanPoint(from.X + (to.X - from.X) * low, from.Z + (to.Z - from.Z) * low);
        }

        // Triangles about `at` (at the rise) from the toe point a width out along (fromX, fromZ), sweeping counter-clockwise
        // (`isReversed`: clockwise) to `last` (at `lastHeight`), each turning as far as keeps its facet under the gradient
        // cap; every triangle's top passes through its three corners, so neighbours share their edges' heights.
        private TalusDraft Fan(PlanPoint at, double fromX, double fromZ, PlanPoint last, double lastHeight, bool isReversed) {
            double lastX = last.X - at.X, lastZ = last.Z - at.Z, lastLength = Math.Sqrt(lastX * lastX + lastZ * lastZ);
            double toX = lastX / lastLength, toZ = lastZ / lastLength;
            int steps = _settings.MinFanSteps;
            while (steps < _settings.MaxFanSteps && !IsFineEnough(fromX, fromZ, toX, toZ, steps, isReversed)) steps++;
            // A fan that would need more (it wraps a small foot) cannot keep its facets under the cap: it fails like an open one.
            bool isTooWide = !IsFineEnough(fromX, fromZ, toX, toZ, steps, isReversed);
            List<(PlanPoint Point, double Height)> corners = FanCorners(at, fromX, fromZ, toX, toZ, steps, isReversed, last, lastHeight);
            var pieces = new List<SlopedPrismPiece>(corners.Count - 1);
            for (int step = 0; step < corners.Count - 1; step++) {
                (PlanPoint one, double oneY) = corners[step];
                (PlanPoint two, double twoY) = corners[step + 1];
                // A sliver (its corners nearly in line) has no plane and is left out: its neighbours meet over it.
                if (Math.Abs((one.X - at.X) * (two.Z - at.Z) - (one.Z - at.Z) * (two.X - at.X)) < SliverCross) continue;

                pieces.Add(SlopedPrismPiece.Through(new List<PlanPoint> { at, one, two }, at, _rise, one, oneY, two, twoY, true));
            }
            return new TalusDraft(at, at, corners[0].Point, last, ConvexPlan.Round(_rise), ConvexPlan.Round(_width), true, pieces) { IsOpen = isTooWide };
        }

        // The fan's toe points a width out along each step's ray, then `last`; a ray landing within 0.1 m of it is merged into it.
        private List<(PlanPoint Point, double Height)> FanCorners(PlanPoint at, double fromX, double fromZ, double toX, double toZ, int steps, bool isReversed, PlanPoint last,
                                                                 double lastHeight) {
            var corners = new List<(PlanPoint Point, double Height)>(steps + 1);
            for (int step = 0; step < steps; step++) {
                Direction(fromX, fromZ, toX, toZ, step, steps, isReversed, out double rayX, out double rayZ);
                corners.Add((Out(at, rayX, rayZ, _width), -_settings.ToeSink));
            }
            if (corners.Count > 1 && PlanSegments.Length(corners[corners.Count - 1].Point, last) < MergeReach) corners.RemoveAt(corners.Count - 1);
            corners.Add((last, lastHeight));
            return corners;
        }

        private bool IsFineEnough(double fromX, double fromZ, double toX, double toZ, int steps, bool isReversed) {
            for (int step = 0; step < steps; step++) {
                Direction(fromX, fromZ, toX, toZ, step, steps, isReversed, out double startX, out double startZ);
                Direction(fromX, fromZ, toX, toZ, step + 1, steps, isReversed, out double endX, out double endZ);
                if (startX * endX + startZ * endZ < _fanStepCosine) return false;
            }
            return true;
        }

        // The unit direction `step` of `steps` turning from one direction to the other (counter-clockwise, or clockwise when
        // reversed): the turn is cut at quarter turns and each part is a normalised blend weighted by its chord; no angles.
        private static void Direction(double fromX, double fromZ, double toX, double toZ, int step, int steps, bool isReversed, out double directionX, out double directionZ) {
            var points = new List<(double X, double Z)> { (fromX, fromZ) };
            double currentX = fromX, currentZ = fromZ;
            for (int quarter = 0; quarter < 4 && IsBeyondQuarter(currentX, currentZ, toX, toZ, isReversed); quarter++) {
                (currentX, currentZ) = isReversed ? (currentZ, -currentX) : (-currentZ, currentX);
                points.Add((currentX, currentZ));
            }
            points.Add((toX, toZ));
            double total = 0.0;
            for (int index = 1; index < points.Count; index++) total += Chord(points[index - 1], points[index]);
            double wanted = total * step / steps;
            for (int index = 1; index < points.Count; index++) {
                double chord = Chord(points[index - 1], points[index]);
                if (wanted <= chord || index == points.Count - 1) {
                    Blend(points[index - 1].X, points[index - 1].Z, points[index].X, points[index].Z, chord > 0.0 ? Math.Min(1.0, wanted / chord) : 1.0, out directionX, out directionZ);
                    return;
                }
                wanted -= chord;
            }
            directionX = toX;
            directionZ = toZ;
        }

        // True when turning from (fromX, fromZ) the given way to (toX, toZ) takes more than a quarter turn.
        private static bool IsBeyondQuarter(double fromX, double fromZ, double toX, double toZ, bool isReversed) {
            double cross = fromX * toZ - fromZ * toX, dot = fromX * toX + fromZ * toZ;
            double turning = isReversed ? -cross : cross;
            return dot < 0.0 || turning < 0.0;
        }

        private static double Chord((double X, double Z) one, (double X, double Z) two) {
            double deltaX = two.X - one.X, deltaZ = two.Z - one.Z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static void Blend(double fromX, double fromZ, double toX, double toZ, double share, out double blendX, out double blendZ) {
            blendX = fromX * (1.0 - share) + toX * share;
            blendZ = fromZ * (1.0 - share) + toZ * share;
            double length = Math.Sqrt(blendX * blendX + blendZ * blendZ);
            blendX /= length;
            blendZ /= length;
        }

        private static PlanPoint Out(PlanPoint at, double directionX, double directionZ, double distance) {
            return ConvexPlan.Round(new PlanPoint(at.X + directionX * distance, at.Z + directionZ * distance));
        }

        private static PlanPoint At(PlanPoint start, PlanPoint end, double along) {
            double length = PlanSegments.Length(start, end);
            return ConvexPlan.Round(new PlanPoint(start.X + (end.X - start.X) / length * along, start.Z + (end.Z - start.Z) / length * along));
        }

        // Outward unit normal of a counter-clockwise edge.
        private static void Normal(PlanPoint start, PlanPoint end, out double normalX, out double normalZ) {
            double deltaX = end.X - start.X, deltaZ = end.Z - start.Z;
            double length = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            normalX = deltaZ / length;
            normalZ = -deltaX / length;
        }
    }
}
