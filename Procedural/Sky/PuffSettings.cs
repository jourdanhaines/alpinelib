using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of emitted smoke puffs (a locomotive's stack): pool size, lifetime, how a puff grows, rises and
    /// shrinks away, and how a burst of effort pushes it.
    /// </summary>
    /// <remarks>Sizes are puff diameters in metres, speeds metres per second.</remarks>
    public sealed class PuffSettings {
        /// <summary>Distinct puff meshes an emitter builds and picks from.</summary>
        public const int VariantCount = 6;

        /// <summary>Variants painted from the light strip (the rest from the dark one).</summary>
        public const int LightVariantCount = 3;

        /// <summary>Most puffs alive at once; a puff emitted into a full pool is dropped.</summary>
        public int Capacity { get; set; } = 64;

        /// <summary>Seconds a puff lives.</summary>
        public double Lifetime { get; set; } = 3.5;

        public double StartSize { get; set; } = 0.6;
        public double EndSize { get; set; } = 2.4;

        /// <summary>Upward speed when emitted and at the end of life.</summary>
        public double StartRise { get; set; } = 2.5;
        public double EndRise { get; set; } = 0.6;

        /// <summary>Last share of the life over which a puff shrinks to nothing.</summary>
        public double ShrinkShare { get; set; } = 0.25;

        /// <summary>Most sideways speed a puff wanders with, so a column spreads instead of stacking in a line.</summary>
        public double Spread { get; set; } = 0.35;

        /// <summary>Extra upward speed at full effort, fading over the first part of the life.</summary>
        public double BurstRise { get; set; } = 2.0;

        /// <summary>Share a puff's start size grows by at full effort.</summary>
        public double BurstGrowth { get; set; } = 0.25;

        /// <summary>Chance an idle puff is painted dark; full effort adds <see cref="EffortDarkShare"/>.</summary>
        public double IdleDarkShare { get; set; } = 0.1;
        public double EffortDarkShare { get; set; } = 0.25;

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (Capacity < 1 || Capacity > 1023) throw new ArgumentOutOfRangeException(nameof(Capacity), Capacity, "Must be 1..1023 (one instanced draw).");
            if (!(Lifetime > 0.0)) throw new ArgumentOutOfRangeException(nameof(Lifetime), Lifetime, "Must be positive.");
            if (!(StartSize > 0.0) || !(EndSize >= StartSize)) throw new ArgumentOutOfRangeException(nameof(EndSize), EndSize, "Sizes must be positive and grow.");
            if (!(StartRise >= 0.0) || !(EndRise >= 0.0)) throw new ArgumentOutOfRangeException(nameof(StartRise), StartRise, "Rise speeds must not be negative.");
            if (!(ShrinkShare > 0.0) || ShrinkShare > 1.0) throw new ArgumentOutOfRangeException(nameof(ShrinkShare), ShrinkShare, "Must be in (0, 1].");
            if (!(Spread >= 0.0) || !(BurstRise >= 0.0) || !(BurstGrowth >= 0.0)) throw new ArgumentOutOfRangeException(nameof(Spread), Spread, "Spread and burst must not be negative.");
            if (!(IdleDarkShare >= 0.0) || !(EffortDarkShare >= 0.0) || IdleDarkShare + EffortDarkShare > 1.0) {
                throw new ArgumentOutOfRangeException(nameof(IdleDarkShare), IdleDarkShare, "Dark shares must be in [0, 1] together.");
            }
        }
    }
}
