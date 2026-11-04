using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of wind-rolled props (tumbleweeds): how long a roll runs, how it hops and wobbles, how long a prop rests
    /// at the end before it reappears at the start, and how fast it rolls for the wind's drift.
    /// </summary>
    /// <remarks>Distances are ground metres along the path unless named otherwise.</remarks>
    public sealed class RollerPathSettings {
        public double MinLength { get; set; } = 30.0;
        public double MaxLength { get; set; } = 90.0;

        /// <summary>Shortest clipped roll kept; a slot hemmed in tighter gets no roller.</summary>
        public double MinClippedLength { get; set; } = 8.0;

        /// <summary>Most the start is pulled upwind of its slot (still clear), so props tend to reappear at the spawn area's upwind edge.</summary>
        public double UpwindReach { get; set; } = 24.0;

        /// <summary>Most a path's direction strays from the prevailing wind, degrees.</summary>
        public double DirectionJitterDegrees { get; set; } = 10.0;

        public double MinHopSpacing { get; set; } = 6.0;
        public double MaxHopSpacing { get; set; } = 14.0;
        public double MinHopHeight { get; set; } = 0.2;
        public double MaxHopHeight { get; set; } = 0.6;

        /// <summary>Most the roll strays sideways from its straight line, metres.</summary>
        public double WobbleAmplitude { get; set; } = 1.2;

        /// <summary>Wavelength of the sideways wobble along the path, metres.</summary>
        public double WobblePeriod { get; set; } = 24.0;

        /// <summary>Range of the rest at the end of a roll, in cycle metres (ground metres of drift spent settled).</summary>
        public double MinRestGap { get; set; } = 20.0;
        public double MaxRestGap { get; set; } = 60.0;

        /// <summary>Share of the wind's drift along the path a prop rolls.</summary>
        public double GroundSpeedShare { get; set; } = 0.6;

        /// <summary>Cycle metres a prop takes to scale in at the start and out at the end of its rest (about a second of the base wind).</summary>
        public double ScaleDistance { get; set; } = 1.8;

        /// <summary>Most a resting prop rocks back and forth about its roll axis, radians.</summary>
        public double RockAngle { get; set; } = 0.08;

        /// <summary>Cycle metres of one rock back and forth while resting.</summary>
        public double RockPeriod { get; set; } = 6.0;

        /// <summary>Step the ground profile is sampled at, metres: fine enough to ride over rails.</summary>
        public double ProfileStep { get; set; } = 0.25;

        /// <summary>Step the path is clipped against the caller's clear test at, metres.</summary>
        public double ClipStep { get; set; } = 0.25;

        /// <summary>The shipped rollers.</summary>
        public static RollerPathSettings Default => new RollerPathSettings();

        /// <summary>Throws when a range is inverted or a length, step or share is not positive.</summary>
        public void Validate() {
            RequireRange(MinLength, MaxLength, nameof(MinLength));
            RequireRange(MinHopSpacing, MaxHopSpacing, nameof(MinHopSpacing));
            RequireRange(MinHopHeight, MaxHopHeight, nameof(MinHopHeight));
            RequireRange(MinRestGap, MaxRestGap, nameof(MinRestGap));
            RequirePositive(MinLength, nameof(MinLength));
            RequirePositive(MinHopSpacing, nameof(MinHopSpacing));
            RequirePositive(ProfileStep, nameof(ProfileStep));
            RequirePositive(ClipStep, nameof(ClipStep));
            RequirePositive(GroundSpeedShare, nameof(GroundSpeedShare));
            RequirePositive(ScaleDistance, nameof(ScaleDistance));
            RequirePositive(RockPeriod, nameof(RockPeriod));
            RequirePositive(WobblePeriod, nameof(WobblePeriod));
            if (MinRestGap < 2.0 * ScaleDistance) throw new ArgumentException($"{nameof(MinRestGap)} must leave room to scale out ({2.0 * ScaleDistance}).");
            if (MinClippedLength < ScaleDistance) throw new ArgumentException($"{nameof(MinClippedLength)} must be at least {nameof(ScaleDistance)}.");
            if (WobbleAmplitude < 0.0 || MinHopHeight < 0.0 || UpwindReach < 0.0 || RockAngle < 0.0) throw new ArgumentException("Wobble, hop height, upwind reach and rock angle must not be negative.");
            if (DirectionJitterDegrees < 0.0 || DirectionJitterDegrees >= 80.0) throw new ArgumentException($"{nameof(DirectionJitterDegrees)} must be in [0, 80).");
        }

        private static void RequireRange(double min, double max, string name) {
            if (!(min <= max)) throw new ArgumentException($"{name} must not exceed its maximum ({min} > {max}).");
        }

        private static void RequirePositive(double value, string name) {
            if (!(value > 0.0)) throw new ArgumentException($"{name} must be positive ({value}).");
        }
    }
}
