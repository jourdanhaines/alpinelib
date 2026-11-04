using System;

namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Seeded Perlin-style gradient noise in one, two and three dimensions, returning values in
    /// [−1, 1] that are bit-identical on Mono, IL2CPP and .NET.
    /// </summary>
    /// <remarks>
    /// Determinism rests on using only IEEE double add/subtract/multiply (never fused), <c>Math.Floor</c>
    /// and integer hashing: no trig, no <c>Math.Pow</c>, no floats. Gradients are picked from fixed
    /// literal tables by <see cref="LatticeHash"/>, blended with the quintic fade 6t⁵ − 15t⁴ + 10t³ (C2
    /// continuous across lattice lines). Noise is zero on every lattice point. Inputs must be finite and
    /// within ±2⁶² after scaling. Static, allocation-free and thread-safe.
    /// </remarks>
    public static class GradientNoise {
        // Unit-gradient Perlin noise is bounded by sqrt(N)/2; these rescale each dimension to [-1, 1].
        private const double Scale1 = 2.0;
        private const double Scale2 = 1.4142135623730951;
        private const double Scale3 = 1.1547005383792515;

        private const double Diagonal = 0.70710678118654757;
        private const double Cos22 = 0.92387953251128674;
        private const double Sin22 = 0.38268343236508978;

        // Sixteen unit directions, 22.5 degrees apart.
        private static readonly double[] Gradient2X = {
            1.0, Cos22, Diagonal, Sin22, 0.0, -Sin22, -Diagonal, -Cos22,
            -1.0, -Cos22, -Diagonal, -Sin22, 0.0, Sin22, Diagonal, Cos22,
        };

        private static readonly double[] Gradient2Y = {
            0.0, Sin22, Diagonal, Cos22, 1.0, Cos22, Diagonal, Sin22,
            0.0, -Sin22, -Diagonal, -Cos22, -1.0, -Cos22, -Diagonal, -Sin22,
        };

        // The twelve cube-edge midpoint directions of improved Perlin noise, normalised.
        private static readonly double[] Gradient3X = {
            Diagonal, -Diagonal, Diagonal, -Diagonal, Diagonal, -Diagonal, Diagonal, -Diagonal, 0.0, 0.0, 0.0, 0.0,
        };

        private static readonly double[] Gradient3Y = {
            Diagonal, Diagonal, -Diagonal, -Diagonal, 0.0, 0.0, 0.0, 0.0, Diagonal, -Diagonal, Diagonal, -Diagonal,
        };

        private static readonly double[] Gradient3Z = {
            0.0, 0.0, 0.0, 0.0, Diagonal, Diagonal, -Diagonal, -Diagonal, Diagonal, Diagonal, -Diagonal, -Diagonal,
        };

        /// <summary>1D gradient noise at <paramref name="x"/>, in [−1, 1].</summary>
        public static double Sample1(ulong seed, double x) {
            double floorX = Math.Floor(x);
            long cellX = (long)floorX;
            double fracX = x - floorX;

            double left = Gradient1(seed, cellX) * fracX;
            double right = Gradient1(seed, cellX + 1) * (fracX - 1.0);
            double value = Lerp(left, right, Fade(fracX));
            return Clamp(value * Scale1);
        }

        /// <summary>2D gradient noise at (<paramref name="x"/>, <paramref name="y"/>), in [−1, 1].</summary>
        public static double Sample2(ulong seed, double x, double y) {
            double floorX = Math.Floor(x);
            double floorY = Math.Floor(y);
            long cellX = (long)floorX;
            long cellY = (long)floorY;
            double fracX = x - floorX;
            double fracY = y - floorY;

            double corner00 = Dot2(seed, cellX, cellY, fracX, fracY);
            double corner10 = Dot2(seed, cellX + 1, cellY, fracX - 1.0, fracY);
            double corner01 = Dot2(seed, cellX, cellY + 1, fracX, fracY - 1.0);
            double corner11 = Dot2(seed, cellX + 1, cellY + 1, fracX - 1.0, fracY - 1.0);

            double fadeX = Fade(fracX);
            double bottom = Lerp(corner00, corner10, fadeX);
            double top = Lerp(corner01, corner11, fadeX);
            return Clamp(Lerp(bottom, top, Fade(fracY)) * Scale2);
        }

        /// <summary>3D gradient noise at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), in [−1, 1].</summary>
        public static double Sample3(ulong seed, double x, double y, double z) {
            double floorX = Math.Floor(x);
            double floorY = Math.Floor(y);
            double floorZ = Math.Floor(z);
            long cellX = (long)floorX;
            long cellY = (long)floorY;
            long cellZ = (long)floorZ;
            double fracX = x - floorX;
            double fracY = y - floorY;
            double fracZ = z - floorZ;

            double fadeX = Fade(fracX);
            double fadeY = Fade(fracY);
            double near = Layer3(seed, cellX, cellY, cellZ, fracX, fracY, fracZ, fadeX, fadeY);
            double far = Layer3(seed, cellX, cellY, cellZ + 1, fracX, fracY, fracZ - 1.0, fadeX, fadeY);
            return Clamp(Lerp(near, far, Fade(fracZ)) * Scale3);
        }

        /// <summary>The quintic fade 6t⁵ − 15t⁴ + 10t³ used to blend lattice corners.</summary>
        public static double Fade(double t) {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        private static double Gradient1(ulong seed, long cellX) {
            return LatticeHash.ToUnit(LatticeHash.Hash(seed, cellX)) * 2.0 - 1.0;
        }

        private static double Dot2(ulong seed, long cellX, long cellY, double offsetX, double offsetY) {
            int index = (int)(LatticeHash.Hash(seed, cellX, cellY) >> 60);
            return Gradient2X[index] * offsetX + Gradient2Y[index] * offsetY;
        }

        private static double Dot3(ulong seed, long cellX, long cellY, long cellZ, double offsetX, double offsetY, double offsetZ) {
            int index = (int)((LatticeHash.Hash(seed, cellX, cellY, cellZ) >> 32) % 12UL);
            return Gradient3X[index] * offsetX + Gradient3Y[index] * offsetY + Gradient3Z[index] * offsetZ;
        }

        // Bilinear blend of the four corners of one z-slice of the cell.
        private static double Layer3(ulong seed, long cellX, long cellY, long cellZ, double fracX, double fracY, double offsetZ, double fadeX, double fadeY) {
            double corner00 = Dot3(seed, cellX, cellY, cellZ, fracX, fracY, offsetZ);
            double corner10 = Dot3(seed, cellX + 1, cellY, cellZ, fracX - 1.0, fracY, offsetZ);
            double corner01 = Dot3(seed, cellX, cellY + 1, cellZ, fracX, fracY - 1.0, offsetZ);
            double corner11 = Dot3(seed, cellX + 1, cellY + 1, cellZ, fracX - 1.0, fracY - 1.0, offsetZ);
            double bottom = Lerp(corner00, corner10, fadeX);
            double top = Lerp(corner01, corner11, fadeX);
            return Lerp(bottom, top, fadeY);
        }

        private static double Lerp(double from, double to, double t) {
            return from + (to - from) * t;
        }

        private static double Clamp(double value) {
            if (value < -1.0) {
                return -1.0;
            }
            return value > 1.0 ? 1.0 : value;
        }
    }
}
