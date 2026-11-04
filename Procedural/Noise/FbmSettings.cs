using System;

namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// Fractal Brownian motion parameters: how many octaves of <see cref="GradientNoise"/> are summed,
    /// the first octave's frequency, and how frequency and amplitude change per octave.
    /// </summary>
    /// <remarks>
    /// <c>default(FbmSettings)</c> has zero octaves, which <see cref="Fbm"/> samples as a flat 0.
    /// </remarks>
    public readonly struct FbmSettings : IEquatable<FbmSettings> {
        /// <summary>The most octaves a settings value may ask for.</summary>
        public const int MaxOctaves = 16;

        /// <summary>
        /// Settings for <paramref name="octaves"/> octaves starting at <paramref name="frequency"/>
        /// cycles per unit, each octave <paramref name="lacunarity"/> times the frequency and
        /// <paramref name="gain"/> times the amplitude of the one before.
        /// </summary>
        public FbmSettings(int octaves, double frequency, double lacunarity = 2.0, double gain = 0.5) {
            if (octaves < 1 || octaves > MaxOctaves) {
                throw new ArgumentOutOfRangeException(nameof(octaves), octaves, $"Must be in [1, {MaxOctaves}].");
            }
            if (!IsPositiveFinite(frequency)) {
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Must be positive and finite.");
            }
            if (!IsPositiveFinite(lacunarity)) {
                throw new ArgumentOutOfRangeException(nameof(lacunarity), lacunarity, "Must be positive and finite.");
            }
            if (!IsPositiveFinite(gain)) {
                throw new ArgumentOutOfRangeException(nameof(gain), gain, "Must be positive and finite.");
            }
            Octaves = octaves;
            Frequency = frequency;
            Lacunarity = lacunarity;
            Gain = gain;
        }

        /// <summary>Number of octaves summed, in [1, <see cref="MaxOctaves"/>] (0 only for <c>default</c>).</summary>
        public int Octaves { get; }

        /// <summary>Frequency of the first octave, in cycles per input unit.</summary>
        public double Frequency { get; }

        /// <summary>Frequency multiplier from one octave to the next.</summary>
        public double Lacunarity { get; }

        /// <summary>Amplitude multiplier from one octave to the next.</summary>
        public double Gain { get; }

        /// <inheritdoc />
        public bool Equals(FbmSettings other) {
            return Octaves == other.Octaves && Frequency.Equals(other.Frequency)
                && Lacunarity.Equals(other.Lacunarity) && Gain.Equals(other.Gain);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is FbmSettings other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                int hash = Octaves;
                hash = hash * 397 ^ Frequency.GetHashCode();
                hash = hash * 397 ^ Lacunarity.GetHashCode();
                return hash * 397 ^ Gain.GetHashCode();
            }
        }

        private static bool IsPositiveFinite(double value) {
            return value > 0.0 && !double.IsInfinity(value);
        }
    }
}
