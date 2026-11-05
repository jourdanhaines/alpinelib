using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// How a set of envelope boxes and spheres sits around its look, for gates: the standoff of exposed collider
    /// surface from the look in a height band (an invisible wall where it is large) and the air under exposed
    /// walkable collider tops (an invisible ledge). Sample spacing is <see cref="Spacing"/>; distances are capped.
    /// </summary>
    public sealed class EnvelopeMetrics {
        private const double CoveredTolerance = 0.02;
        private const double WalkableNormal = 0.7071;
        private const double TopLift = 0.02;
        private const double OverTop = 0.05;
        private const int SphereRings = 24;

        private readonly IReadOnlyList<EnvelopeBox> _boxes;
        private readonly IReadOnlyList<EnvelopeSphere> _spheres;
        private readonly TriangleProximity _look;
        private double _worst;

        /// <summary>Metrics of <paramref name="boxes"/> and <paramref name="spheres"/> around the faces of <paramref name="corners"/>.</summary>
        public EnvelopeMetrics(IReadOnlyList<Vector3> corners, IReadOnlyList<EnvelopeBox> boxes, IReadOnlyList<EnvelopeSphere> spheres, double spacing) {
            _boxes = boxes ?? Array.Empty<EnvelopeBox>();
            _spheres = spheres ?? Array.Empty<EnvelopeSphere>();
            Spacing = spacing;
            _look = new TriangleProximity(corners, Math.Max(spacing * 4.0, 0.5));
        }

        /// <summary>Where the largest standoff of the last <see cref="Standoff"/> call was sampled.</summary>
        public Vector3 WorstStandoffPoint { get; private set; }

        /// <summary>Where the most air of the last <see cref="LedgeAir"/> call was sampled.</summary>
        public Vector3 WorstLedgePoint { get; private set; }

        /// <summary>Sample spacing on collider surfaces, metres.</summary>
        public double Spacing { get; }

        /// <summary>
        /// Distances (ascending) from exposed side-face points of the boxes and surface points of the spheres with
        /// heights in [<paramref name="low"/>, <paramref name="high"/>] to the nearest look face, each at most <paramref name="cap"/>.
        /// </summary>
        public List<double> Standoff(double low, double high, double cap) {
            var distances = new List<double>();
            _worst = double.NegativeInfinity;
            for (int index = 0; index < _boxes.Count; index++) BoxSides(index, low, high, cap, distances);
            for (int index = 0; index < _spheres.Count; index++) SphereSurface(index, low, high, cap, distances);
            distances.Sort();
            return distances;
        }

        /// <summary>
        /// Air (ascending) between exposed walkable collider tops with heights in [<paramref name="low"/>, <paramref name="high"/>]
        /// and the look or <paramref name="ground"/> under them: box tops, and sphere surface no steeper than 45°.
        /// </summary>
        public List<double> LedgeAir(double ground, double low, double high) {
            var air = new List<double>();
            _worst = double.NegativeInfinity;
            for (int index = 0; index < _boxes.Count; index++) BoxTop(index, ground, low, high, air);
            for (int index = 0; index < _spheres.Count; index++) SphereCap(index, ground, low, high, air);
            air.Sort();
            return air;
        }

        /// <summary>The value at quantile <paramref name="share"/> (0–1) of an ascending list; 0 when empty.</summary>
        public static double Quantile(List<double> sorted, double share) {
            if (sorted == null || sorted.Count == 0) return 0.0;

            return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * share))];
        }

        private void BoxSides(int index, double low, double high, double cap, List<double> distances) {
            EnvelopeBox box = _boxes[index];
            Axes(box, out Vector3 right, out Vector3 forward);
            double bottom = Math.Max(low, box.Center.Y - box.HalfExtents.Y);
            double top = Math.Min(high, box.Center.Y + box.HalfExtents.Y);
            if (bottom > top) return;

            for (int side = 0; side < 4; side++) {
                Vector3 normal = side == 0 ? right : side == 1 ? -right : side == 2 ? forward : -forward;
                Vector3 along = side < 2 ? forward : right;
                float depth = side < 2 ? box.HalfExtents.X : box.HalfExtents.Z;
                float width = side < 2 ? box.HalfExtents.Z : box.HalfExtents.X;
                SideSamples(index, box.Center + normal * depth, normal, along, width, bottom, top, cap, distances);
            }
        }

        private void SideSamples(int index, Vector3 middle, Vector3 normal, Vector3 along, float width, double bottom, double top, double cap, List<double> distances) {
            int columns = Math.Max(1, (int)Math.Ceiling(2.0 * width / Spacing));
            int rows = Math.Max(1, (int)Math.Ceiling((top - bottom) / Spacing));
            for (int column = 0; column <= columns; column++) {
                for (int row = 0; row <= rows; row++) {
                    Vector3 point = middle + along * (float)(-width + 2.0 * width * column / columns);
                    point.Y = (float)(bottom + (top - bottom) * row / rows);
                    if (!IsCovered(point + normal * (float)CoveredTolerance, index, -1)) AddStandoff(point, _look.Distance(point, cap), distances);
                }
            }
        }

        private void SphereSurface(int index, double low, double high, double cap, List<double> distances) {
            EnvelopeSphere sphere = _spheres[index];
            foreach (Vector3 normal in SphereNormals()) {
                Vector3 point = sphere.Center + normal * sphere.Radius;
                if (point.Y < low || point.Y > high || IsCovered(point + normal * (float)CoveredTolerance, -1, index)) continue;

                AddStandoff(point, _look.Distance(point, cap), distances);
            }
        }

        private void BoxTop(int index, double ground, double low, double high, List<double> air) {
            EnvelopeBox box = _boxes[index];
            double top = box.Center.Y + box.HalfExtents.Y;
            if (top < low || top > high) return;

            Axes(box, out Vector3 right, out Vector3 forward);
            int columns = Math.Max(1, (int)Math.Ceiling(2.0 * box.HalfExtents.X / Spacing));
            int rows = Math.Max(1, (int)Math.Ceiling(2.0 * box.HalfExtents.Z / Spacing));
            for (int column = 0; column <= columns; column++) {
                for (int row = 0; row <= rows; row++) {
                    Vector3 point = box.Center + right * (float)(box.HalfExtents.X * (2.0 * column / columns - 1.0)) + forward * (float)(box.HalfExtents.Z * (2.0 * row / rows - 1.0));
                    point.Y = (float)(top + TopLift);
                    if (!IsInsideAny(point)) AddLedge(point, top - _look.HeightBelow(point.X, point.Z, top + OverTop, ground), air);
                }
            }
        }

        private void SphereCap(int index, double ground, double low, double high, List<double> air) {
            EnvelopeSphere sphere = _spheres[index];
            foreach (Vector3 normal in SphereNormals()) {
                if (normal.Y < WalkableNormal) continue;

                Vector3 point = sphere.Center + normal * sphere.Radius;
                if (point.Y < low || point.Y > high || IsInsideAny(point + normal * (float)TopLift)) continue;

                AddLedge(point, point.Y - _look.HeightBelow(point.X, point.Z, point.Y + OverTop, ground), air);
            }
        }

        private void AddStandoff(Vector3 point, double distance, List<double> distances) {
            distances.Add(distance);
            if (distance <= _worst) return;

            _worst = distance;
            WorstStandoffPoint = point;
        }

        private void AddLedge(Vector3 point, double air, List<double> airs) {
            airs.Add(air);
            if (air <= _worst) return;

            _worst = air;
            WorstLedgePoint = point;
        }

        // Points on the unit sphere, rings of latitude about every 7.5°.
        private static IEnumerable<Vector3> SphereNormals() {
            for (int ring = 0; ring <= SphereRings; ring++) {
                double polar = Math.PI * ring / SphereRings;
                int around = Math.Max(1, (int)Math.Round(2 * SphereRings * Math.Sin(polar)));
                for (int step = 0; step < around; step++) {
                    double azimuth = 2.0 * Math.PI * step / around;
                    yield return new Vector3((float)(Math.Sin(polar) * Math.Cos(azimuth)), (float)Math.Cos(polar), (float)(Math.Sin(polar) * Math.Sin(azimuth)));
                }
            }
        }

        // True when the point (a surface sample pushed just off its own collider) lies in any other collider:
        // the surface there is inside the union, not exposed.
        private bool IsCovered(Vector3 point, int boxIndex, int sphereIndex) {
            for (int index = 0; index < _boxes.Count; index++) {
                if (index != boxIndex && _boxes[index].DistanceOutside(point) <= 0.0) return true;
            }
            for (int index = 0; index < _spheres.Count; index++) {
                if (index != sphereIndex && _spheres[index].DistanceOutside(point) <= 0.0) return true;
            }
            return false;
        }

        private bool IsInsideAny(Vector3 point) {
            foreach (EnvelopeBox box in _boxes) {
                if (box.DistanceOutside(point) <= 0.0) return true;
            }
            foreach (EnvelopeSphere sphere in _spheres) {
                if (sphere.DistanceOutside(point) <= 0.0) return true;
            }
            return false;
        }

        private static void Axes(EnvelopeBox box, out Vector3 right, out Vector3 forward) {
            double radians = box.YawDegrees * Math.PI / 180.0;
            right = new Vector3((float)Math.Cos(radians), 0f, (float)-Math.Sin(radians));
            forward = new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
        }
    }
}
