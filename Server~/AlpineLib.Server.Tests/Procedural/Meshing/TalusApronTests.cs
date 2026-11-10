using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class TalusApronTests {
        // The values the consuming game passes (its landmark talus).
        public static readonly TalusApronSettings Settings = new TalusApronSettings(0.3, 0.1, 0.70020753820970982, 1.01, 2, 10, 0.5, 1.0, 1.5, 0.02);
        private const double Gentle = 0.57735026918962576, Steep = 0.66188248591897087;
        private static readonly List<IReadOnlyList<PlanPoint>> None = new List<IReadOnlyList<PlanPoint>>();

        [Fact]
        public void RecordedLandmarkFeetLayExactlyAsTheGameDid() {
            Assert.NotEmpty(TalusApronGolden.Cases);
            bool sawUnlaid = false, sawRefused = false;
            foreach (TalusCase talusCase in TalusApronGolden.Cases) {
                TalusApronResult result = Lay(talusCase);
                Assert.Equal(talusCase.ResultHash, GoldenHash.Result(result));
                Assert.Equal(talusCase.PartCount, result.Parts.Count);
                sawUnlaid |= !result.IsLaid;
                sawRefused |= talusCase.Refused.Count > 0;
            }
            // The game never met a seam step on these seeds; SeamStepLaysNothing covers it.
            Assert.False(sawUnlaid);
            Assert.True(sawRefused);
        }

        [Fact]
        public void RecordedApronsAreConvexCounterClockwiseAndSeamless() {
            foreach (TalusCase talusCase in TalusApronGolden.Cases) {
                TalusApronResult result = Lay(talusCase);
                if (!result.IsLaid) continue;

                AssertWellFormed(result, talusCase.Rise, talusCase.Slope);
            }
        }

        [Theory]
        [InlineData(Gentle, 6.0)]
        [InlineData(Steep, 6.0)]
        [InlineData(Gentle, 2.0)]
        public void AnOpenFootIsRingedBySeamlessPieces(double slope, double rise) {
            List<PlanPoint> foot = Hexagon(30.0);

            TalusApronResult result = TalusApron.Lay(foot, None, rise, slope, (edge) => false, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            Assert.Empty(result.Bare);
            // A closed ring: one strip and one corner fan per edge.
            Assert.Equal(6, result.Parts.Count((part) => !part.IsFan));
            Assert.Equal(6, result.Parts.Count((part) => part.IsFan));
            AssertWellFormed(result, rise, slope);
        }

        [Fact]
        public void ASlopeAtTheGradientCapCannotFanSoTheFootStaysBare() {
            TalusApronResult result = TalusApron.Lay(Hexagon(30.0), None, 4.0, Settings.GradientMax, (edge) => false, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            Assert.Empty(result.Parts);
            Assert.Equal(6, result.Bare.Count);
        }

        [Fact]
        public void SeamStepLaysNothing() {
            // End fans either side of two bare short edges reach round them into each other's ground.
            var foot = new List<PlanPoint> {
                new PlanPoint(1.1, 2.6), new PlanPoint(22.3, 2.7), new PlanPoint(25.6, 4.0), new PlanPoint(23.8, 9.0), new PlanPoint(14.9, 11.4), new PlanPoint(8.6, 8.2),
            };

            TalusApronResult result = TalusApron.Lay(foot, None, 5.5, 0.6075, (edge) => edge == 3 || edge == 5, None, (piece) => true, Settings);

            Assert.False(result.IsLaid);
            Assert.Empty(result.Parts);
            Assert.Empty(result.Bare);
            Assert.True(TalusApron.Lay(foot, None, 4.4, 0.6075, (edge) => edge == 3 || edge == 5, None, (piece) => true, Settings).IsLaid);
        }

        [Fact]
        public void ZeroClearLeavesTheWholeFootBare() {
            List<PlanPoint> foot = Hexagon(30.0);

            TalusApronResult result = TalusApron.Lay(foot, None, 6.0, Gentle, (edge) => false, None, (piece) => false, Settings);

            Assert.True(result.IsLaid);
            Assert.Empty(result.Parts);
            Assert.Equal(6, result.Bare.Count);
        }

        [Fact]
        public void BareEdgesTakeNoApronAndCloseTheirNeighboursWithEndFans() {
            List<PlanPoint> foot = Hexagon(30.0);

            TalusApronResult result = TalusApron.Lay(foot, None, 6.0, Gentle, (edge) => edge == 2, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            Assert.Single(result.Bare);
            Assert.Equal(5, result.Parts.Count((part) => !part.IsFan));
            Assert.Equal(4 + 2, result.Parts.Count((part) => part.IsFan));
            AssertWellFormed(result, 6.0, Gentle);
        }

        [Fact]
        public void AllBareLaysNothing() {
            TalusApronResult result = TalusApron.Lay(Hexagon(30.0), None, 6.0, Gentle, (edge) => true, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            Assert.Empty(result.Parts);
            Assert.Equal(6, result.Bare.Count);
        }

        [Fact]
        public void ShortEdgesAndCoveredStretchesTakeNoRun() {
            var foot = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(40, 0), new PlanPoint(40.5, 0.5), new PlanPoint(40.5, 40), new PlanPoint(0, 40) };
            // Another footprint just west of the west edge's middle: the probe line half a metre out runs inside it there.
            var other = new List<PlanPoint> { new PlanPoint(-10, 15), new PlanPoint(-0.2, 15), new PlanPoint(-0.2, 25), new PlanPoint(-10, 25) };

            TalusApronResult result = TalusApron.Lay(foot, new List<IReadOnlyList<PlanPoint>> { other }, 5.0, Gentle, (edge) => false, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            // Four long edges (the 0.7 m one takes none), the west one split in two by the other footprint.
            List<TalusApronPart> strips = result.Parts.Where((part) => !part.IsFan).ToList();
            Assert.Equal(5, strips.Count);
            List<TalusApronPart> west = strips.Where((part) => part.InnerStart.X == 0.0 && part.InnerEnd.X == 0.0).OrderBy((part) => part.InnerEnd.Z).ToList();
            Assert.Equal(2, west.Count);
            Assert.Equal((15.0, 0.0), (west[0].InnerStart.Z, west[0].InnerEnd.Z));
            Assert.Equal((40.0, 25.0), (west[1].InnerStart.Z, west[1].InnerEnd.Z));
            AssertWellFormed(result, 5.0, Gentle);
        }

        [Fact]
        public void KeepOffRegionsLeaveTheirStretchBare() {
            List<PlanPoint> foot = Square(40.0);
            var keepOff = new List<PlanPoint> { new PlanPoint(18, -6), new PlanPoint(22, -6), new PlanPoint(22, -1), new PlanPoint(18, -1) };

            TalusApronResult result = TalusApron.Lay(foot, None, 5.0, Gentle, (edge) => false, new List<IReadOnlyList<PlanPoint>> { keepOff }, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            List<TalusApronPart> southStrips = result.Parts.Where((part) => !part.IsFan && part.InnerStart.Z == 0.0 && part.InnerEnd.Z == 0.0).OrderBy((part) => part.InnerStart.X).ToList();
            Assert.Equal(2, southStrips.Count);
            Assert.Equal(16.5, southStrips[0].InnerEnd.X, 6);
            Assert.Equal(23.5, southStrips[1].InnerStart.X, 6);
        }

        [Fact]
        public void AnEndFanThatCannotReachBackToTheFootLeavesItsRunBare() {
            // A 10 m triangle with its base bare: at rise 6 the apron is 10.6 m wide and the end fans never find the foot
            // again a width away; at rise 4 (7.1 m) they do.
            var triangle = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(10, 0), new PlanPoint(5, 8.6603) };

            TalusApronResult wide = TalusApron.Lay(triangle, None, 6.0, Gentle, (edge) => edge == 0, None, (piece) => true, Settings);
            TalusApronResult narrow = TalusApron.Lay(triangle, None, 4.0, Gentle, (edge) => edge == 0, None, (piece) => true, Settings);

            Assert.True(wide.IsLaid);
            Assert.Empty(wide.Parts);
            Assert.Equal(3, wide.Bare.Count);
            Assert.Equal(2, narrow.Parts.Count((part) => !part.IsFan));
            Assert.Single(narrow.Bare);
            AssertWellFormed(narrow, 4.0, Gentle);
        }

        [Theory]
        [InlineData(0.006, 1)]
        [InlineData(0.017, 2)]
        public void AFanRayWithin10CentimetresOfTheLastPointMergesIntoIt(double rise, int piecesPerCornerFan) {
            // Hexagon corners turn 60°, cut in two 30° steps: the last ray lands 0.518 × width from the last point,
            // 0.095 m at rise 0.006 (merged) and 0.105 m at rise 0.017 (kept).
            TalusApronResult result = TalusApron.Lay(Hexagon(30.0), None, rise, Gentle, (edge) => false, None, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            List<TalusApronPart> fans = result.Parts.Where((part) => part.IsFan).ToList();
            Assert.Equal(6, fans.Count);
            foreach (TalusApronPart fan in fans) Assert.Equal(piecesPerCornerFan, fan.Pieces.Count);
        }

        [Theory]
        [InlineData(1.4, 2)]
        [InlineData(1.6, 1)]
        [InlineData(3.0, 1)]
        [InlineData(4.4, 1)]
        public void OnlyKeepOffsWithinTheGapCutTheEdge(double distance, int southStrips) {
            var keepOff = new List<PlanPoint> { new PlanPoint(18, -distance - 5), new PlanPoint(22, -distance - 5), new PlanPoint(22, -distance), new PlanPoint(18, -distance) };

            TalusApronResult result = TalusApron.Lay(Square(40.0), None, 5.0, Gentle, (edge) => false, new List<IReadOnlyList<PlanPoint>> { keepOff }, (piece) => true, Settings);

            Assert.True(result.IsLaid);
            Assert.Equal(southStrips, result.Parts.Count((part) => !part.IsFan && part.InnerStart.Z == 0.0 && part.InnerEnd.Z == 0.0));
        }

        [Fact]
        public void UnbuiltSettingsAndBadRisesThrow() {
            Assert.Throws<ArgumentException>(() => TalusApron.Lay(Square(10), None, 5.0, Gentle, (edge) => false, None, (piece) => true, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => TalusApron.Lay(Square(10), None, -1.0, Gentle, (edge) => false, None, (piece) => true, Settings));
            Assert.Throws<ArgumentOutOfRangeException>(() => TalusApron.Lay(Square(10), None, double.NaN, Gentle, (edge) => false, None, (piece) => true, Settings));
            Assert.True(TalusApron.Lay(Square(10), None, 0.0, Gentle, (edge) => false, None, (piece) => true, Settings).IsLaid);
        }

        [Fact]
        public void TinyFeetAndBadArgumentsAreHandled() {
            var segment = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(1, 0) };
            TalusApronResult result = TalusApron.Lay(segment, None, 5.0, Gentle, (edge) => false, None, (piece) => true, Settings);
            Assert.True(result.IsLaid);
            Assert.Empty(result.Parts);

            Assert.Throws<ArgumentOutOfRangeException>(() => TalusApron.Lay(Square(10), None, 5.0, 0.0, (edge) => false, None, (piece) => true, Settings));
            Assert.Throws<ArgumentNullException>(() => TalusApron.Lay(Square(10), None, 5.0, Gentle, null, None, (piece) => true, Settings));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TalusApronSettings(0.3, 0.1, 0.7, 1.01, 3, 2, 0.5, 1.0, 1.5, 0.02));
        }

        // Pieces convex, counter-clockwise and within the collision library's 16-vertex cap; part surfaces the
        // counter-clockwise hull of their pieces; strip walls at the rise and toes under the ground; shared edges agree.
        private static void AssertWellFormed(TalusApronResult result, double rise, double slope) {
            var heights = new Dictionary<(long, long), double>();
            foreach (TalusApronPart part in result.Parts) {
                Assert.True(ConvexPlan.IsStrictlyConvexCcw(part.Surface, 0.0));
                foreach (SlopedPrismPiece piece in part.Pieces) {
                    AssertPiece(piece, slope);
                    foreach (PlanPoint corner in piece.Plan) Assert.True(ConvexPlan.Distance(part.Surface, corner.X, corner.Z) <= 1e-9);
                    AssertSeams(piece, heights);
                }
                if (!part.IsFan) AssertStrip(part, rise);
            }
        }

        private static void AssertPiece(SlopedPrismPiece piece, double slope) {
            Assert.InRange(piece.Plan.Count, 3, 16);
            Assert.True(ConvexPlan.IsStrictlyConvexCcw(piece.Plan, 0.0));
            Assert.True(piece.Gradient <= Math.Max(slope, Settings.GradientMax) + 1e-3, $"gradient {piece.Gradient}");
        }

        private static void AssertStrip(TalusApronPart part, double rise) {
            SlopedPrismPiece strip = part.Pieces[0];
            Assert.InRange(strip.HeightAt(part.InnerStart.X, part.InnerStart.Z) - rise, -1e-3, 1e-3);
            Assert.InRange(strip.HeightAt(part.InnerEnd.X, part.InnerEnd.Z) - rise, -1e-3, 1e-3);
            Assert.InRange(strip.HeightAt(part.OuterStart.X, part.OuterStart.Z) + Settings.ToeSink, -1e-3, 1e-3);
        }

        // Every vertex two pieces share stands at the same height on both, within the seam step.
        private static void AssertSeams(SlopedPrismPiece piece, Dictionary<(long, long), double> heights) {
            foreach (PlanPoint corner in piece.Plan) {
                (long, long) key = ((long)Math.Round(corner.X * 1000.0), (long)Math.Round(corner.Z * 1000.0));
                double height = piece.HeightAt(corner.X, corner.Z);
                if (heights.TryGetValue(key, out double seen)) Assert.True(Math.Abs(seen - height) <= Settings.SeamStep, $"seam step {Math.Abs(seen - height)} at {corner}");
                else heights[key] = height;
            }
        }

        private static TalusApronResult Lay(TalusCase talusCase) {
            return TalusApron.Lay(talusCase.Foot, talusCase.Others, talusCase.Rise, talusCase.Slope, (edge) => talusCase.BareEdges[edge] == '1', talusCase.KeepOffs,
                                  (piece) => !talusCase.Refused.Contains(GoldenHash.PieceKey(piece)), Settings);
        }

        private static List<PlanPoint> Hexagon(double radius) {
            var points = new List<PlanPoint>(6);
            double half = radius * 0.5, across = ConvexPlan.Round(radius * 0.8660254037844386);
            points.Add(new PlanPoint(radius, 0));
            points.Add(new PlanPoint(half, across));
            points.Add(new PlanPoint(-half, across));
            points.Add(new PlanPoint(-radius, 0));
            points.Add(new PlanPoint(-half, -across));
            points.Add(new PlanPoint(half, -across));
            return points;
        }

        private static List<PlanPoint> Square(double size) {
            return new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(size, 0), new PlanPoint(size, size), new PlanPoint(0, size) };
        }
    }
}
