using System;

namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Fractal Brownian motion: octaves of <see cref="GradientNoise"/> summed and normalised to [−1, 1].
    /// </summary>
    /// <remarks>
    /// Octave <c>n</c> samples with its own seed (<see cref="OctaveSeed"/>) at a point shifted by its own
    /// non-integer offset (<see cref="OctaveOffset"/>), so lattice zeros never line up across octaves and
    /// the sum is not pinned to 0 on a grid of 1/frequency. The sum is divided by the total amplitude, so
    /// the result stays in [−1, 1] for any gain. Same determinism guarantees as <see cref="GradientNoise"/>.
    /// </remarks>
    public static class Fbm {
        private const ulong OctaveSalt = 0x4F43544156UL;
        private const ulong OffsetXSalt = 0x58UL;
        private const ulong OffsetYSalt = 0x59UL;
        private const double OffsetRange = 1024.0;

        /// <summary>1D fBm at <paramref name="x"/>, in [−1, 1].</summary>
        public static double Sample1(ulong seed, double x, in FbmSettings settings) {
            RequireOctaves(settings);
            double sum = 0.0;
            double totalAmplitude = 0.0;
            double amplitude = 1.0;
            double frequency = settings.Frequency;
            for (int octave = 0; octave < settings.Octaves; octave++) {
                ulong octaveSeed = OctaveSeed(seed, octave);
                double sampleX = x * frequency + OffsetOf(octaveSeed, OffsetXSalt);
                sum += amplitude * GradientNoise.Sample1(octaveSeed, sampleX);
                totalAmplitude += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }
            return sum / totalAmplitude;
        }

        /// <summary>2D fBm at (<paramref name="x"/>, <paramref name="y"/>), in [−1, 1].</summary>
        public static double Sample2(ulong seed, double x, double y, in FbmSettings settings) {
            RequireOctaves(settings);
            double sum = 0.0;
            double totalAmplitude = 0.0;
            double amplitude = 1.0;
            double frequency = settings.Frequency;
            for (int octave = 0; octave < settings.Octaves; octave++) {
                ulong octaveSeed = OctaveSeed(seed, octave);
                double sampleX = x * frequency + OffsetOf(octaveSeed, OffsetXSalt);
                double sampleY = y * frequency + OffsetOf(octaveSeed, OffsetYSalt);
                sum += amplitude * GradientNoise.Sample2(octaveSeed, sampleX, sampleY);
                totalAmplitude += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }
            return sum / totalAmplitude;
        }

        /// <summary>The seed octave <paramref name="octave"/> samples with: <c>Mix(Mix(seed, salt), octave)</c>.</summary>
        public static ulong OctaveSeed(ulong seed, int octave) {
            return StableHash.Mix(StableHash.Mix(seed, OctaveSalt), (ulong)octave);
        }

        /// <summary>The shift in [0, 1024) added to axis <paramref name="axis"/> (0 = x, 1 = y) of octave <paramref name="octave"/>, after scaling by its frequency.</summary>
        public static double OctaveOffset(ulong seed, int octave, int axis) {
            if (axis < 0 || axis > 1) {
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Must be 0 (x) or 1 (y).");
            }
            return OffsetOf(OctaveSeed(seed, octave), axis == 0 ? OffsetXSalt : OffsetYSalt);
        }

        private static double OffsetOf(ulong octaveSeed, ulong axisSalt) {
            return LatticeHash.ToUnit(StableHash.Mix(octaveSeed, axisSalt)) * OffsetRange;
        }

        private static void RequireOctaves(in FbmSettings settings) {
            if (settings.Octaves < 1) {
                throw new ArgumentException("FbmSettings has no octaves; construct it with the FbmSettings constructor.", nameof(settings));
            }
        }
    }
}
