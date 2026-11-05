using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The sphere around a set of points that best trades radius against top height: the least of
    /// radius × (1 + w) + w × top-centre height, so a lump keeps its sphere crown near its own crown.
    /// </summary>
    /// <remarks>Pattern search over the centre with a halving step; deterministic, editor or bake time.</remarks>
    public static class SphereEnvelopeFitter {
        private const double FirstStep = 0.25;
        private const double LastStep = 0.0005;
        private const float Slack = 0.002f;

        private static readonly Vector3[] Moves = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };

        /// <summary>The sphere holding every point, centre weighted down by <paramref name="topWeight"/> (0: smallest sphere).</summary>
        public static EnvelopeSphere Fit(IReadOnlyList<Vector3> points, double topWeight) {
            if (points == null || points.Count == 0) throw new ArgumentException("Need at least one point.", nameof(points));
            if (topWeight < 0.0) throw new ArgumentException("topWeight must not be negative.", nameof(topWeight));

            Vector3 centre = Mean(points);
            double scale = Math.Max(Reach(points, centre), 1e-3);
            for (double step = FirstStep * scale; step > LastStep * scale; step *= 0.5) centre = Descend(points, centre, (float)step, topWeight);
            return new EnvelopeSphere(centre, (float)Reach(points, centre) + Slack * (float)scale);
        }

        private static Vector3 Descend(IReadOnlyList<Vector3> points, Vector3 centre, float step, double topWeight) {
            double best = Cost(points, centre, topWeight);
            bool moved = true;
            while (moved) {
                moved = false;
                foreach (Vector3 move in Moves) {
                    Vector3 candidate = centre + move * step;
                    double cost = Cost(points, candidate, topWeight);
                    if (cost >= best - 1e-12) continue;

                    best = cost;
                    centre = candidate;
                    moved = true;
                }
            }
            return centre;
        }

        private static double Cost(IReadOnlyList<Vector3> points, Vector3 centre, double topWeight) {
            return Reach(points, centre) * (1.0 + topWeight) + topWeight * centre.Y;
        }

        private static double Reach(IReadOnlyList<Vector3> points, Vector3 centre) {
            double reach = 0.0;
            foreach (Vector3 point in points) reach = Math.Max(reach, Vector3.Distance(point, centre));
            return reach;
        }

        private static Vector3 Mean(IReadOnlyList<Vector3> points) {
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 point in points) sum += point;
            return sum / points.Count;
        }
    }
}
