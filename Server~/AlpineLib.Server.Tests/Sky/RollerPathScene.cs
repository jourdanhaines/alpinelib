using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // A 128 m cell with blocking discs and a gently rolling ground.
    public sealed class RollerPathScene {
        private const double HalfCell = 64.0;
        private static readonly RollerPathSettings Settings = RollerPathSettings.Default;

        private readonly List<double[]> _discs = new List<double[]>();
        private readonly double _radius;

        private RollerPathScene(double radius) {
            _radius = radius;
        }

        public static RollerPathScene Open(double radius) {
            return new RollerPathScene(radius);
        }

        public static RollerPathScene Random(ulong seed) {
            var random = new SplitMix64(seed);
            var scene = new RollerPathScene(0.3 + random.NextDouble() * 0.3);
            int count = random.NextInt(10, 60);
            for (int index = 0; index < count; index++) {
                scene._discs.Add(new[] { random.NextDouble() * 128.0 - 64.0, random.NextDouble() * 128.0 - 64.0, 0.7 + random.NextDouble() * 3.0 });
            }
            return scene;
        }

        public RollerPath Build(ulong seed, WindVector prevailing, double slotShift = 0.0) {
            var random = new SplitMix64(StableHash.Mix(seed, 77UL));
            double slotX = (random.NextDouble() * 2.0 - 1.0) * 50.0 + slotShift;
            double slotZ = (random.NextDouble() * 2.0 - 1.0) * 50.0;
            if (_discs.Count == 0) {
                slotX = -prevailing.X * 40.0;
                slotZ = -prevailing.Z * 40.0;
            }
            double size = RollerPath.SizeOf(seed, Settings);
            double radius = _radius * size;
            return RollerPath.Build(seed, slotX, slotZ, radius, size, prevailing, Settings, (x, z) => Penetration(x, z, radius) <= 0.0, Height);
        }

        // How far a ball of the scene's radius at size 1 (or `radius`) at (x, z) reaches into a disc or past the cell.
        public double Penetration(double x, double z, double radius = 0.0) {
            if (radius <= 0.0) radius = _radius;
            double worst = Math.Max(Math.Abs(x), Math.Abs(z)) - (HalfCell - radius);
            foreach (double[] disc in _discs) {
                double dx = x - disc[0];
                double dz = z - disc[1];
                worst = Math.Max(worst, disc[2] + radius - Math.Sqrt(dx * dx + dz * dz));
            }
            return worst;
        }

        private static double Height(double x, double z) {
            return 0.15 * Math.Sin(x / 9.0) * Math.Cos(z / 13.0);
        }
    }
}
