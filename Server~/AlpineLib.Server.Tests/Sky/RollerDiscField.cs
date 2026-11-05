using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // An open test world strewn with clustered round stops like the desert's (shrubs, cactus trunks, boulders at their
    // ground radius, a few wide rocks), binned so a long roll can ask for nearby stops cheaply.
    public sealed class RollerDiscField : IRollerWorld {
        private const double BinSize = 8.0;
        private readonly Dictionary<(long X, long Z), List<RollerDisc>> _bins = new Dictionary<(long X, long Z), List<RollerDisc>>();
        private double _widest;

        public int Count { get; private set; }

        /// <summary>
        /// Clusters of 2-5 stops (radius 0.3-1.0 m, one in twenty 1.5-3 m) at <paramref name="clustersPerHectare"/>
        /// over a square of <paramref name="half"/> metres either side of the origin.
        /// </summary>
        public static RollerDiscField Clustered(ulong seed, double clustersPerHectare, double half) {
            var field = new RollerDiscField();
            var random = new SplitMix64(seed);
            int clusters = (int)(clustersPerHectare * (2.0 * half) * (2.0 * half) / 10000.0);
            for (int cluster = 0; cluster < clusters; cluster++) {
                double centreX = (random.NextDouble() * 2.0 - 1.0) * half, centreZ = (random.NextDouble() * 2.0 - 1.0) * half;
                int members = 2 + (int)(random.NextDouble() * 4.0);
                for (int member = 0; member < members; member++) {
                    double radius = random.NextDouble() < 0.05 ? 1.5 + random.NextDouble() * 1.5 : 0.3 + random.NextDouble() * 0.7;
                    field.Add(new RollerDisc(centreX + (random.NextDouble() * 2.0 - 1.0) * 6.0, centreZ + (random.NextDouble() * 2.0 - 1.0) * 6.0, radius));
                }
            }
            return field;
        }

        public void Add(RollerDisc disc) {
            (long X, long Z) bin = BinOf(disc.X, disc.Z);
            if (!_bins.TryGetValue(bin, out List<RollerDisc> list)) _bins[bin] = list = new List<RollerDisc>();
            list.Add(disc);
            _widest = Math.Max(_widest, disc.Radius);
            Count++;
        }

        public RollerBlock BlockAt(double x, double z, double radius) {
            return Clearance(x, z, radius) < 0.0 ? RollerBlock.Stop : RollerBlock.None;
        }

        public RollerSupport SupportAt(double x, double z, double radius) {
            return RollerSupport.Level(0.0);
        }

        public double SurfaceAt(double x, double z) {
            return 0.0;
        }

        public bool StopsNear(double x, double z, double radius, double reach, List<RollerDisc> into) {
            ForNear(x, z, reach + radius + _widest, (disc) => {
                if (disc.ClearanceOf(x, z, radius) <= reach) into.Add(disc);
            });
            return true;
        }

        /// <summary>Least gap between a ball of <paramref name="radius"/> at (x, z) and any stop (positive infinity: none near).</summary>
        public double Clearance(double x, double z, double radius) {
            double least = double.PositiveInfinity;
            ForNear(x, z, radius + _widest + 1.0, (disc) => least = Math.Min(least, disc.ClearanceOf(x, z, radius)));
            return least;
        }

        private void ForNear(double x, double z, double reach, Action<RollerDisc> visit) {
            (long X, long Z) from = BinOf(x - reach, z - reach);
            (long X, long Z) to = BinOf(x + reach, z + reach);
            for (long column = from.X; column <= to.X; column++) {
                for (long row = from.Z; row <= to.Z; row++) {
                    if (!_bins.TryGetValue((column, row), out List<RollerDisc> list)) continue;

                    foreach (RollerDisc disc in list) visit(disc);
                }
            }
        }

        private static (long X, long Z) BinOf(double x, double z) {
            return ((long)Math.Floor(x / BinSize), (long)Math.Floor(z / BinSize));
        }
    }
}
