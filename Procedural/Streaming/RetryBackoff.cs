using System;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// How long to wait before retrying work that failed: <see cref="FirstSeconds"/> after the first failure, doubling
    /// with each further one up to <see cref="MaxSeconds"/>.
    /// </summary>
    public readonly struct RetryBackoff {
        public RetryBackoff(double firstSeconds, double maxSeconds) {
            if (!(firstSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(firstSeconds), firstSeconds, "First delay must be positive.");
            if (!(maxSeconds >= firstSeconds)) throw new ArgumentOutOfRangeException(nameof(maxSeconds), maxSeconds, "Longest delay must be at least the first.");

            FirstSeconds = firstSeconds;
            MaxSeconds = maxSeconds;
        }

        public double FirstSeconds { get; }
        public double MaxSeconds { get; }

        /// <summary>Seconds to wait after <paramref name="failures"/> failures in a row (0 when none).</summary>
        public double DelayAfter(int failures) {
            if (failures <= 0) return 0.0;

            double delay = FirstSeconds * Math.Pow(2.0, Math.Min(failures - 1, 30));
            return Math.Min(delay, MaxSeconds);
        }
    }
}
