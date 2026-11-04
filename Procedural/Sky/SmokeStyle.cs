using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How an emitter's smoke changes with its efficiency (0 = labouring, 1 = clean running): ordered
    /// <see cref="SmokeStage"/>s over a palette of atlas strips (<see cref="Shades"/>), cross-faded by probability so the
    /// colour mix moves smoothly (no hard switch), plus the time constants the efficiency is smoothed with.
    /// </summary>
    /// <remarks>
    /// Between stage a (pure at e_a) and stage b (pure at e_b) a puff comes from b with probability
    /// (e − e_a) / (e_b − e_a); thickness, life and rise blend linearly. <see cref="Sample"/> is a pure function of the
    /// efficiency and the seed. Every puff is one flat colour: its shade's strip at <see cref="Tone"/>, stepped by
    /// <see cref="ToneJitter"/> per shape (<see cref="PuffShape.ToneOf"/>).
    /// </remarks>
    public sealed class SmokeStyle {
        /// <summary>Most shades a style may hold (the emitter builds a mesh per shape and shade).</summary>
        public const int MaxShades = 5;

        public const double DefaultRiseSeconds = 2.0;
        public const double DefaultFallSeconds = 3.0;

        /// <summary>Default strip point puffs are painted at (mid-strip).</summary>
        public const double DefaultTone = 0.5;

        /// <summary>Default and largest tone step between shapes of one shade.</summary>
        public const double DefaultToneJitter = 0.1;
        public const double MaxToneJitter = 0.15;

        private const long StageKey = 11;
        private const long ShadeKey = 12;

        public SmokeStyle(IReadOnlyList<SkyAtlasBand> shades, IReadOnlyList<SmokeStage> stages,
            double riseSeconds = DefaultRiseSeconds, double fallSeconds = DefaultFallSeconds,
            double tone = DefaultTone, double toneJitter = DefaultToneJitter) {
            Shades = shades ?? throw new ArgumentNullException(nameof(shades));
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            RiseSeconds = riseSeconds;
            FallSeconds = fallSeconds;
            Tone = tone;
            ToneJitter = toneJitter;
            Validate();
        }

        /// <summary>The atlas strips puffs are painted from.</summary>
        public IReadOnlyList<SkyAtlasBand> Shades { get; }

        /// <summary>Stages by rising <see cref="SmokeStage.Efficiency"/>.</summary>
        public IReadOnlyList<SmokeStage> Stages { get; }

        /// <summary>Time constants (seconds) the efficiency follows its target with, going up and going down; 0 = no smoothing.</summary>
        public double RiseSeconds { get; }
        public double FallSeconds { get; }

        /// <summary>Strip point (0 light .. 1 dark) every puff is painted at, before its shape's tone step.</summary>
        public double Tone { get; }

        /// <summary>Tone step between shapes of one shade (0..<see cref="MaxToneJitter"/>).</summary>
        public double ToneJitter { get; }

        /// <summary>The longest life any stage gives a puff (for pool budgets).</summary>
        public double MaxLifeScale {
            get {
                double most = 0.0;
                foreach (SmokeStage stage in Stages) most = Math.Max(most, stage.LifeScale);
                return most;
            }
        }

        /// <summary>One shade, one stage, no scaling: plain smoke from <paramref name="band"/>.</summary>
        public static SmokeStyle Single(SkyAtlasBand band) {
            return new SmokeStyle(new[] { band }, new[] { new SmokeStage(0.0, new[] { new SmokeShadeWeight(0, 1.0) }) });
        }

        /// <summary>
        /// The two stages either side of <paramref name="efficiency"/> and the share of the upper one (0..1); below the
        /// first or above the last stage both are that stage.
        /// </summary>
        public void Blend(double efficiency, out int lower, out int upper, out double share) {
            double clamped = Math.Min(1.0, Math.Max(0.0, efficiency));
            int last = Stages.Count - 1;
            if (clamped <= Stages[0].Efficiency) {
                lower = upper = 0;
                share = 0.0;
                return;
            }
            if (clamped >= Stages[last].Efficiency) {
                lower = upper = last;
                share = 0.0;
                return;
            }

            upper = 1;
            while (Stages[upper].Efficiency < clamped) upper++;
            lower = upper - 1;
            share = (clamped - Stages[lower].Efficiency) / (Stages[upper].Efficiency - Stages[lower].Efficiency);
        }

        /// <summary>Probability a puff emitted at <paramref name="efficiency"/> gets shade <paramref name="shade"/>.</summary>
        public double ShadeShare(double efficiency, int shade) {
            Blend(efficiency, out int lower, out int upper, out double share);
            return (1.0 - share) * Stages[lower].ShareOf(shade) + share * Stages[upper].ShareOf(shade);
        }

        /// <summary>The look of a puff emitted at <paramref name="efficiency"/>, keyed by <paramref name="seed"/>.</summary>
        public SmokeSample Sample(double efficiency, ulong seed) {
            Blend(efficiency, out int lower, out int upper, out double share);
            SmokeStage from = Stages[lower];
            SmokeStage to = Stages[upper];
            SmokeStage drawn = LatticeHash.Unit(seed, StageKey) < share ? to : from;
            int shade = drawn.Pick(LatticeHash.Unit(seed, ShadeKey));
            return new SmokeSample(shade, Lerp(from.Thickness, to.Thickness, share), Lerp(from.LifeScale, to.LifeScale, share),
                Lerp(from.RiseScale, to.RiseScale, share));
        }

        /// <summary>Strip point the puff mesh of shape <paramref name="variant"/> is painted at.</summary>
        public double ToneOf(int variant) {
            return PuffShape.ToneOf(variant, Tone, ToneJitter);
        }

        /// <summary><paramref name="current"/> moved toward <paramref name="target"/> over <paramref name="seconds"/> by the rise or fall time constant.</summary>
        public double Smooth(double current, double target, double seconds) {
            double constant = target >= current ? RiseSeconds : FallSeconds;
            if (!(constant > 0.0)) return target;
            if (!(seconds > 0.0)) return current;

            return target + (current - target) * Math.Exp(-seconds / constant);
        }

        /// <summary>Throws when the style is out of range.</summary>
        public void Validate() {
            if (Shades.Count < 1 || Shades.Count > MaxShades) throw new ArgumentOutOfRangeException(nameof(Shades), Shades.Count, $"Must hold 1..{MaxShades} shades.");
            if (Stages.Count < 1) throw new ArgumentOutOfRangeException(nameof(Stages), Stages.Count, "Must hold a stage.");
            if (!(RiseSeconds >= 0.0) || !(FallSeconds >= 0.0)) throw new ArgumentOutOfRangeException(nameof(RiseSeconds), RiseSeconds, "Time constants must not be negative.");
            if (!(Tone >= 0.0 && Tone <= 1.0)) throw new ArgumentOutOfRangeException(nameof(Tone), Tone, "Must lie within the strip, 0..1.");
            if (!(ToneJitter >= 0.0 && ToneJitter <= MaxToneJitter)) throw new ArgumentOutOfRangeException(nameof(ToneJitter), ToneJitter, $"Must lie within 0..{MaxToneJitter}.");
            double previous = double.NegativeInfinity;
            foreach (SmokeStage stage in Stages) {
                ValidateStage(stage, previous);
                previous = stage.Efficiency;
            }
        }

        private void ValidateStage(SmokeStage stage, double previous) {
            if (stage == null) throw new ArgumentNullException(nameof(Stages), "A stage is null.");
            if (!(stage.Efficiency > previous) || stage.Efficiency < 0.0 || stage.Efficiency > 1.0) {
                throw new ArgumentOutOfRangeException(nameof(Stages), stage.Efficiency, "Stage efficiencies must rise within 0..1.");
            }
            if (!(stage.Thickness > 0.0) || !(stage.LifeScale > 0.0) || !(stage.RiseScale >= 0.0)) {
                throw new ArgumentOutOfRangeException(nameof(Stages), stage.Thickness, "Thickness and life must be positive, rise not negative.");
            }
            if (stage.Shades.Count < 1 || !(stage.TotalWeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(Stages), stage.Shades.Count, "A stage needs a weighted shade.");
            foreach (SmokeShadeWeight shade in stage.Shades) {
                if (shade.Shade < 0 || shade.Shade >= Shades.Count) throw new ArgumentOutOfRangeException(nameof(Stages), shade.Shade, "No such shade.");
                if (!(shade.Weight >= 0.0)) throw new ArgumentOutOfRangeException(nameof(Stages), shade.Weight, "Weights must not be negative.");
            }
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
