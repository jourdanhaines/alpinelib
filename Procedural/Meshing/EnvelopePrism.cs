using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// A vertical convex prism around part of a look: a counter-clockwise <see cref="ConvexPlan"/> polygon in the
    /// mesh's space extruded from <see cref="Bottom"/> to <see cref="Top"/>. One piece of a
    /// <see cref="PrismEnvelopeFitter"/> result.
    /// </summary>
    public sealed class EnvelopePrism : IEquatable<EnvelopePrism> {
        /// <summary>Builds a prism; the plan is copied.</summary>
        public EnvelopePrism(double bottom, double top, IReadOnlyList<PlanPoint> plan) {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!(top > bottom)) throw new ArgumentException("Top must be above bottom.", nameof(top));

            Bottom = bottom;
            Top = top;
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

        /// <summary>Plan area times height, cubic units.</summary>
        public double Volume => ConvexPlan.Area(Plan) * (Top - Bottom);

        /// <summary>How far <paramref name="point"/> lies outside the prism; 0 on or inside it.</summary>
        public double DistanceOutside(Vector3 point) {
            return DistanceOutside(point.X, point.Y, point.Z);
        }

        /// <summary>How far (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) lies outside the prism; 0 on or inside it.</summary>
        public double DistanceOutside(double x, double y, double z) {
            double plan = ConvexPlan.Distance(Plan, x, z);
            double vertical = Math.Max(0.0, Math.Max(Bottom - y, y - Top));
            return Math.Sqrt(plan * plan + vertical * vertical);
        }

        /// <inheritdoc />
        public bool Equals(EnvelopePrism other) {
            if (other is null || !Bottom.Equals(other.Bottom) || !Top.Equals(other.Top) || Plan.Count != other.Plan.Count) return false;

            for (int index = 0; index < Plan.Count; index++) {
                if (!Plan[index].Equals(other.Plan[index])) return false;
            }
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is EnvelopePrism other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() {
            int hash = Bottom.GetHashCode() * 397 ^ Top.GetHashCode();
            foreach (PlanPoint point in Plan) hash = hash * 31 + point.GetHashCode();
            return hash;
        }
    }
}
