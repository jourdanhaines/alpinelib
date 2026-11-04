namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Fractal Brownian motion: octaves of <see cref="GradientNoise"/> summed and normalised to [−1, 1].
    /// </summary>
    /// <remarks>
    /// Octave <c>n</c> samples with seed <c>StableHash.Mix(seed, n)</c>, so octaves are uncorrelated and
    /// never line up on the lattice origin. The sum is divided by the total amplitude, so the result stays
    /// in [−1, 1] for any gain. Same determinism guarantees as <see cref="GradientNoise"/>.
    /// </remarks>
    public static class Fbm {
        /// <summary>1D fBm at <paramref name="x"/>, in [−1, 1]; 0 for zero-octave settings.</summary>
        public static double Sample1(ulong seed, double x, in FbmSettings settings) {
            double sum = 0.0;
            double totalAmplitude = 0.0;
            double amplitude = 1.0;
            double frequency = settings.Frequency;
            for (int octave = 0; octave < settings.Octaves; octave++) {
                sum += amplitude * GradientNoise.Sample1(OctaveSeed(seed, octave), x * frequency);
                totalAmplitude += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }
            return Normalise(sum, totalAmplitude);
        }

        /// <summary>2D fBm at (<paramref name="x"/>, <paramref name="y"/>), in [−1, 1]; 0 for zero-octave settings.</summary>
        public static double Sample2(ulong seed, double x, double y, in FbmSettings settings) {
            double sum = 0.0;
            double totalAmplitude = 0.0;
            double amplitude = 1.0;
            double frequency = settings.Frequency;
            for (int octave = 0; octave < settings.Octaves; octave++) {
                sum += amplitude * GradientNoise.Sample2(OctaveSeed(seed, octave), x * frequency, y * frequency);
                totalAmplitude += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }
            return Normalise(sum, totalAmplitude);
        }

        /// <summary>The seed octave <paramref name="octave"/> samples with.</summary>
        public static ulong OctaveSeed(ulong seed, int octave) {
            return StableHash.Mix(seed, (ulong)octave);
        }

        private static double Normalise(double sum, double totalAmplitude) {
            if (totalAmplitude <= 0.0) {
                return 0.0;
            }
            return sum / totalAmplitude;
        }
    }
}
