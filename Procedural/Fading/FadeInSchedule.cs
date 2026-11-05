using System;

namespace AlpineLib.Procedural.Fading {
    /// <summary>
    /// When each of a set of items (instances, renderers) fades in: item i goes from 0 to 1 over
    /// <see cref="Duration"/> seconds, <see cref="DelayOf"/> seconds after <see cref="Start"/>. Held (amount 0)
    /// until started, so items made over several frames can wait and then fade together.
    /// </summary>
    public sealed class FadeInSchedule {
        private const double MillimetresPerMetre = 1000.0;
        private const double UnitScale = 1.0 / (1UL << 53);

        private readonly float[] _delays;
        private float _maxDelay;

        /// <summary>Items fading over <paramref name="durationSeconds"/>, each after its delay in <paramref name="delays"/> (copied).</summary>
        public FadeInSchedule(double durationSeconds, float[] delays) {
            if (!(durationSeconds > 0.0) || double.IsInfinity(durationSeconds)) {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Fade duration must be positive and finite.");
            }

            Duration = durationSeconds;
            _delays = delays != null ? (float[])delays.Clone() : Array.Empty<float>();
            RefreshMaxDelay();
        }

        /// <summary>Seconds each item takes from 0 to 1.</summary>
        public double Duration { get; }

        /// <summary>Items in the schedule.</summary>
        public int Count => _delays.Length;

        /// <summary>Clock time the fade began, or NaN while held.</summary>
        public double Start { get; private set; } = double.NaN;

        /// <summary>True until <see cref="StartAt"/>: every item reads 0.</summary>
        public bool IsHeld => double.IsNaN(Start);

        /// <summary>The longest delay of any item.</summary>
        public float MaxDelay => _maxDelay;

        /// <summary>Seconds after <see cref="Start"/> item <paramref name="index"/> begins to show.</summary>
        public float DelayOf(int index) {
            return _delays[index];
        }

        /// <summary>
        /// Moves item <paramref name="index"/>'s delay by <paramref name="seconds"/>; a negative shift far enough
        /// back makes it start fully shown (an item that was already showing elsewhere).
        /// </summary>
        public void ShiftDelay(int index, float seconds) {
            _delays[index] += seconds;
            RefreshMaxDelay();
        }

        /// <summary>Moves the delay of every item whose <paramref name="which"/> entry is true by <paramref name="seconds"/> (one rescan).</summary>
        public void ShiftDelays(bool[] which, float seconds) {
            if (which == null) throw new ArgumentNullException(nameof(which));
            if (which.Length != _delays.Length) throw new ArgumentException($"Expected {_delays.Length} flags, got {which.Length}.", nameof(which));

            for (int index = 0; index < which.Length; index++) {
                if (which[index]) _delays[index] += seconds;
            }
            RefreshMaxDelay();
        }

        /// <summary>Begins the fade at clock time <paramref name="start"/>.</summary>
        public void StartAt(double start) {
            if (double.IsNaN(start) || double.IsInfinity(start)) throw new ArgumentOutOfRangeException(nameof(start), start, "Fade start must be finite.");

            Start = start;
        }

        /// <summary>Item <paramref name="index"/>'s amount at clock time <paramref name="now"/>: 0 hidden, 1 fully shown.</summary>
        public float AmountOf(int index, double now) {
            if (IsHeld) return 0f;

            return Amount(now - Start - _delays[index], Duration);
        }

        /// <summary>True once every item reads 1 at <paramref name="now"/>.</summary>
        public bool IsDone(double now) {
            return !IsHeld && now - Start >= Duration + _maxDelay;
        }

        /// <summary>Writes the amounts of items <paramref name="first"/>.. into <paramref name="amounts"/> from index 0.</summary>
        public void Fill(float[] amounts, int first, int count, double now) {
            for (int index = 0; index < count; index++) {
                amounts[index] = AmountOf(first + index, now);
            }
        }

        /// <summary>0..1 share of a fade <paramref name="elapsed"/> seconds in, over <paramref name="duration"/> seconds.</summary>
        public static float Amount(double elapsed, double duration) {
            if (!(elapsed > 0.0)) return 0f;
            if (elapsed >= duration) return 1f;

            return (float)(elapsed / duration);
        }

        /// <summary>
        /// A stable delay in [0, <paramref name="maxSeconds"/>) for an item at (<paramref name="x"/>, <paramref name="z"/>)
        /// (metres, quantised to millimetres), so the same item gets the same delay wherever it is built.
        /// </summary>
        public static float OffsetFor(double x, double z, double maxSeconds) {
            if (!(maxSeconds > 0.0)) return 0f;

            ulong keyX = unchecked((ulong)(long)Math.Round(x * MillimetresPerMetre));
            ulong keyZ = unchecked((ulong)(long)Math.Round(z * MillimetresPerMetre));
            ulong hash = StableHash.Mix(keyX, keyZ);
            return (float)((hash >> 11) * UnitScale * maxSeconds);
        }

        private void RefreshMaxDelay() {
            float largest = 0f;
            foreach (float delay in _delays) {
                if (delay > largest) largest = delay;
            }
            _maxDelay = largest;
        }
    }
}
