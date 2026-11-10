using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class PolygonFilletTests {
        private static readonly PolygonFilletSettings Settings = new PolygonFilletSettings(0.8660254037844386, 0.45);

        [Fact]
        public void RecordedBandPlansRoundExactlyAsTheGameDid() {
            Assert.NotEmpty(PolygonFilletGolden.Cases);
            foreach (FilletCase filletCase in PolygonFilletGolden.Cases) {
                List<PlanPoint> rounded = PolygonFillet.Round(filletCase.Polygon, filletCase.Corners, Settings);
                Assert.Equal(filletCase.RoundedHash, GoldenHash.Add(GoldenHash.Start, rounded));
                Assert.Equal(filletCase.MaxRadiusHash, MaxRadiusHash(filletCase.Polygon));
            }
        }

        [Fact]
        public void RoundedPlansStayConvexAndInsideTheirInput() {
            foreach (FilletCase filletCase in PolygonFilletGolden.Cases) {
                List<PlanPoint> rounded = PolygonFillet.Round(filletCase.Polygon, filletCase.Corners, Settings);
                Assert.True(ConvexPlan.IsStrictlyConvexCcw(rounded, 0.0));
                foreach (PlanPoint point in rounded) Assert.True(ConvexPlan.Distance(filletCase.Polygon, point.X, point.Z) <= 2e-4);
                Assert.True(ConvexPlan.Area(rounded) <= ConvexPlan.Area(filletCase.Polygon) + 1e-3);
            }
        }

        [Fact]
        public void ASquareCornerBecomesItsTangentPointsAndArcMidpoint() {
            List<PlanPoint> square = Square(10.0);
            var corners = Corners(4, 2.0, true);

            List<PlanPoint> rounded = PolygonFillet.Round(square, corners, Settings);

            Assert.Equal(12, rounded.Count);
            Assert.Contains(new PlanPoint(2.0, 0.0), rounded);
            Assert.Contains(new PlanPoint(0.0, 2.0), rounded);
            // The midpoint lies on the arc of radius 2 about (2, 2).
            double reach = 2.0 - 2.0 / Math.Sqrt(2.0);
            Assert.Contains(ConvexPlan.Round(new PlanPoint(reach, reach)), rounded);
        }

        [Fact]
        public void WithoutAMidpointACornerIsChamfered() {
            List<PlanPoint> rounded = PolygonFillet.Round(Square(10.0), Corners(4, 2.0, false), Settings);

            Assert.Equal(8, rounded.Count);
            Assert.DoesNotContain(new PlanPoint(0.0, 0.0), rounded);
        }

        [Fact]
        public void BluntCornersStaySharp() {
            // A regular octagon turns 45° at each corner (cosine 0.71), blunter than a 60° cut-off: every corner is kept.
            var octagon = new List<PlanPoint>();
            for (int index = 0; index < 8; index++) {
                double angle = index * Math.PI / 4.0;
                octagon.Add(ConvexPlan.Round(new PlanPoint(10.0 * Math.Cos(angle), 10.0 * Math.Sin(angle))));
            }
            var blunt = new PolygonFilletSettings(0.5, 0.45);

            Assert.Equal(ConvexPlan.Hull(octagon), PolygonFillet.Round(octagon, Corners(8, 3.0, true), blunt));
        }

        [Fact]
        public void TheTangentLengthIsCappedByTheEdgeShare() {
            // A 1 m square: a 5 m radius would pass the edge middles; the tangent stops at 0.45 m.
            List<PlanPoint> rounded = PolygonFillet.Round(Square(1.0), Corners(4, 5.0, false), Settings);

            Assert.Contains(new PlanPoint(0.45, 0.0), rounded);
            Assert.Contains(new PlanPoint(0.55, 0.0), rounded);
        }

        [Fact]
        public void ReflexAndRepeatedVerticesAreLeftToTheHull() {
            var concave = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(10, 0), new PlanPoint(5, 2), new PlanPoint(10, 10), new PlanPoint(0, 10), new PlanPoint(0, 10) };

            List<PlanPoint> rounded = PolygonFillet.Round(concave, Corners(6, 0.0, false), Settings);

            Assert.True(ConvexPlan.IsStrictlyConvexCcw(rounded, 0.0));
            Assert.DoesNotContain(new PlanPoint(5, 2), rounded);
        }

        [Fact]
        public void ShortPolygonsComeBackAsCopiesAndBadInputThrows() {
            var segment = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(1, 0) };
            Assert.Equal(segment, PolygonFillet.Round(segment, new List<FilletCorner>(), Settings));
            Assert.Throws<ArgumentException>(() => PolygonFillet.Round(Square(1.0), Corners(3, 1.0, true), Settings));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolygonFilletSettings(0.9, 0.6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolygonFilletSettings(1.5, 0.4));
        }

        [Fact]
        public void MaxRadiusKeepsTheTangentWithinTheShare() {
            // A right angle has tan(45°) = 1: the radius equals the tangent length, half the shorter edge.
            Assert.Equal(2.5, PolygonFillet.MaxRadius(Rectangle(5.0, 8.0), 0, 0.5), 9);
            var line = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(1, 0), new PlanPoint(2, 0) };
            Assert.Equal(0.0, PolygonFillet.MaxRadius(line, 1, 0.5));
        }

        private static ulong MaxRadiusHash(List<PlanPoint> polygon) {
            ulong hash = GoldenHash.Start;
            for (int index = 0; index < polygon.Count; index++) hash = GoldenHash.Add(hash, PolygonFillet.MaxRadius(polygon, index, 0.5));
            return hash;
        }

        private static List<PlanPoint> Square(double size) {
            return Rectangle(size, size);
        }

        private static List<PlanPoint> Rectangle(double width, double depth) {
            return new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(width, 0), new PlanPoint(width, depth), new PlanPoint(0, depth) };
        }

        private static List<FilletCorner> Corners(int count, double radius, bool hasMidpoint) {
            var corners = new List<FilletCorner>(count);
            for (int index = 0; index < count; index++) corners.Add(new FilletCorner(radius, hasMidpoint));
            return corners;
        }
    }
}
