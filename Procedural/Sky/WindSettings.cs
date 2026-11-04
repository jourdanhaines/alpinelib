using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of a <see cref="WindField"/>: a base speed, a prevailing heading drawn from the seed that
    /// wanders slowly, and speed gusts from two noise periods.
    /// </summary>
    public readonly struct WindSettings : IEquatable<WindSettings> {
        /// <summary>
        /// Settings with <paramref name="baseSpeed"/> m/s, a heading wandering up to <paramref name="headingWanderDegrees"/>
        /// either side of the prevailing one over about <paramref name="headingPeriodSeconds"/>, and gusts of up to
        /// <paramref name="gustShare"/> of the base speed from noise periods <paramref name="gustPeriodSeconds"/> and
        /// <paramref name="gustDetailPeriodSeconds"/>.
        /// </summary>
        public WindSettings(double baseSpeed, double headingWanderDegrees, double headingPeriodSeconds, double gustShare,
            double gustPeriodSeconds, double gustDetailPeriodSeconds) {
            RequireNonNegative(baseSpeed, nameof(baseSpeed));
            RequireNonNegative(headingWanderDegrees, nameof(headingWanderDegrees));
            RequirePositive(headingPeriodSeconds, nameof(headingPeriodSeconds));
            if (!(gustShare >= 0.0 && gustShare < 1.0)) throw new ArgumentOutOfRangeException(nameof(gustShare), gustShare, "Must be in [0, 1).");
            RequirePositive(gustPeriodSeconds, nameof(gustPeriodSeconds));
            RequirePositive(gustDetailPeriodSeconds, nameof(gustDetailPeriodSeconds));
            BaseSpeed = baseSpeed;
            HeadingWanderDegrees = headingWanderDegrees;
            HeadingPeriodSeconds = headingPeriodSeconds;
            GustShare = gustShare;
            GustPeriodSeconds = gustPeriodSeconds;
            GustDetailPeriodSeconds = gustDetailPeriodSeconds;
        }

        /// <summary>3 m/s, ±35° over ~600 s, ±40 % gusts over ~45 s and ~8 s.</summary>
        public static WindSettings Default => new WindSettings(3.0, 35.0, 600.0, 0.4, 45.0, 8.0);

        /// <summary>Mean wind speed, m/s.</summary>
        public double BaseSpeed { get; }

        /// <summary>Most the heading strays from the prevailing heading, degrees.</summary>
        public double HeadingWanderDegrees { get; }

        /// <summary>Noise period of the heading wander, seconds.</summary>
        public double HeadingPeriodSeconds { get; }

        /// <summary>Most a gust adds to or takes from the base speed, as a share of it.</summary>
        public double GustShare { get; }

        /// <summary>Noise period of the main gusts, seconds.</summary>
        public double GustPeriodSeconds { get; }

        /// <summary>Noise period of the gust detail, seconds.</summary>
        public double GustDetailPeriodSeconds { get; }

        /// <summary>Slowest the wind ever blows, m/s.</summary>
        public double MinSpeed => BaseSpeed * (1.0 - GustShare);

        /// <summary>Fastest the wind ever blows, m/s.</summary>
        public double MaxSpeed => BaseSpeed * (1.0 + GustShare);

        public bool Equals(WindSettings other) {
            return BaseSpeed.Equals(other.BaseSpeed) && HeadingWanderDegrees.Equals(other.HeadingWanderDegrees)
                && HeadingPeriodSeconds.Equals(other.HeadingPeriodSeconds) && GustShare.Equals(other.GustShare)
                && GustPeriodSeconds.Equals(other.GustPeriodSeconds) && GustDetailPeriodSeconds.Equals(other.GustDetailPeriodSeconds);
        }

        public override bool Equals(object obj) {
            return obj is WindSettings other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(BaseSpeed, HeadingWanderDegrees, HeadingPeriodSeconds, GustShare, GustPeriodSeconds, GustDetailPeriodSeconds);
        }

        private static void RequirePositive(double value, string name) {
            if (!(value > 0.0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be positive and finite.");
        }

        private static void RequireNonNegative(double value, string name) {
            if (!(value >= 0.0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be non-negative and finite.");
        }
    }
}
