using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// How a set of envelope boxes, spheres and prisms sits around its look, for gates: the standoff of exposed collider
    /// surface from the look in a height band (an invisible wall where it is large) and the air under exposed
    /// walkable collider tops (an invisible ledge). Sample spacing is <see cref="Spacing"/>; distances are capped.
    /// </summary>
    public sealed class EnvelopeMetrics {
        private const double CoveredTolerance = TriangleProximity.RimTolerance;
        private const double WalkableNormal = 0.7071;
        private const double TopLift = 0.02;
        private const double OverTop = 0.05;
        private const int SphereRings = 24;

        // Float samples on a slanted prism rim shared with another prism land a hair either side of it.
        private const double PrismRimTolerance = 1e-5;

        private readonly IReadOnlyList<EnvelopeBox> _boxes;
        private readonly IReadOnlyList<EnvelopeSphere> _spheres;
        private readonly IReadOnlyList<EnvelopePrism> _prisms;
        private readonly TriangleProximity _look;
        private double _worst;

        /// <summary>Metrics of <paramref name="boxes"/> and <paramref name="spheres"/> around the faces of <paramref name="corners"/>.</summary>
        public EnvelopeMetrics(IReadOnlyList<Vector3> corners, IReadOnlyList<EnvelopeBox> boxes, IReadOnlyList<EnvelopeSphere> spheres, double spacing)
            : this(corners, boxes, spheres, null, spacing) { }

        /// <summary>Metrics of <paramref name="boxes"/>, <paramref name="spheres"/> and <paramref name="prisms"/> around the faces of <paramref name="corners"/>.</summary>
        public EnvelopeMetrics(
            IReadOnlyList<Vector3> corners,
            IReadOnlyList<EnvelopeBox> boxes,
            IReadOnlyList<EnvelopeSphere> spheres,
            IReadOnlyList<EnvelopePrism> prisms,
            double spacing) {
            _boxes = boxes ?? Array.Empty<EnvelopeBox>();
            _spheres = spheres ?? Array.Empty<EnvelopeSphere>();
            _prisms = prisms ?? Array.Empty<EnvelopePrism>();
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
        /// A prism cap strip within this plan distance of another prism that reaches at least <see cref="LipRise"/>
        /// above the cap and starts within a capsule diameter (twice this) above it is a lip against a wall, not a
        /// ledge, and leaves no air in <see cref="LedgeAir"/>; lower neighbours never hide a cap. Look metres (callers pass the capsule radius over the look's scale). Defaults to
        /// 0.02; <see cref="PrismEnvelopeFitter"/> sets <see cref="PrismEnvelopeSettings.LedgeLip"/>.
        /// </summary>
        public double LedgeLip { get; set; } = CoveredTolerance;

        /// <summary>
        /// How far above a cap a neighbour must rise before a capsule on the cap's lip touches it, look metres.
        /// Defaults to 0.15.
        /// </summary>
        public double LipRise { get; set; } = 0.15;

        /// <summary>
        /// Distances (ascending) from exposed side-face points of the boxes and prisms and surface points of the spheres with
        /// heights in [<paramref name="low"/>, <paramref name="high"/>] to the nearest look face, each at most <paramref name="cap"/>.
        /// </summary>
        public List<double> Standoff(double low, double high, double cap) {
            var distances = new List<double>();
            _worst = double.NegativeInfinity;
            for (int index = 0; index < _boxes.Count; index++) BoxSides(index, low, high, cap, distances);
            for (int index = 0; index < _spheres.Count; index++) SphereSurface(index, low, high, cap, distances);
            for (int index = 0; index < _prisms.Count; index++) PrismSides(index, low, high, cap, distances);
            distances.Sort();
            return distances;
        }

        /// <summary>
        /// Air (ascending) between exposed walkable collider tops with heights in [<paramref name="low"/>, <paramref name="high"/>]
        /// and the look or <paramref name="ground"/> under them: box and prism caps, and sphere surface no steeper than 45°.
        /// </summary>
        public List<double> LedgeAir(double ground, double low, double high) {
            var air = new List<double>();
            _worst = double.NegativeInfinity;
            for (int index = 0; index < _boxes.Count; index++) BoxTop(index, ground, low, high, air);
            for (int index = 0; index < _spheres.Count; index++) SphereCap(index, ground, low, high, air);
            for (int index = 0; index < _prisms.Count; index++) PrismTop(index, ground, low, high, air);
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
                    if (!IsCovered(point + normal * (float)CoveredTolerance, index, -1, -1)) AddStandoff(point, _look.Distance(point, cap), distances);
                }
            }
        }

        private void SphereSurface(int index, double low, double high, double cap, List<double> distances) {
            EnvelopeSphere sphere = _spheres[index];
            foreach (Vector3 normal in SphereNormals()) {
                Vector3 point = sphere.Center + normal * sphere.Radius;
                if (point.Y < low || point.Y > high || IsCovered(point + normal * (float)CoveredTolerance, -1, index, -1)) continue;

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

        // Every side face of the prism, sampled on a grid of about the spacing along the edge and up the band.
        private void PrismSides(int index, double low, double high, double cap, List<double> distances) {
            EnvelopePrism prism = _prisms[index];
            double bottom = Math.Max(low, prism.Bottom);
            double top = Math.Min(high, prism.Top);
            if (bottom > top) return;

            int rows = Math.Max(1, (int)Math.Ceiling((top - bottom) / Spacing));
            for (int edge = 0; edge < prism.Plan.Count; edge++) {
                PlanPoint start = prism.Plan[edge];
                PlanPoint end = prism.Plan[(edge + 1) % prism.Plan.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
                if (!(length > 0.0)) continue;

                var normal = new Vector3((float)(edgeZ / length), 0f, (float)(-edgeX / length));
                int columns = Math.Max(1, (int)Math.Ceiling(length / Spacing));
                for (int column = 0; column <= columns; column++) {
                    double share = (double)column / columns;
                    for (int row = 0; row <= rows; row++) {
                        var point = new Vector3((float)(start.X + edgeX * share), (float)(bottom + (top - bottom) * row / rows), (float)(start.Z + edgeZ * share));
                        if (IsCovered(point + normal * (float)CoveredTolerance, -1, -1, index)) continue;

                        // A side buried inside the look's own solid is no wall a capsule meets; within the covered
                        // tolerance of the surface a sample counts either way, so the costly inside test is skipped.
                        double distance = _look.Distance(point, cap);
                        if (distance > CoveredTolerance && _look.IsInside(point.X, point.Y, point.Z)) continue;

                        AddStandoff(point, distance, distances);
                    }
                }
            }
        }

        // The cap sampled on the plan grid inside the polygon and along its rim.
        private void PrismTop(int index, double ground, double low, double high, List<double> air) {
            EnvelopePrism prism = _prisms[index];
            if (prism.Top < low || prism.Top > high) return;

            foreach (PlanPoint sample in CapSamples(prism.Plan)) {
                var point = new Vector3((float)sample.X, (float)(prism.Top + TopLift), (float)sample.Z);
                if (!IsInsideAny(point) && !IsNearOtherPrism(point, index)) AddLedge(point, prism.Top - _look.HeightBelow(point.X, point.Z, prism.Top + OverTop, ground), air);
            }
        }

        private List<PlanPoint> CapSamples(IReadOnlyList<PlanPoint> plan) {
            var samples = new List<PlanPoint>();
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            for (int edge = 0; edge < plan.Count; edge++) {
                PlanPoint start = plan[edge];
                PlanPoint end = plan[(edge + 1) % plan.Count];
                minX = Math.Min(minX, start.X);
                maxX = Math.Max(maxX, start.X);
                minZ = Math.Min(minZ, start.Z);
                maxZ = Math.Max(maxZ, start.Z);
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z)) / Spacing));
                for (int step = 0; step < steps; step++) samples.Add(new PlanPoint(start.X + (end.X - start.X) * step / steps, start.Z + (end.Z - start.Z) * step / steps));
            }
            int columns = Math.Max(1, (int)Math.Ceiling((maxX - minX) / Spacing));
            int rows = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / Spacing));
            for (int column = 1; column < columns; column++) {
                for (int row = 1; row < rows; row++) {
                    double x = minX + (maxX - minX) * column / columns, z = minZ + (maxZ - minZ) * row / rows;
                    if (ConvexPlan.Distance(plan, x, z) <= 0.0) samples.Add(new PlanPoint(x, z));
                }
            }
            return samples;
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
        private bool IsCovered(Vector3 point, int boxIndex, int sphereIndex, int prismIndex) {
            for (int index = 0; index < _boxes.Count; index++) {
                if (index != boxIndex && _boxes[index].DistanceOutside(point) <= 0.0) return true;
            }
            for (int index = 0; index < _spheres.Count; index++) {
                if (index != sphereIndex && _spheres[index].DistanceOutside(point) <= 0.0) return true;
            }
            for (int index = 0; index < _prisms.Count; index++) {
                if (index != prismIndex && _prisms[index].Holds(point.X, point.Y, point.Z, PrismRimTolerance)) return true;
            }
            return false;
        }

        // A cap strip narrower than the lip width against a prism rising above it is no ledge.
        private bool IsNearOtherPrism(Vector3 point, int prismIndex) {
            for (int index = 0; index < _prisms.Count; index++) {
                EnvelopePrism other = _prisms[index];
                double cap = point.Y - TopLift;
                if (index == prismIndex || other.Bottom > cap + 2.0 * LedgeLip || other.Top < cap + LipRise) continue;
                if (other.PlanHolds(point.X, point.Z, LedgeLip)) return true;
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
            foreach (EnvelopePrism prism in _prisms) {
                if (prism.Holds(point.X, point.Y, point.Z, PrismRimTolerance)) return true;
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
