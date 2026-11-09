using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A vertical convex prism around part of a look: a counter-clockwise <see cref="ConvexPlan"/> polygon in the
    /// mesh's space extruded from <see cref="Bottom"/> to <see cref="Top"/>, or up to a sloped plane (<see cref="Sloped"/>).
    /// One piece of a <see cref="PrismEnvelopeFitter"/> result.
    /// </summary>
    public sealed class EnvelopePrism : IEquatable<EnvelopePrism> {
        /// <summary>Builds a prism; the plan is copied.</summary>
        public EnvelopePrism(double bottom, double top, IReadOnlyList<PlanPoint> plan) : this(bottom, top, plan, top, 0.0, 0.0) { }

        private EnvelopePrism(double bottom, double top, IReadOnlyList<PlanPoint> plan, double topY0, double slopeX, double slopeZ) {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!(top > bottom)) throw new ArgumentException("Top must be above bottom.", nameof(top));

            Bottom = bottom;
            Top = top;
            TopY0 = topY0;
            SlopeX = slopeX;
            SlopeZ = slopeZ;
            Plan = new List<PlanPoint>(plan).AsReadOnly();
            _minX = _minZ = double.PositiveInfinity;
            _maxX = _maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in Plan) {
                _minX = Math.Min(_minX, point.X);
                _maxX = Math.Max(_maxX, point.X);
                _minZ = Math.Min(_minZ, point.Z);
                _maxZ = Math.Max(_maxZ, point.Z);
            }
        }

        private readonly double _minX, _maxX, _minZ, _maxZ;

        /// <summary>
        /// A prism from <paramref name="bottom"/> up to the plane <c>y = topY0 + slopeX·x + slopeZ·z</c> (mesh space);
        /// <see cref="Top"/> is its highest vertex. The plane must clear the bottom at every vertex.
        /// </summary>
        public static EnvelopePrism Sloped(double bottom, IReadOnlyList<PlanPoint> plan, double topY0, double slopeX, double slopeZ) {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            double highest = double.NegativeInfinity, lowest = double.PositiveInfinity;
            foreach (PlanPoint point in plan) {
                double top = topY0 + slopeX * point.X + slopeZ * point.Z;
                highest = Math.Max(highest, top);
                lowest = Math.Min(lowest, top);
            }
            if (!(lowest > bottom)) throw new ArgumentException("A sloped top must clear the bottom at every vertex.", nameof(topY0));

            return new EnvelopePrism(bottom, highest, plan, topY0, slopeX, slopeZ);
        }

        /// <summary>True when the top is a sloped plane rather than flat at <see cref="Top"/>.</summary>
        public bool IsSloped => SlopeX != 0.0 || SlopeZ != 0.0;

        /// <summary>The top plane's height at the mesh-space origin; <see cref="Top"/> for a flat prism.</summary>
        public double TopY0 { get; }

        /// <summary>The top's rise per unit of x; 0 for a flat prism.</summary>
        public double SlopeX { get; }

        /// <summary>The top's rise per unit of z; 0 for a flat prism.</summary>
        public double SlopeZ { get; }

        /// <summary>The top's height over (<paramref name="x"/>, <paramref name="z"/>); exactly <see cref="Top"/> for a flat prism.</summary>
        public double TopAt(double x, double z) {
            return IsSloped ? TopY0 + SlopeX * x + SlopeZ * z : Top;
        }

        /// <summary>True when the point lies within <paramref name="tolerance"/> of the prism; a bounding-box test first.</summary>
        public bool Holds(double x, double y, double z, double tolerance) {
            if (x < _minX - tolerance || x > _maxX + tolerance || z < _minZ - tolerance || z > _maxZ + tolerance) return false;
            if (y < Bottom - tolerance || y > Top + tolerance) return false;

            return DistanceOutside(x, y, z) <= tolerance;
        }

        /// <summary>True when the plan point lies within <paramref name="tolerance"/> of the plan; a bounding-box test first.</summary>
        public bool PlanHolds(double x, double z, double tolerance) {
            if (x < _minX - tolerance || x > _maxX + tolerance || z < _minZ - tolerance || z > _maxZ + tolerance) return false;

            return ConvexPlan.Distance(Plan, x, z) <= tolerance;
        }

        /// <summary>Lowest height.</summary>
        public double Bottom { get; }

        /// <summary>Highest height: the cap.</summary>
        public double Top { get; }

        /// <summary>Plan polygon, counter-clockwise, 3–8 vertices for a fit.</summary>
        public IReadOnlyList<PlanPoint> Plan { get; }

        /// <summary>Plan area times mean height (the top at the plan's centroid), cubic units.</summary>
        public double Volume => ConvexPlan.Area(Plan) * (IsSloped ? CentroidTop() - Bottom : Top - Bottom);

        /// <summary>How far <paramref name="point"/> lies outside the prism; 0 on or inside it.</summary>
        public double DistanceOutside(Vector3 point) {
            return DistanceOutside(point.X, point.Y, point.Z);
        }

        /// <summary>How far (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) lies outside the prism; 0 on or inside it.</summary>
        public double DistanceOutside(double x, double y, double z) {
            if (IsSloped) return SlopedDistanceOutside(x, y, z);

            double plan = ConvexPlan.Distance(Plan, x, z);
            double vertical = Math.Max(0.0, Math.Max(Bottom - y, y - Top));
            return Math.Sqrt(plan * plan + vertical * vertical);
        }

        // Under the bottom: plan distance and drop; above it, the distance to the upper surface (plane projection inside
        // the plan, else the nearest rim point, exact per edge).
        private double SlopedDistanceOutside(double x, double y, double z) {
            if (y < Bottom) {
                double plan = ConvexPlan.Distance(Plan, x, z);
                return Math.Sqrt(plan * plan + (Bottom - y) * (Bottom - y));
            }

            double rise = y - TopAt(x, z);
            if (rise <= 0.0 && ConvexPlan.Distance(Plan, x, z) <= 0.0) return 0.0;

            if (rise > 0.0) {
                double lengthSquared = 1.0 + SlopeX * SlopeX + SlopeZ * SlopeZ;
                double pull = rise / lengthSquared;
                if (ConvexPlan.Distance(Plan, x + pull * SlopeX, z + pull * SlopeZ) <= 0.0) return rise / Math.Sqrt(lengthSquared);
            }

            double best = double.PositiveInfinity;
            for (int index = 0; index < Plan.Count; index++) best = Math.Min(best, RimEdgeSquared(index, x, y, z));
            return Math.Sqrt(best);
        }

        private double RimEdgeSquared(int index, double x, double y, double z) {
            PlanPoint start = Plan[index];
            PlanPoint end = Plan[(index + 1) % Plan.Count];
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
            if (!(lengthSquared > 0.0)) return RimPointSquared(start, 0.0, 0.0, 0.0, x, y, z);

            double projection = (x - start.X) * edgeX + (z - start.Z) * edgeZ;
            double climb = SlopeX * edgeX + SlopeZ * edgeZ;
            double startGap = y - TopAt(start.X, start.Z);
            double best = Math.Min(RimPointSquared(start, edgeX, edgeZ, (projection + climb * startGap) / (lengthSquared + climb * climb), x, y, z),
                RimPointSquared(start, edgeX, edgeZ, projection / lengthSquared, x, y, z));
            return climb != 0.0 ? Math.Min(best, RimPointSquared(start, edgeX, edgeZ, startGap / climb, x, y, z)) : best;
        }

        private double RimPointSquared(PlanPoint start, double edgeX, double edgeZ, double along, double x, double y, double z) {
            double clamped = Math.Max(0.0, Math.Min(1.0, along));
            double pointX = start.X + edgeX * clamped, pointZ = start.Z + edgeZ * clamped;
            double gapY = Math.Max(0.0, y - TopAt(pointX, pointZ));
            return (x - pointX) * (x - pointX) + (z - pointZ) * (z - pointZ) + gapY * gapY;
        }

        private double CentroidTop() {
            double area = 0.0, sumX = 0.0, sumZ = 0.0;
            for (int index = 0; index < Plan.Count; index++) {
                PlanPoint current = Plan[index], next = Plan[(index + 1) % Plan.Count];
                double cross = current.X * next.Z - next.X * current.Z;
                area += cross;
                sumX += (current.X + next.X) * cross;
                sumZ += (current.Z + next.Z) * cross;
            }
            return area != 0.0 ? TopAt(sumX / (3.0 * area), sumZ / (3.0 * area)) : Top;
        }

        /// <inheritdoc />
        public bool Equals(EnvelopePrism other) {
            if (other is null || !Bottom.Equals(other.Bottom) || !Top.Equals(other.Top) || Plan.Count != other.Plan.Count) return false;
            if (!TopY0.Equals(other.TopY0) || !SlopeX.Equals(other.SlopeX) || !SlopeZ.Equals(other.SlopeZ)) return false;

            for (int index = 0; index < Plan.Count; index++) {
                if (!Plan[index].Equals(other.Plan[index])) return false;
            }
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is EnvelopePrism other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() {
            int hash = Bottom.GetHashCode() * 397 ^ Top.GetHashCode() ^ SlopeX.GetHashCode() * 31 ^ SlopeZ.GetHashCode() * 17;
            foreach (PlanPoint point in Plan) hash = hash * 31 + point.GetHashCode();
            return hash;
        }
    }
}
