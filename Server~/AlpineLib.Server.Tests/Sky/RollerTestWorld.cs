using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // An open test world for rollers: gently rolling ground (or a caller's support), discs too big to hop, and an
    // optional edge east of which nothing is loaded.
    public sealed class RollerTestWorld : IRollerWorld {
        private readonly List<double[]> _stops = new List<double[]>();

        public Func<double, double, double, RollerSupport> Support { get; set; }

        public Func<double, double, double> Surface { get; set; } = Height;

        /// <summary>Absolute x past which the world is not loaded (positive infinity: nowhere).</summary>
        public double EdgeX { get; set; } = double.PositiveInfinity;

        public IReadOnlyList<double[]> Stops => _stops;

        public static RollerTestWorld Open() {
            return new RollerTestWorld();
        }

        /// <summary>Open ground with seeded stop discs (x, z, radius) scattered over ±150 m.</summary>
        public static RollerTestWorld Scattered(ulong seed, int count) {
            var world = new RollerTestWorld();
            var random = new SplitMix64(seed);
            for (int index = 0; index < count; index++) world.AddStop(random.NextDouble() * 300.0 - 150.0, random.NextDouble() * 300.0 - 150.0, 0.7 + random.NextDouble() * 3.0);

            return world;
        }

        public void AddStop(double x, double z, double radius) {
            _stops.Add(new[] { x, z, radius });
        }

        public RollerBlock BlockAt(double x, double z, double radius) {
            if (x > EdgeX) return RollerBlock.Edge;

            return Penetration(x, z, radius) > 0.0 ? RollerBlock.Stop : RollerBlock.None;
        }

        public RollerSupport SupportAt(double x, double z, double radius) {
            return Support != null ? Support(x, z, radius) : RollerSupport.BallOnHeights(Surface, x, z, radius);
        }

        public double SurfaceAt(double x, double z) {
            return Surface(x, z);
        }

        /// <summary>How far a ball of <paramref name="radius"/> at (x, z) reaches into the nearest stop disc (negative: clear).</summary>
        public double Penetration(double x, double z, double radius) {
            double worst = double.NegativeInfinity;
            foreach (double[] disc in _stops) {
                double dx = x - disc[0];
                double dz = z - disc[1];
                worst = Math.Max(worst, disc[2] + radius - Math.Sqrt(dx * dx + dz * dz));
            }
            return worst;
        }

        public static double Height(double x, double z) {
            return 0.15 * Math.Sin(x / 9.0) * Math.Cos(z / 13.0);
        }
    }
}
