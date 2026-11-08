using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One landmark, or one mass of a landmark, as the sky sees it: the height of its top, its footprint hull and its
    /// anchor in absolute xz (metres), and its tier. Pure data with a bounding circle, so a cloud far from it is ruled out with one test.
    /// </summary>
    public sealed class LandmarkSkyShape {
        private readonly PlanPoint[] _hull;

        /// <summary>A landmark topping out at <paramref name="plateauY"/> over the closed polygon <paramref name="hull"/> (at least 3 points).</summary>
        public LandmarkSkyShape(double plateauY, IReadOnlyList<PlanPoint> hull, double anchorX, double anchorZ, LandmarkSkyTier tier) {
            if (hull == null) throw new ArgumentNullException(nameof(hull));
            if (hull.Count < 3) throw new ArgumentException("A hull needs at least 3 points.", nameof(hull));

            PlateauY = plateauY;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            Tier = tier;
            _hull = new PlanPoint[hull.Count];
            for (int index = 0; index < hull.Count; index++) _hull[index] = hull[index];
            BoundCircle();
        }

        /// <summary>Height of the landmark's top above the ground, metres.</summary>
        public double PlateauY { get; }

        /// <summary>Footprint polygon, absolute xz.</summary>
        public IReadOnlyList<PlanPoint> Hull => _hull;

        /// <summary>Where the landmark stands, absolute xz: the centre giants gather clouds about.</summary>
        public double AnchorX { get; }
        public double AnchorZ { get; }

        public LandmarkSkyTier Tier { get; }

        /// <summary>Circle holding the whole hull: its centre (mean of the hull points) and radius.</summary>
        public double BoundX { get; private set; }
        public double BoundZ { get; private set; }
        public double BoundRadius { get; private set; }

        /// <summary>Horizontal distance from (<paramref name="x"/>, <paramref name="z"/>) to the hull, 0 inside it.</summary>
        public double DistanceTo(double x, double z) {
            if (Contains(x, z)) return 0.0;

            double nearest = double.MaxValue;
            for (int index = 0; index < _hull.Length; index++) {
                PlanPoint from = _hull[index];
                PlanPoint to = _hull[(index + 1) % _hull.Length];
                nearest = Math.Min(nearest, SegmentDistanceSquared(x, z, from.X, from.Z, to.X, to.Z));
            }
            return Math.Sqrt(nearest);
        }

        /// <summary>True when (<paramref name="x"/>, <paramref name="z"/>) lies inside the hull (crossing count, any simple polygon).</summary>
        public bool Contains(double x, double z) {
            bool isInside = false;
            for (int index = 0, previous = _hull.Length - 1; index < _hull.Length; previous = index++) {
                if (Crosses(_hull[index], _hull[previous], x, z)) isInside = !isInside;
            }
            return isInside;
        }

        /// <summary>Squared distance from a point to the segment from (<paramref name="ax"/>, <paramref name="az"/>) to (<paramref name="bx"/>, <paramref name="bz"/>).</summary>
        public static double SegmentDistanceSquared(double x, double z, double ax, double az, double bx, double bz) {
            double edgeX = bx - ax;
            double edgeZ = bz - az;
            double length = edgeX * edgeX + edgeZ * edgeZ;
            double share = length > 0.0 ? ((x - ax) * edgeX + (z - az) * edgeZ) / length : 0.0;
            share = Math.Max(0.0, Math.Min(1.0, share));
            double deltaX = x - (ax + edgeX * share);
            double deltaZ = z - (az + edgeZ * share);
            return deltaX * deltaX + deltaZ * deltaZ;
        }

        private static bool Crosses(PlanPoint point, PlanPoint previous, double x, double z) {
            if ((point.Z > z) == (previous.Z > z)) return false;

            double crossX = point.X + (z - point.Z) * (previous.X - point.X) / (previous.Z - point.Z);
            return x < crossX;
        }

        private void BoundCircle() {
            double sumX = 0.0;
            double sumZ = 0.0;
            foreach (PlanPoint point in _hull) {
                sumX += point.X;
                sumZ += point.Z;
            }

            BoundX = sumX / _hull.Length;
            BoundZ = sumZ / _hull.Length;
            double radius = 0.0;
            foreach (PlanPoint point in _hull) {
                double deltaX = point.X - BoundX;
                double deltaZ = point.Z - BoundZ;
                radius = Math.Max(radius, deltaX * deltaX + deltaZ * deltaZ);
            }
            BoundRadius = Math.Sqrt(radius);
        }
    }
}
