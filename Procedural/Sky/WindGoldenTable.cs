using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The determinism contract for <see cref="WindField"/>: the exact drift of one seed after an hour and a
    /// quarter second, checked by the .NET tests and by the Unity editor gate.
    /// </summary>
    public static class WindGoldenTable {
        public const ulong Seed = 42UL;
        public const double Time = 3600.25;

        /// <summary>Bits of the recorded drift x and z under <see cref="WindSettings.Default"/>.</summary>
        public const long ExpectedXBits = 4664431482819695031L;
        public const long ExpectedZBits = 4665646490911138918L;

        /// <summary>One line per component that differs from the recording; empty when the drift matches.</summary>
        public static List<string> FindMismatches() {
            var mismatches = new List<string>();
            WindVector drift = new WindField(Seed, WindSettings.Default).Displacement(Time);
            AddIfDifferent(mismatches, "x", ExpectedXBits, BitConverter.DoubleToInt64Bits(drift.X));
            AddIfDifferent(mismatches, "z", ExpectedZBits, BitConverter.DoubleToInt64Bits(drift.Z));
            return mismatches;
        }

        private static void AddIfDifferent(List<string> mismatches, string axis, long expected, long actual) {
            if (expected == actual) return;

            mismatches.Add($"WindField drift {axis}: expected {BitConverter.Int64BitsToDouble(expected):R}, got {BitConverter.Int64BitsToDouble(actual):R}");
        }
    }
}
