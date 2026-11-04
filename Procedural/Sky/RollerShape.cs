using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The outline of one rolling prop look at size 1, in its mesh's own axes around its pivot: only the points that are
    /// farthest out in some direction (its hull, sampled over many directions), so a <see cref="RollerProfile"/> of a sized,
    /// squashed and tipped prop is cheap to build, plus every distinct point (<see cref="AllPoint"/>) for dropping the prop onto
    /// rough ground where an inner twig may touch first. Built once per look from the mesh's positions (any thread), read-only after.
    /// </summary>
    public sealed class RollerShape {
        private const int SampleDirections = 2048;

        private readonly double[] _points;
        private readonly double[] _all;

        private RollerShape(double[] points, double[] all, double reach) {
            _points = points;
            _all = all;
            Reach = reach;
        }

        /// <summary>Hull points kept.</summary>
        public int PointCount => _points.Length / 3;

        /// <summary>Farthest any point is from the pivot, metres at size 1.</summary>
        public double Reach { get; }

        /// <summary>Distinct points of the mesh.</summary>
        public int AllCount => _all.Length / 3;

        public void AllPoint(int index, out double x, out double y, out double z) {
            x = _all[index * 3];
            y = _all[index * 3 + 1];
            z = _all[index * 3 + 2];
        }

        /// <summary>Hull point <paramref name="index"/>.</summary>
        public void Point(int index, out double x, out double y, out double z) {
            x = _points[index * 3];
            y = _points[index * 3 + 1];
            z = _points[index * 3 + 2];
        }

        /// <summary>The shape of the positions <paramref name="xyz"/> (x, y, z per point, metres around the pivot).</summary>
        public static RollerShape FromPositions(IReadOnlyList<double> xyz) {
            if (xyz == null) throw new ArgumentNullException(nameof(xyz));
            if (xyz.Count < 3 || xyz.Count % 3 != 0) throw new ArgumentException("Needs at least one point, three values each.", nameof(xyz));

            int count = xyz.Count / 3;
            var kept = new bool[count];
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int direction = 0; direction < SampleDirections; direction++) {
                double y = 1.0 - 2.0 * (direction + 0.5) / SampleDirections;
                double ring = Math.Sqrt(1.0 - y * y);
                double x = ring * Math.Cos(golden * direction);
                double z = ring * Math.Sin(golden * direction);
                kept[Farthest(xyz, count, x, y, z)] = true;
            }

            var points = new List<double>();
            var all = new List<double>();
            var seen = new HashSet<(double, double, double)>();
            double reach = 0.0;
            for (int index = 0; index < count; index++) {
                if (seen.Add((xyz[index * 3], xyz[index * 3 + 1], xyz[index * 3 + 2]))) {
                    all.Add(xyz[index * 3]);
                    all.Add(xyz[index * 3 + 1]);
                    all.Add(xyz[index * 3 + 2]);
                }
                if (!kept[index]) continue;

                double px = xyz[index * 3];
                double py = xyz[index * 3 + 1];
                double pz = xyz[index * 3 + 2];
                points.Add(px);
                points.Add(py);
                points.Add(pz);
                reach = Math.Max(reach, Math.Sqrt(px * px + py * py + pz * pz));
            }
            return new RollerShape(points.ToArray(), all.ToArray(), reach);
        }

        private static int Farthest(IReadOnlyList<double> xyz, int count, double x, double y, double z) {
            int best = 0;
            double most = double.NegativeInfinity;
            for (int index = 0; index < count; index++) {
                double along = xyz[index * 3] * x + xyz[index * 3 + 1] * y + xyz[index * 3 + 2] * z;
                if (along <= most) continue;

                most = along;
                best = index;
            }
            return best;
        }
    }
}
