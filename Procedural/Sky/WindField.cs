using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A global, noise-driven wind that is a pure function of a seed and session time: every client with the
    /// same seed and clock gets the same direction, speed and accumulated drift.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The heading is the direction the wind blows toward, in radians clockwise from +z (North) toward +x
    /// (East). <see cref="Displacement"/> integrates <see cref="Velocity"/> from t = 0 in fixed
    /// <see cref="StepSeconds"/> steps, each sampled at its midpoint and summed in order in double, so the
    /// same t gives the same bits whether it is reached step by step or replayed cold from 0.
    /// </para>
    /// <para>
    /// Only IEEE-exact operations feed the drift (add, multiply, divide, square root, floor and integer hashing):
    /// the heading is the prevailing unit vector bent toward its perpendicular by <c>tan(wander) × noise</c> and
    /// renormalised, with the tangent from a fixed series, so no platform trig library can split clients apart.
    /// </para>
    /// <para>
    /// The running sum is cached (with a checkpoint every <see cref="CheckpointSteps"/> steps and the previous
    /// step's sum), so frame-by-frame queries cost one step at most, a clock easing back by under a step costs
    /// nothing, and an hour-long cold join is 3600 steps. A longer cold join (time counts from server start) is
    /// replayed off the main thread with <see cref="Replay"/> and taken in with <see cref="Adopt"/>. Not thread
    /// safe, except <see cref="Replay"/> and the pure functions, which never touch the cache.
    /// </para>
    /// <para>
    /// The drift is unbounded in double: consumers reduce it modulo their own tile in double before any float cast.
    /// </para>
    /// </remarks>
    public sealed class WindField {
        /// <summary>Integration step of <see cref="Displacement"/>, seconds.</summary>
        public const double StepSeconds = 1.0;

        /// <summary>Steps between cached checkpoints of the running sum.</summary>
        public const int CheckpointSteps = 256;

        /// <summary>Most steps a caller should sum on the main thread; past it, replay on a worker (<see cref="Replay"/>).</summary>
        public const int ReplayBudgetSteps = 4096;

        private const double GustWeight = 0.7;
        private const double GustDetailWeight = 0.3;
        private const double MaxWanderDegrees = 80.0;
        private const int SeriesTerms = 14;
        private const ulong PrevailingSalt = 0x50524556UL;
        private const ulong HeadingSalt = 0x48454144UL;
        private const ulong GustSalt = 0x47555354UL;
        private const ulong GustDetailSalt = 0x47444554UL;

        private readonly ulong _headingSeed;
        private readonly ulong _gustSeed;
        private readonly ulong _gustDetailSeed;
        private readonly FbmSettings _headingNoise;
        private readonly FbmSettings _gustNoise;
        private readonly FbmSettings _gustDetailNoise;
        private readonly double _wanderTangent;
        private readonly WindVector _prevailing;
        private readonly List<WindVector> _checkpoints = new List<WindVector> { WindVector.Zero };
        private long _cachedSteps;
        private WindVector _cachedSum;
        private WindVector _previousSum;
        private bool _hasPrevious;

        /// <summary>The wind of <paramref name="seed"/> shaped by <paramref name="settings"/>.</summary>
        public WindField(ulong seed, WindSettings settings) {
            Seed = seed;
            Settings = settings;
            _prevailing = PrevailingDirection(StableHash.Mix(seed, PrevailingSalt));
            _headingSeed = StableHash.Mix(seed, HeadingSalt);
            _gustSeed = StableHash.Mix(seed, GustSalt);
            _gustDetailSeed = StableHash.Mix(seed, GustDetailSalt);
            _headingNoise = new FbmSettings(2, 1.0 / settings.HeadingPeriodSeconds);
            _gustNoise = new FbmSettings(1, 1.0 / settings.GustPeriodSeconds);
            _gustDetailNoise = new FbmSettings(1, 1.0 / settings.GustDetailPeriodSeconds);
            _wanderTangent = SeriesTangent(Math.Min(settings.HeadingWanderDegrees, MaxWanderDegrees) * Math.PI / 180.0);
        }

        public ulong Seed { get; }
        public WindSettings Settings { get; }

        /// <summary>Unit direction the wander centres on.</summary>
        public WindVector Prevailing => _prevailing;

        /// <summary>The heading the wander centres on, radians clockwise from +z (diagnostic; the drift never reads it).</summary>
        public double PrevailingHeading => Math.Atan2(_prevailing.X, _prevailing.Z);

        /// <summary>Steps summed into the running cache so far (for tests and diagnostics).</summary>
        public long CachedSteps => _cachedSteps;

        /// <summary>Heading at <paramref name="time"/>, radians clockwise from +z (diagnostic; the drift never reads it).</summary>
        public double Heading(double time) {
            WindVector direction = Direction(time);
            return Math.Atan2(direction.X, direction.Z);
        }

        /// <summary>Unit direction the wind blows toward at <paramref name="time"/>, within the wander of <see cref="Prevailing"/>.</summary>
        public WindVector Direction(double time) {
            return Bend(Fbm.Sample1(_headingSeed, time, _headingNoise));
        }

        /// <summary>The direction for heading noise <paramref name="noise"/> in [−1, 1]: ±1 bends <see cref="Prevailing"/> by the whole wander.</summary>
        public WindVector Bend(double noise) {
            double bend = _wanderTangent * noise;
            double x = _prevailing.X + _prevailing.Z * bend;
            double z = _prevailing.Z - _prevailing.X * bend;
            double length = Math.Sqrt(x * x + z * z);
            return new WindVector(x / length, z / length);
        }

        /// <summary>Wind speed at <paramref name="time"/>, m/s, within <see cref="WindSettings.MinSpeed"/>..<see cref="WindSettings.MaxSpeed"/>.</summary>
        public double Speed(double time) {
            double gust = GustWeight * Fbm.Sample1(_gustSeed, time, _gustNoise)
                + GustDetailWeight * Fbm.Sample1(_gustDetailSeed, time, _gustDetailNoise);
            return Settings.BaseSpeed * (1.0 + Settings.GustShare * gust);
        }

        /// <summary>Wind velocity at <paramref name="time"/>, m/s.</summary>
        public WindVector Velocity(double time) {
            return Direction(time) * Speed(time);
        }

        /// <summary>
        /// How far the air has moved from t = 0 to <paramref name="time"/> seconds (zero for t ≤ 0): whole steps
        /// summed in order, plus the current step's midpoint velocity over the part already elapsed.
        /// </summary>
        public WindVector Displacement(double time) {
            if (!(time > 0.0)) return WindVector.Zero;
            if (double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time), time, "Must be finite.");

            long steps = StepsOf(time);
            WindVector whole = SumOfSteps(steps);
            double partial = time - steps * StepSeconds;
            if (partial <= 0.0) return whole;

            return whole + StepVelocity(steps) * partial;
        }

        /// <summary>The ordered sum of the first <paramref name="steps"/> steps, computed from 0 with no cache.</summary>
        public WindVector ReplaySteps(long steps) {
            WindVector sum = WindVector.Zero;
            for (long step = 0; step < steps; step++) sum += StepVelocity(step) * StepSeconds;

            return sum;
        }

        /// <summary>The whole steps <see cref="Displacement"/> of <paramref name="time"/> sums (0 for t ≤ 0 or NaN).</summary>
        public static long StepsOf(double time) {
            if (!(time > 0.0)) return 0;

            return (long)Math.Floor(time / StepSeconds);
        }

        /// <summary>Steps <see cref="Displacement"/> of <paramref name="time"/> would sum from the cache (compare with <see cref="ReplayBudgetSteps"/>).</summary>
        public long StepsToReach(double time) {
            long steps = StepsOf(time);
            if (steps >= _cachedSteps) return steps - _cachedSteps;
            if (steps == _cachedSteps - 1 && _hasPrevious) return 0;

            return steps - NearestCheckpoint(steps) * CheckpointSteps;
        }

        /// <summary>The ordered sum of the first <paramref name="steps"/> steps, resumed from the cache or the nearest checkpoint.</summary>
        public WindVector SumOfSteps(long steps) {
            if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps), steps, "Must not be negative.");
            if (steps < _cachedSteps) RewindTo(steps);

            while (_cachedSteps < steps) {
                _previousSum = _cachedSum;
                _hasPrevious = true;
                _cachedSum += StepVelocity(_cachedSteps) * StepSeconds;
                _cachedSteps++;
                if (_cachedSteps % CheckpointSteps == 0 && _cachedSteps / CheckpointSteps == _checkpoints.Count) _checkpoints.Add(_cachedSum);
            }

            return _cachedSum;
        }

        /// <summary>
        /// The ordered sum of the first <paramref name="steps"/> steps with its checkpoints, computed from 0 without
        /// touching the cache, so it may run on a worker thread; hand the result to <see cref="Adopt"/>.
        /// </summary>
        public WindReplay Replay(long steps) {
            if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps), steps, "Must not be negative.");

            var checkpoints = new List<WindVector>((int)(steps / CheckpointSteps) + 1) { WindVector.Zero };
            WindVector sum = WindVector.Zero;
            for (long step = 0; step < steps; step++) {
                sum += StepVelocity(step) * StepSeconds;
                if ((step + 1) % CheckpointSteps == 0) checkpoints.Add(sum);
            }

            return new WindReplay(this, steps, sum, checkpoints);
        }

        /// <summary>Takes in a <see cref="Replay"/> of this field: the cache jumps ahead to it and keeps its checkpoints. Main thread.</summary>
        public void Adopt(WindReplay replay) {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            if (!ReferenceEquals(replay.Field, this)) throw new ArgumentException("The replay is of another field.", nameof(replay));

            for (int index = _checkpoints.Count; index < replay.Checkpoints.Count; index++) _checkpoints.Add(replay.Checkpoints[index]);
            if (replay.Steps <= _cachedSteps) return;

            _cachedSteps = replay.Steps;
            _cachedSum = replay.Sum;
            _hasPrevious = false;
        }

        // One step back is the kept previous sum; further back resumes from the nearest checkpoint.
        private void RewindTo(long steps) {
            if (steps == _cachedSteps - 1 && _hasPrevious) {
                _cachedSteps = steps;
                _cachedSum = _previousSum;
                _hasPrevious = false;
                return;
            }

            long checkpoint = NearestCheckpoint(steps);
            _cachedSteps = checkpoint * CheckpointSteps;
            _cachedSum = _checkpoints[(int)checkpoint];
            _hasPrevious = false;
        }

        private long NearestCheckpoint(long steps) {
            return Math.Min(steps / CheckpointSteps, _checkpoints.Count - 1);
        }

        // A uniform direction by rejection from the unit disc (square root only).
        private static WindVector PrevailingDirection(ulong seed) {
            var random = new SplitMix64(seed);
            while (true) {
                double x = random.NextDouble() * 2.0 - 1.0;
                double z = random.NextDouble() * 2.0 - 1.0;
                double square = x * x + z * z;
                if (square <= 0.0625 || square > 1.0) continue;

                double length = Math.Sqrt(square);
                return new WindVector(x / length, z / length);
            }
        }

        /// <summary>tan(<paramref name="angle"/>) for |angle| below 90°, from Taylor series of sin and cos in add, multiply and divide only.</summary>
        public static double SeriesTangent(double angle) {
            double square = angle * angle;
            double sine = 0.0;
            double cosine = 0.0;
            double sineTerm = angle;
            double cosineTerm = 1.0;
            for (int order = 0; order < SeriesTerms; order++) {
                sine += sineTerm;
                cosine += cosineTerm;
                sineTerm *= -square / ((2 * order + 2) * (2 * order + 3));
                cosineTerm *= -square / ((2 * order + 1) * (2 * order + 2));
            }

            return sine / cosine;
        }

        private WindVector StepVelocity(long step) {
            return Velocity((step + 0.5) * StepSeconds);
        }
    }
}
