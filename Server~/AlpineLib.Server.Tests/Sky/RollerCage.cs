using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // A synthetic tumbleweed cage of ball radius `radius`: irregular hoops on small circles (0.7-1.05 r, planes up to
    // 0.45 r off the centre) and a few stubs, like the kit looks, as raw positions and as a RollerShape.
    public static class RollerCage {
        public static double[] Positions(ulong seed, double radius) {
            var random = new SplitMix64(seed);
            var positions = new List<double>();
            int hoops = random.NextInt(5, 8);
            for (int hoop = 0; hoop < hoops; hoop++) AddHoop(random, radius, positions);

            for (int stub = 0; stub < 6; stub++) {
                RandomAxis(random, out double x, out double y, out double z);
                double reach = radius * (0.9 + 0.15 * random.NextDouble());
                positions.Add(x * reach);
                positions.Add(y * reach);
                positions.Add(z * reach);
            }
            return positions.ToArray();
        }

        public static RollerShape Shape(ulong seed, double radius) {
            return RollerShape.FromPositions(Positions(seed, radius));
        }

        private static void AddHoop(SplitMix64 random, double radius, List<double> positions) {
            RandomAxis(random, out double ax, out double ay, out double az);
            double offset = (random.NextDouble() * 2.0 - 1.0) * 0.45 * radius;
            double ring = radius * (0.7 + 0.35 * random.NextDouble());
            double circle = Math.Sqrt(Math.Max(0.0, ring * ring - offset * offset));
            Perpendicular(ax, ay, az, out double ux, out double uy, out double uz);
            double vx = ay * uz - az * uy, vy = az * ux - ax * uz, vz = ax * uy - ay * ux;
            for (int point = 0; point < 24; point++) {
                double angle = 2.0 * Math.PI * point / 24.0;
                double cos = Math.Cos(angle) * circle, sin = Math.Sin(angle) * circle;
                positions.Add(ax * offset + ux * cos + vx * sin);
                positions.Add(ay * offset + uy * cos + vy * sin);
                positions.Add(az * offset + uz * cos + vz * sin);
            }
        }

        private static void RandomAxis(SplitMix64 random, out double x, out double y, out double z) {
            y = random.NextDouble() * 2.0 - 1.0;
            double ring = Math.Sqrt(1.0 - y * y);
            double angle = random.NextDouble() * 2.0 * Math.PI;
            x = ring * Math.Cos(angle);
            z = ring * Math.Sin(angle);
        }

        private static void Perpendicular(double x, double y, double z, out double px, out double py, out double pz) {
            bool useUp = Math.Abs(y) < 0.9;
            double ox = useUp ? 0.0 : 1.0, oy = useUp ? 1.0 : 0.0;
            px = y * 0.0 - z * oy;
            py = z * ox - x * 0.0;
            pz = x * oy - y * ox;
            double length = Math.Sqrt(px * px + py * py + pz * pz);
            px /= length;
            py /= length;
            pz /= length;
        }
    }
}
