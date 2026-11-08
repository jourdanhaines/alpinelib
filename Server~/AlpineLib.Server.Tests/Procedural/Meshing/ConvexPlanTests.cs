using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class ConvexPlanTests {
        private const double Tolerance = 2e-4;

        [Fact]
        public void AHullIsConvexCounterClockwiseAndHoldsEveryPoint() {
            for (int seed = 0; seed < 50; seed++) {
                List<PlanPoint> points = Cloud(seed, 40);

                List<PlanPoint> hull = ConvexPlan.Hull(points);

                Assert.True(ConvexPlan.IsStrictlyConvexCcw(hull, 0.0));
                Assert.All(points, (point) => Assert.True(ConvexPlan.Contains(hull, point.X, point.Z, Tolerance)));
            }
        }

        [Fact]
        public void EnclosingNeverShrinksAndKeepsTheBudget() {
            for (int seed = 0; seed < 50; seed++) {
                List<PlanPoint> points = Cloud(seed, 60);
                List<PlanPoint> hull = ConvexPlan.Hull(points);

                List<PlanPoint> enclosed = ConvexPlan.EncloseMaxVertices(hull, 8);

                Assert.InRange(enclosed.Count, 3, 8);
                Assert.True(ConvexPlan.IsStrictlyConvexCcw(enclosed, 0.0));
                Assert.True(ConvexPlan.Area(enclosed) >= ConvexPlan.Area(hull) - 1e-6);
                Assert.All(points, (point) => Assert.True(ConvexPlan.Contains(enclosed, point.X, point.Z, Tolerance)));
            }
        }

        [Fact]
        public void CleaningRemovesNearStraightVerticesAndShortEdgesOutward() {
            var polygon = new List<PlanPoint> {
                new PlanPoint(0.0, 0.0), new PlanPoint(1.0, -0.001), new PlanPoint(2.0, 0.0), new PlanPoint(2.0, 1.0),
                new PlanPoint(1.02, 1.6), new PlanPoint(1.0, 1.61), new PlanPoint(0.0, 1.0),
            };

            List<PlanPoint> cleaned = ConvexPlan.Clean(polygon, 0.01, 0.05, 0.1);

            Assert.True(ConvexPlan.IsStrictlyConvexCcw(cleaned, 0.01));
            Assert.Equal(5, cleaned.Count);
            Assert.All(polygon, (point) => Assert.True(ConvexPlan.Contains(cleaned, point.X, point.Z, Tolerance)));
            for (int index = 0; index < cleaned.Count; index++) {
                PlanPoint start = cleaned[index], end = cleaned[(index + 1) % cleaned.Count];
                Assert.True(Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z)) >= 0.05);
            }
        }

        [Fact]
        public void ClippingAnOctagonToASquareSplitsBackUnderTheBudget() {
            List<PlanPoint> octagon = RegularOctagon(1.0);

            List<PlanPoint> clipped = ConvexPlan.ClipRect(octagon, -0.85, 0.85, -0.85, 0.85);
            List<List<PlanPoint>> pieces = ConvexPlan.SplitToMaxVertices(clipped, 5);

            Assert.True(clipped.Count > 8);
            Assert.True(pieces.Count >= 2);
            Assert.All(pieces, (piece) => Assert.InRange(piece.Count, 3, 5));
            Assert.All(clipped, (point) => Assert.True(ConvexPlan.Contains(octagon, point.X, point.Z, 1e-9) && Math.Abs(point.X) <= 0.85 + 1e-9 && Math.Abs(point.Z) <= 0.85 + 1e-9));
            for (int sample = 0; sample < 400; sample++) {
                double x = -1.0 + (sample % 20) * 0.1 + 0.05, z = -1.0 + (sample / 20) * 0.1 + 0.05;
                bool inClip = ConvexPlan.Contains(clipped, x, z, 1e-9);
                bool inPiece = pieces.Exists((piece) => ConvexPlan.Contains(piece, x, z, 1e-9));
                Assert.Equal(inClip, inPiece);
            }
        }

        [Fact]
        public void HalfPlaneClipsKeepTheLowSide() {
            List<PlanPoint> octagon = RegularOctagon(1.0);
            double half = Math.Sqrt(0.5);

            List<PlanPoint> clipped = ConvexPlan.ClipHalfPlane(octagon, half, half, 0.2);

            Assert.All(clipped, (point) => Assert.True(half * point.X + half * point.Z <= 0.2 + 1e-9));
            Assert.Empty(ConvexPlan.ClipHalfPlane(octagon, 1.0, 0.0, -2.0));
            Assert.Equal(octagon.Count, ConvexPlan.ClipHalfPlane(octagon, 1.0, 0.0, 2.0).Count);
        }

        [Fact]
        public void DistanceIsZeroInsideAndExactOutside() {
            var square = new List<PlanPoint> { new PlanPoint(0.0, 0.0), new PlanPoint(1.0, 0.0), new PlanPoint(1.0, 1.0), new PlanPoint(0.0, 1.0) };

            Assert.Equal(0.0, ConvexPlan.Distance(square, 0.5, 0.5));
            Assert.Equal(0.0, ConvexPlan.Distance(square, 1.0, 0.5));
            Assert.Equal(0.5, ConvexPlan.Distance(square, 1.5, 0.5), 12);
            Assert.Equal(Math.Sqrt(2.0), ConvexPlan.Distance(square, 2.0, 2.0), 12);
            Assert.False(ConvexPlan.Contains(square, 1.1, 0.5, 0.05));
            Assert.True(ConvexPlan.Contains(square, 1.04, 0.5, 0.05));
        }

        [Fact]
        public void ReRunsAreBitIdentical() {
            for (int seed = 0; seed < 10; seed++) {
                List<PlanPoint> first = Pipeline(Cloud(seed, 50));
                List<PlanPoint> second = Pipeline(Cloud(seed, 50));

                Assert.Equal(first, second);
            }
        }

        private static List<PlanPoint> Pipeline(List<PlanPoint> points) {
            List<PlanPoint> enclosed = ConvexPlan.EncloseMaxVertices(ConvexPlan.Hull(points), 8);
            return ConvexPlan.Clean(ConvexPlan.ClipRect(enclosed, -1.0, 1.5, -2.0, 0.7), 0.01, 0.05, 0.1);
        }

        private static List<PlanPoint> Cloud(int seed, int count) {
            var random = new Random(seed);
            var points = new List<PlanPoint>(count);
            for (int index = 0; index < count; index++) points.Add(new PlanPoint(random.NextDouble() * 4.0 - 2.0, (random.NextDouble() * 2.0 - 1.0) * (0.3 + seed % 3)));
            return points;
        }

        private static List<PlanPoint> RegularOctagon(double radius) {
            var octagon = new List<PlanPoint>(8);
            for (int index = 0; index < 8; index++) {
                double angle = index * Math.PI / 4.0;
                octagon.Add(new PlanPoint(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
            return octagon;
        }
    }
}
