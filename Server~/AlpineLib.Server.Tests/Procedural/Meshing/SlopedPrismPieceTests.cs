using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class SlopedPrismPieceTests {
        [Fact]
        public void ThroughPassesItsPlaneThroughAllThreeCorners() {
            var first = new PlanPoint(1.0, 2.0);
            var second = new PlanPoint(5.0, 2.5);
            var third = new PlanPoint(2.0, 7.0);
            var plan = new List<PlanPoint> { third, first, second };

            SlopedPrismPiece piece = SlopedPrismPiece.Through(plan, first, 3.0, second, 2.0, third, -0.1, true);

            Assert.Equal(3.0, piece.HeightAt(first.X, first.Z), 9);
            Assert.Equal(2.0, piece.HeightAt(second.X, second.Z), 5);
            Assert.Equal(-0.1, piece.HeightAt(third.X, third.Z), 5);
            Assert.True(ConvexPlan.Area(piece.Plan) > 0.0);
            Assert.Equal((1.0, 2.0), (piece.CentreX, piece.CentreZ));
            Assert.True(piece.IsFan);
        }

        [Fact]
        public void ThroughRoundsTheTopToTheGridAndSlopesTo1e7() {
            var first = new PlanPoint(0.0, 0.0);
            SlopedPrismPiece piece = SlopedPrismPiece.Through(new List<PlanPoint> { first, new PlanPoint(3.0, 0.0), new PlanPoint(0.0, 3.0) }, first, 1.234567,
                                                              new PlanPoint(3.0, 0.0), 1.234567 + 1.0, new PlanPoint(0.0, 3.0), 1.234567, false);

            Assert.Equal(1.2346, piece.TopY0, 12);
            Assert.Equal(0.3333333, piece.SlopeX, 12);
            Assert.Equal(0.0, piece.SlopeZ);
            Assert.Equal(0.3333333, piece.Gradient, 12);
        }

        [Fact]
        public void TheConstructorCopiesAndChecksItsPlan() {
            var plan = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(2, 0), new PlanPoint(0, 2) };
            var piece = new SlopedPrismPiece(plan, 0, 0, 1, 0.5, 0, false);

            plan[0] = new PlanPoint(-5, -5);

            Assert.Equal(new PlanPoint(0, 0), piece.Plan[0]);
            Assert.False(piece.Plan is PlanPoint[]);
            var clockwise = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(0, 2), new PlanPoint(2, 0) };
            Assert.Throws<ArgumentException>(() => new SlopedPrismPiece(clockwise, 0, 0, 1, 0, 0, false));
            Assert.Throws<ArgumentException>(() => new SlopedPrismPiece(new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(1, 0) }, 0, 0, 1, 0, 0, false));
            Assert.Throws<ArgumentNullException>(() => new SlopedPrismPiece(null, 0, 0, 1, 0, 0, false));
            Assert.Throws<ArgumentException>(() => new SlopedPrismPiece(new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(2, 0), new PlanPoint(0, 2) }, 0, 0, double.NaN, 0, 0, false));
        }
    }
}
