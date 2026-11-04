using System;
using System.Numerics;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A pooled stream of smoke puffs relative to their emitter: emitted at a rate in bursts, each rising, growing and
    /// carried by the air relative to the emitter until it shrinks away at the end of its life.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Puffs live in the emitter's frame with world axes: each step moves them by their rise, their drift and the air
    /// relative to the emitter (wind minus the emitter's own velocity), so in the world they ride the wind while the
    /// emitter moves away from them. A floating-origin shift never touches them.
    /// </para>
    /// <para>
    /// Deterministic: puff n's look and wander come from (seed, n) and the cadence's phase from the seed, so the same
    /// seed fed the same steps gives the same puffs bit for bit. Smoke is cosmetic and not replicated: a joiner's
    /// emitter starts its count at 0 with its own steps, so it shows similar smoke, not the same puffs.
    /// </para>
    /// <para>
    /// Each puff's shade, thickness, life and rise come from the <see cref="Style"/> at the smoothed
    /// <see cref="Efficiency"/> when it is emitted, so the mix of colours follows the target without popping. A burst
    /// is styled once: its mates share shade, thickness and tone step (<see cref="MateVariant"/>), differing in shape,
    /// heading, spin and drift only.
    /// </para>
    /// <para>A burst emitted into a full pool loses what does not fit (<see cref="Dropped"/>); live puffs never pop.</para>
    /// </remarks>
    public sealed class PuffSimulation {
        private const long PhaseKey = -1;
        private const long VariantKey = 0;
        private const ulong LookSalt = 0x4C4F4F4BUL;
        private const long YawKey = 1;
        private const long SpinKey = 2;
        private const long DriftAngleKey = 3;
        private const long DriftSpeedKey = 4;
        private const double MaxSpin = 0.6;
        private const double BurstFadeShare = 0.35;

        private readonly Puff[] _puffs;
        private readonly ulong _seed;
        private double _cadence;
        private ulong _emitted;
        private double _targetEfficiency;

        /// <summary>A pool of <paramref name="settings"/> keyed by <paramref name="seed"/>, painted by <paramref name="style"/> (one plain shade when null).</summary>
        public PuffSimulation(PuffSettings settings, ulong seed, SmokeStyle style = null) {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            Style = style ?? SmokeStyle.Single(default);
            _seed = seed;
            _puffs = new Puff[settings.Capacity];
            _cadence = LatticeHash.Unit(seed, PhaseKey);
        }

        public PuffSettings Settings { get; }

        public SmokeStyle Style { get; }

        /// <summary>The efficiency (0..1) <see cref="Efficiency"/> heads for, by the style's time constants.</summary>
        public double TargetEfficiency {
            get => _targetEfficiency;
            set => _targetEfficiency = Clamp01(value);
        }

        /// <summary>The smoothed efficiency new puffs are styled at.</summary>
        public double Efficiency { get; private set; }

        /// <summary>Jumps <see cref="Efficiency"/> and its target to <paramref name="efficiency"/> (a fresh start, no fade).</summary>
        public void ResetEfficiency(double efficiency) {
            _targetEfficiency = Clamp01(efficiency);
            Efficiency = _targetEfficiency;
        }

        /// <summary>Puffs alive now.</summary>
        public int Count { get; private set; }

        /// <summary>Puffs emitted so far, dropped ones included.</summary>
        public ulong Emitted => _emitted;

        /// <summary>Puffs that found the pool full.</summary>
        public ulong Dropped { get; private set; }

        /// <summary>Live puff <paramref name="index"/> (0..<see cref="Count"/> − 1; order changes as puffs die).</summary>
        public Puff this[int index] {
            get {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index), index, "No such live puff.");

                return _puffs[index];
            }
        }

        /// <summary>Forgets every live puff (the count and cadence go on).</summary>
        public void Clear() {
            Count = 0;
        }

        /// <summary>Moves every live puff by <paramref name="offset"/> (a snap of the emitter the puffs must not follow).</summary>
        public void Translate(Vector3 offset) {
            for (int index = 0; index < Count; index++) _puffs[index] = _puffs[index].Moved(_puffs[index].Offset + offset, _puffs[index].Age);
        }

        /// <summary>
        /// Advances <paramref name="seconds"/>: live puffs move in <paramref name="relativeAir"/> (wind minus the
        /// emitter's velocity, m/s) and age, then <paramref name="rate"/> bursts per second of <paramref name="burst"/>
        /// puffs at <paramref name="effort"/> (0..1) are emitted, each already moved for the part of the step since it left.
        /// </summary>
        public void Step(double seconds, Vector3 relativeAir, double rate, int burst, double effort) {
            if (!(seconds > 0.0)) return;

            double clampedEffort = Math.Min(1.0, Math.Max(0.0, effort));
            Efficiency = Style.Smooth(Efficiency, _targetEfficiency, seconds);
            AdvanceLive(seconds, relativeAir);
            if (!(rate > 0.0) || burst < 1) return;

            _cadence += rate * seconds;
            while (_cadence >= 1.0) {
                _cadence -= 1.0;
                double late = Math.Min(seconds, _cadence / rate);
                EmitBurst(burst, late, relativeAir, (float)clampedEffort);
            }
        }

        /// <summary>Share of the life <paramref name="age"/> seconds is, clamped to 0..1.</summary>
        public double Progress(double age) {
            return Math.Min(1.0, Math.Max(0.0, age / Settings.Lifetime));
        }

        /// <summary>Seconds <paramref name="puff"/> lives (the lifetime by its life scale).</summary>
        public double LifetimeOf(in Puff puff) {
            return Settings.Lifetime * puff.LifeScale;
        }

        /// <summary>Share of its own life <paramref name="puff"/> has lived, 0..1.</summary>
        public double ProgressOf(in Puff puff) {
            return Clamp01(puff.Age / LifetimeOf(puff));
        }

        /// <summary>Diameter of <paramref name="puff"/> now.</summary>
        public double SizeOf(in Puff puff) {
            return SizeAt(ProgressOf(puff), puff.Effort, puff.Thickness);
        }

        /// <summary>
        /// Diameter at life share <paramref name="progress"/> for <paramref name="effort"/> and <paramref name="thickness"/>
        /// (a multiplier): eases out from the start to the end size, shrinking to 0 over the last <see cref="PuffSettings.ShrinkShare"/>.
        /// </summary>
        public double SizeAt(double progress, double effort, double thickness = 1.0) {
            double share = Clamp01(progress);
            double start = Settings.StartSize * (1.0 + Settings.BurstGrowth * Clamp01(effort));
            double grown = start + (Settings.EndSize - start) * (1.0 - (1.0 - share) * (1.0 - share));
            return grown * ShrinkAt(share) * thickness;
        }

        /// <summary>1 until the last <see cref="PuffSettings.ShrinkShare"/> of the life, then smoothly down to 0 at its end.</summary>
        public double ShrinkAt(double progress) {
            double from = 1.0 - Settings.ShrinkShare;
            double share = Clamp01(progress);
            if (share <= from) return 1.0;

            double fall = Clamp01((share - from) / Settings.ShrinkShare);
            return 1.0 - fall * fall * (3.0 - 2.0 * fall);
        }

        /// <summary>Upward speed at life share <paramref name="progress"/>: from the start to the end rise, slowing early, plus the effort's burst fading over the first third.</summary>
        public double RiseAt(double progress, double effort) {
            double share = Clamp01(progress);
            double slowing = (1.0 - share) * (1.0 - share);
            double rise = Settings.EndRise + (Settings.StartRise - Settings.EndRise) * slowing;
            double burst = Math.Max(0.0, 1.0 - share / BurstFadeShare);
            return rise + Settings.BurstRise * Clamp01(effort) * burst * burst;
        }

        /// <summary>Heading of <paramref name="puff"/> now, radians.</summary>
        public static double YawOf(in Puff puff) {
            return puff.Yaw + puff.Spin * puff.Age;
        }

        private void AdvanceLive(double seconds, Vector3 relativeAir) {
            int index = 0;
            while (index < Count) {
                Puff moved = Move(_puffs[index], seconds, relativeAir);
                if (moved.Age >= LifetimeOf(moved)) {
                    Count--;
                    _puffs[index] = _puffs[Count];
                    continue;
                }

                _puffs[index] = moved;
                index++;
            }
        }

        // Midpoint rise over the step; drift and the relative air are constant across it.
        private Puff Move(in Puff puff, double seconds, Vector3 relativeAir) {
            double middle = Clamp01((puff.Age + seconds * 0.5) / LifetimeOf(puff));
            var rise = new Vector3(0f, (float)(RiseAt(middle, puff.Effort) * puff.RiseScale), 0f);
            Vector3 offset = puff.Offset + (rise + puff.Drift + relativeAir) * (float)seconds;
            return puff.Moved(offset, (float)(puff.Age + seconds));
        }

        // One look and tone step per burst, keyed by its first puff.
        private void EmitBurst(int burst, double late, Vector3 relativeAir, float effort) {
            ulong first = _emitted;
            SmokeSample look = Style.Sample(Efficiency, StableHash.Mix(StableHash.Mix(_seed, LookSalt), first));
            int firstVariant = ShapeFor(LatticeHash.Unit(_seed, unchecked((long)first), VariantKey), look.Thickness);
            for (int mate = 0; mate < burst; mate++) Emit(late, relativeAir, effort, look, MateVariant(firstVariant, mate));
        }

        private void Emit(double late, Vector3 relativeAir, float effort, in SmokeSample look, int variant) {
            ulong index = _emitted++;
            if (Count >= _puffs.Length) {
                Dropped++;
                return;
            }

            long key = unchecked((long)index);
            double yaw = LatticeHash.Unit(_seed, key, YawKey) * 2.0 * Math.PI;
            double spin = (LatticeHash.Unit(_seed, key, SpinKey) * 2.0 - 1.0) * MaxSpin;
            double driftAngle = LatticeHash.Unit(_seed, key, DriftAngleKey) * 2.0 * Math.PI;
            double driftSpeed = Settings.Spread * Math.Sqrt(LatticeHash.Unit(_seed, key, DriftSpeedKey));
            var drift = new Vector3((float)(Math.Cos(driftAngle) * driftSpeed), 0f, (float)(Math.Sin(driftAngle) * driftSpeed));
            var born = new Puff(Vector3.Zero, 0f, variant, (float)yaw, (float)spin, drift, effort, index, look);
            Puff moved = late > 0.0 ? Move(born, late, relativeAir) : born;
            if (moved.Age >= LifetimeOf(moved)) return;

            _puffs[Count++] = moved;
        }

        /// <summary>
        /// Shape of burst mate <paramref name="mate"/> after a first of <paramref name="firstVariant"/>: the shapes of the
        /// same tone step in turn (the variant count is a multiple of <see cref="PuffShape.ToneSteps"/>).
        /// </summary>
        public static int MateVariant(int firstVariant, int mate) {
            return (firstVariant + mate * PuffShape.ToneSteps) % PuffSettings.VariantCount;
        }

        /// <summary>Shape at <paramref name="roll"/> (0..1): even at thickness 1, thicker smoke leaning to the lumpier (more blobs) shapes.</summary>
        public static int ShapeFor(double roll, double thickness) {
            double leaned = Math.Pow(Clamp01(roll), 1.0 / Math.Max(0.25, thickness));
            return Math.Min(PuffSettings.VariantCount - 1, (int)(leaned * PuffSettings.VariantCount));
        }

        private static double Clamp01(double value) {
            return Math.Min(1.0, Math.Max(0.0, value));
        }
    }
}
