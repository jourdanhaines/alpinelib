namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Seeded hashes of integer lattice points, identical on every runtime: the random source the
    /// noise functions read their gradients from.
    /// </summary>
    /// <remarks>
    /// Pure functions of their arguments (no state), so they are thread-safe and allocation-free.
    /// Coordinates are folded in one at a time through <see cref="StableHash.Mix"/>, so swapping
    /// axes or dimensions gives unrelated values.
    /// </remarks>
    public static class LatticeHash {
        private const double DoubleUnit = 1.0 / 9007199254740992.0;

        /// <summary>The 64-bit hash of the 1D lattice point <paramref name="ix"/>.</summary>
        public static ulong Hash(ulong seed, long ix) {
            return StableHash.Mix(seed, (ulong)ix);
        }

        /// <summary>The 64-bit hash of the 2D lattice point (<paramref name="ix"/>, <paramref name="iy"/>).</summary>
        public static ulong Hash(ulong seed, long ix, long iy) {
            return StableHash.Mix(StableHash.Mix(seed, (ulong)ix), (ulong)iy);
        }

        /// <summary>The 64-bit hash of the 3D lattice point (<paramref name="ix"/>, <paramref name="iy"/>, <paramref name="iz"/>).</summary>
        public static ulong Hash(ulong seed, long ix, long iy, long iz) {
            return StableHash.Mix(StableHash.Mix(StableHash.Mix(seed, (ulong)ix), (ulong)iy), (ulong)iz);
        }

        /// <summary>A uniform double in [0, 1) for the 1D lattice point.</summary>
        public static double Unit(ulong seed, long ix) {
            return ToUnit(Hash(seed, ix));
        }

        /// <summary>A uniform double in [0, 1) for the 2D lattice point.</summary>
        public static double Unit(ulong seed, long ix, long iy) {
            return ToUnit(Hash(seed, ix, iy));
        }

        /// <summary>A uniform double in [0, 1) for the 3D lattice point.</summary>
        public static double Unit(ulong seed, long ix, long iy, long iz) {
            return ToUnit(Hash(seed, ix, iy, iz));
        }

        /// <summary>The top 53 bits of <paramref name="hash"/> as a uniform double in [0, 1).</summary>
        public static double ToUnit(ulong hash) {
            return (hash >> 11) * DoubleUnit;
        }
    }
}
