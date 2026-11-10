using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A vertical prism over a convex counter-clockwise plan under a planar sloped top,
    /// y = TopY0 + SlopeX·(x − CentreX) + SlopeZ·(z − CentreZ); its bottom is the consumer's to choose.
    /// </summary>
    public sealed class SlopedPrismPiece {
        /// <summary>Builds a piece.</summary>
        public SlopedPrismPiece(IReadOnlyList<PlanPoint> plan, double centreX, double centreZ, double topY0, double slopeX, double slopeZ, bool isFan) {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            CentreX = centreX;
            CentreZ = centreZ;
            TopY0 = topY0;
            SlopeX = slopeX;
            SlopeZ = slopeZ;
            IsFan = isFan;
        }

        /// <summary>Convex plan, counter-clockwise.</summary>
        public IReadOnlyList<PlanPoint> Plan { get; }

        /// <summary>Plane reference point X.</summary>
        public double CentreX { get; }

        /// <summary>Plane reference point Z.</summary>
        public double CentreZ { get; }

        /// <summary>Top height at the reference point.</summary>
        public double TopY0 { get; }

        /// <summary>Top rise per metre of X.</summary>
        public double SlopeX { get; }

        /// <summary>Top rise per metre of Z.</summary>
        public double SlopeZ { get; }

        /// <summary>True for a fan triangle, false for a strip trapezoid.</summary>
        public bool IsFan { get; }

        /// <summary>Steepest rise per metre of the top.</summary>
        public double Gradient => Math.Sqrt(SlopeX * SlopeX + SlopeZ * SlopeZ);

        /// <summary>Top height over (x, z).</summary>
        public double HeightAt(double x, double z) {
            return TopY0 + SlopeX * (x - CentreX) + SlopeZ * (z - CentreZ);
        }

        /// <summary>
        /// The piece whose plane passes through three corners (x, height, z) and whose plan is the hull of
        /// <paramref name="plan"/>: the top height rounded to <see cref="ConvexPlan.Grid"/>, slopes to 1e-7, the reference
        /// point at the first corner.
        /// </summary>
        public static SlopedPrismPiece Through(IReadOnlyList<PlanPoint> plan, PlanPoint first, double firstY, PlanPoint second, double secondY, PlanPoint third, double thirdY,
                                               bool isFan) {
            double secondX = second.X - first.X, secondZ = second.Z - first.Z, secondRise = secondY - firstY;
            double thirdX = third.X - first.X, thirdZ = third.Z - first.Z, thirdRise = thirdY - firstY;
            double determinant = secondX * thirdZ - secondZ * thirdX;
            double slopeX = Fine((secondRise * thirdZ - secondZ * thirdRise) / determinant), slopeZ = Fine((secondX * thirdRise - secondRise * thirdX) / determinant);
            return new SlopedPrismPiece(ConvexPlan.Hull(plan), first.X, first.Z, ConvexPlan.Round(firstY), slopeX, slopeZ, isFan);
        }

        // Slopes rounded to 1e-7 so every runtime stores the same plane.
        private static double Fine(double value) {
            return Math.Round(value * 1e7) / 1e7;
        }
    }
}
