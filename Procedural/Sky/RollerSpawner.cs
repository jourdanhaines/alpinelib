using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// When and where wind-rolled props appear: per cell and per bucket of <see cref="RollerSpawnSettings.BucketDrift"/>
    /// drift metres a seeded Poisson count of spawns at seeded drifts inside the bucket, each at seeded points uniform
    /// over the cell. Each cell's buckets are shifted by a seeded phase, so neighbouring cells never roll into a new
    /// bucket on the same frame. Drift is the wind's travel along its prevailing direction (never decreasing), so spawns
    /// are a pure function of the world seed, the cell and the session clock: every client agrees without sync.
    /// </summary>
    /// <remarks>
    /// The count's mean is <see cref="RollerSpawnSettings.PerCellPerMinute"/> over a minute of the wind's mean drift
    /// (<see cref="AlongSpeed"/>), so windier spells bring more props. A candidate is admitted while fewer than
    /// <see cref="RollerSpawnSettings.MaxPerCell"/> admitted ones of its cell are still out (by their whole life): one
    /// ordered pass per bucket carries the cell's admitted ones still out on to the next (<see cref="RollerSpawnChain"/>),
    /// from the session's first bucket, so the rule reads candidates alone, never the world or the join time, and is the
    /// same everywhere. Admissions are cached per cell and bucket (<see cref="Prefetch"/> works the next bucket out early,
    /// <see cref="Prune"/> drops old ones and unloaded cells). Not thread safe, except <see cref="PointOf"/>,
    /// <see cref="Clock"/> and the static helpers.
    /// </remarks>
    public sealed class RollerSpawner {
        private const ulong CellSalt = 0x43454C4CUL;
        private const ulong BucketSalt = 0x4255434BUL;
        private const ulong LifeSalt = 0x4C494645UL;
        private const ulong PointSalt = 0x504F494EUL;
        private const ulong PhaseSalt = 0x50484153UL;

        private readonly double _meanPerBucket;
        private readonly double _exponent;
        private readonly int _backBuckets;
        private readonly List<RollerSpawn> _kept = new List<RollerSpawn>();
        private readonly List<(long X, long Z, long Bucket)> _stale = new List<(long X, long Z, long Bucket)>();
        private readonly List<(long X, long Z)> _staleChains = new List<(long X, long Z)>();
        private readonly Dictionary<(long X, long Z), RollerSpawnChain> _chains = new Dictionary<(long X, long Z), RollerSpawnChain>();
        private readonly Dictionary<(long X, long Z, long Bucket), RollerSpawn[]> _cache = new Dictionary<(long X, long Z, long Bucket), RollerSpawn[]>();
        private readonly Dictionary<(long X, long Z, long Bucket), RollerSpawn[]> _admitted = new Dictionary<(long X, long Z, long Bucket), RollerSpawn[]>();

        /// <summary>
        /// Spawns of world seed <paramref name="seed"/> in cells of <paramref name="cellSize"/> metres for a wind
        /// blowing toward <paramref name="prevailing"/> at a mean drift of <paramref name="alongSpeed"/> metres per second
        /// (<see cref="AlongSpeedOf"/>), their rolls following <paramref name="clock"/>'s live wind (null: the prevailing
        /// direction). Throws when the settings do not validate.
        /// </summary>
        public RollerSpawner(ulong seed, double cellSize, WindVector prevailing, double alongSpeed, RollerSpawnSettings spawn, RollerPathSettings paths,
                RollerWindClock clock = null) {
            if (!(cellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Must be positive.");
            if (!(alongSpeed > 0.0)) throw new ArgumentOutOfRangeException(nameof(alongSpeed), alongSpeed, "Must be positive.");
            if (!(prevailing.Length > 0.0)) throw new ArgumentException("The prevailing direction must not be zero.", nameof(prevailing));

            Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            spawn.Validate();
            paths.Validate();
            Seed = seed;
            CellSize = cellSize;
            Prevailing = prevailing * (1.0 / prevailing.Length);
            AlongSpeed = alongSpeed;
            Clock = clock;
            _meanPerBucket = spawn.PerCellPerMinute / 60.0 / alongSpeed * spawn.BucketDrift;
            _exponent = Math.Exp(-_meanPerBucket);
            _backBuckets = (int)Math.Ceiling(paths.MostLifeDrift / spawn.BucketDrift) + 1;
        }

        public ulong Seed { get; }
        public double CellSize { get; }

        /// <summary>Unit direction the wind's drift is measured along.</summary>
        public WindVector Prevailing { get; }

        /// <summary>Mean drift metres per second the rate is set for.</summary>
        public double AlongSpeed { get; }

        /// <summary>The live wind rolls follow, or null (they follow <see cref="Prevailing"/>).</summary>
        public RollerWindClock Clock { get; }

        public RollerSpawnSettings Spawn { get; }
        public RollerPathSettings Paths { get; }

        /// <summary>Mean candidates per cell and bucket.</summary>
        public double MeanPerBucket => _meanPerBucket;

        /// <summary>Cell buckets cached (candidates and admissions).</summary>
        public int CachedBuckets => _cache.Count + _admitted.Count;

        /// <summary>Cells whose admission pass is carried (see <see cref="RollerSpawnChain"/>).</summary>
        public int ChainCount => _chains.Count;

        /// <summary>
        /// Mean drift metres per second of <paramref name="wind"/> along its prevailing direction over its first
        /// <paramref name="seconds"/> (replayed without touching its cache).
        /// </summary>
        public static double AlongSpeedOf(WindField wind, long seconds = 3600) {
            if (wind == null) throw new ArgumentNullException(nameof(wind));
            if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Must be at least 1.");

            WindVector sum = wind.ReplaySteps(seconds);
            double along = sum.X * wind.Prevailing.X + sum.Z * wind.Prevailing.Z;
            return along / (seconds * WindField.StepSeconds);
        }

        /// <summary>
        /// Least drift metres per second a wind of <paramref name="settings"/> ever blows along its prevailing direction (its
        /// slowest gust at its widest wander): a fade over a second of this drift never takes longer than a second.
        /// </summary>
        public static double SlowestDriftSpeed(WindSettings settings) {
            double wander = Math.Min(settings.HeadingWanderDegrees, 80.0) * Math.PI / 180.0;
            return settings.MinSpeed * Math.Cos(wander);
        }

        /// <summary>Drift metres along the prevailing direction of the wind's <paramref name="displacement"/>.</summary>
        public double DriftOf(WindVector displacement) {
            return displacement.X * Prevailing.X + displacement.Z * Prevailing.Z;
        }

        /// <summary>The seeded shift of the cell's buckets, drift metres in [0, <see cref="RollerSpawnSettings.BucketDrift"/>).</summary>
        public double PhaseOf(long cellX, long cellZ) {
            return new SplitMix64(StableHash.Mix(CellSeed(cellX, cellZ), PhaseSalt)).NextDouble() * Spawn.BucketDrift;
        }

        /// <summary>The cell's bucket holding <paramref name="drift"/>.</summary>
        public long BucketOf(long cellX, long cellZ, double drift) {
            return (long)Math.Floor((drift + PhaseOf(cellX, cellZ)) / Spawn.BucketDrift);
        }

        /// <summary>Every candidate of cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) in <paramref name="bucket"/>, in index order (admitted or not).</summary>
        public void CandidatesIn(long cellX, long cellZ, long bucket, List<RollerSpawn> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (bucket < 0) return;

            into.AddRange(Candidates(cellX, cellZ, bucket));
        }

        /// <summary>Admitted spawns of the cell out at <paramref name="drift"/> (appeared, whole life not yet rolled), in spawn order.</summary>
        public void AliveAt(long cellX, long cellZ, double drift, List<RollerSpawn> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));

            double phase = PhaseOf(cellX, cellZ);
            long lastBucket = (long)Math.Floor((drift + phase) / Spawn.BucketDrift);
            long firstBucket = Math.Max(0, (long)Math.Floor((drift - Paths.MostLifeDrift + phase) / Spawn.BucketDrift));
            for (long bucket = firstBucket; bucket <= lastBucket; bucket++) AddAlive(AdmittedIn(cellX, cellZ, bucket), drift, into);
        }

        /// <summary>
        /// Admitted spawns of the cell that appeared after drift <paramref name="after"/> and are out at
        /// <paramref name="drift"/>, in spawn order: what a refresh that saw <paramref name="after"/> has not seen yet.
        /// </summary>
        public void AppearedBetween(long cellX, long cellZ, double after, double drift, List<RollerSpawn> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));

            double phase = PhaseOf(cellX, cellZ);
            long lastBucket = (long)Math.Floor((drift + phase) / Spawn.BucketDrift);
            long firstBucket = Math.Max(0, (long)Math.Floor((after + phase) / Spawn.BucketDrift));
            for (long bucket = firstBucket; bucket <= lastBucket; bucket++) {
                foreach (RollerSpawn spawn in AdmittedIn(cellX, cellZ, bucket)) {
                    if (spawn.Drift > after && spawn.IsAliveAt(drift)) into.Add(spawn);
                }
            }
        }

        /// <summary>The cell's admitted spawns of <paramref name="bucket"/> in spawn order (worked out once, then cached).</summary>
        public IReadOnlyList<RollerSpawn> AdmittedOf(long cellX, long cellZ, long bucket) {
            return bucket < 0 ? Array.Empty<RollerSpawn>() : AdmittedIn(cellX, cellZ, bucket);
        }

        /// <summary>True when the spawn passed its bucket's admission pass.</summary>
        public bool IsAdmitted(RollerSpawn spawn) {
            foreach (RollerSpawn admitted in AdmittedIn(spawn.CellX, spawn.CellZ, spawn.Bucket)) {
                if (admitted.Id == spawn.Id) return true;
            }
            return false;
        }

        /// <summary>Works out the cell's bucket of drift <paramref name="drift"/> ahead of time (a refresh then finds it cached).</summary>
        public void Prefetch(long cellX, long cellZ, double drift) {
            long bucket = BucketOf(cellX, cellZ, drift);
            if (bucket >= 0) AdmittedIn(cellX, cellZ, bucket);
        }

        /// <summary>
        /// Forgets the buckets no prop out at <paramref name="drift"/> or later can need, and every bucket of a cell
        /// <paramref name="isKept"/> rejects (an unloaded one).
        /// </summary>
        public void Prune(double drift, Func<long, long, bool> isKept) {
            if (isKept == null) throw new ArgumentNullException(nameof(isKept));

            PruneFrom(_cache, drift, isKept, _backBuckets);
            PruneFrom(_admitted, drift, isKept, _backBuckets);
            _staleChains.Clear();
            foreach ((long X, long Z) cell in _chains.Keys) {
                if (!isKept(cell.X, cell.Z)) _staleChains.Add(cell);
            }
            foreach ((long X, long Z) cell in _staleChains) _chains.Remove(cell);
        }

        /// <summary>
        /// Absolute point of the spawn's try <paramref name="attempt"/> (0 first): uniform inside its cell, clear of the
        /// edge margin.
        /// </summary>
        public void PointOf(RollerSpawn spawn, int attempt, out double x, out double z) {
            var random = new SplitMix64(StableHash.Mix(spawn.Id, StableHash.Mix(PointSalt, (ulong)attempt)));
            double half = Math.Max(0.0, CellSize * 0.5 - Spawn.EdgeMargin);
            x = RollerCellWorld.CentreOf(spawn.CellX, CellSize) + (random.NextDouble() * 2.0 - 1.0) * half;
            z = RollerCellWorld.CentreOf(spawn.CellZ, CellSize) + (random.NextDouble() * 2.0 - 1.0) * half;
        }

        /// <summary>Sort comparison in spawn order (<see cref="RollerSpawn.IsBefore"/>).</summary>
        public static int CompareSpawnOrder(RollerSpawn first, RollerSpawn second) {
            return first.IsBefore(second) ? -1 : second.IsBefore(first) ? 1 : 0;
        }

        private static void AddAlive(RollerSpawn[] spawns, double drift, List<RollerSpawn> into) {
            foreach (RollerSpawn spawn in spawns) {
                if (spawn.IsAliveAt(drift)) into.Add(spawn);
            }
        }

        private RollerSpawn[] Candidates(long cellX, long cellZ, long bucket) {
            if (_cache.TryGetValue((cellX, cellZ, bucket), out RollerSpawn[] spawns)) return spawns;

            spawns = DrawBucket(cellX, cellZ, bucket);
            _cache[(cellX, cellZ, bucket)] = spawns;
            return spawns;
        }

        // The bucket's seeded Poisson count of spawns at seeded drifts inside it (shifted by the cell's phase), each with its traits.
        private RollerSpawn[] DrawBucket(long cellX, long cellZ, long bucket) {
            ulong bucketSeed = StableHash.Mix(CellSeed(cellX, cellZ), StableHash.Mix(BucketSalt, (ulong)bucket));
            var random = new SplitMix64(bucketSeed);
            var spawns = new RollerSpawn[PoissonCount(random)];
            double phase = PhaseOf(cellX, cellZ);
            for (int index = 0; index < spawns.Length; index++) {
                double drift = (bucket + random.NextDouble()) * Spawn.BucketDrift - phase;
                ulong id = StableHash.Mix(bucketSeed, (ulong)index + 1UL);
                double size = RollerPath.SizeOf(id, Paths);
                double share = RollerPath.GroundSpeedShareOf(id, size, Paths);
                double life = Lerp(Paths.MinLife, Paths.MaxLife, new SplitMix64(StableHash.Mix(id, LifeSalt)).NextDouble());
                spawns[index] = new RollerSpawn(id, cellX, cellZ, bucket, index, drift, size, share, life);
            }
            return spawns;
        }

        // The bucket's admitted spawns: the cell's pass carried on bucket by bucket up to it (from the first bucket again
        // when it was pruned past).
        private RollerSpawn[] AdmittedIn(long cellX, long cellZ, long bucket) {
            if (_admitted.TryGetValue((cellX, cellZ, bucket), out RollerSpawn[] admitted)) return admitted;

            if (!_chains.TryGetValue((cellX, cellZ), out RollerSpawnChain chain)) {
                chain = new RollerSpawnChain();
                _chains.Add((cellX, cellZ), chain);
            }
            if (bucket < chain.NextBucket) chain.Restart();
            while (chain.NextBucket <= bucket) {
                long next = chain.NextBucket;
                _kept.Clear();
                chain.Admit(DrawBucket(cellX, cellZ, next), Spawn.MaxPerCell, _kept);
                _admitted[(cellX, cellZ, next)] = _kept.ToArray();
            }
            return _admitted[(cellX, cellZ, bucket)];
        }

        // Drops a cache's buckets older than `keep` buckets before the drift's, or of cells no longer kept.
        private void PruneFrom(Dictionary<(long X, long Z, long Bucket), RollerSpawn[]> cache, double drift, Func<long, long, bool> isKept, int keep) {
            _stale.Clear();
            foreach ((long X, long Z, long Bucket) key in cache.Keys) {
                if (!isKept(key.X, key.Z) || key.Bucket < BucketOf(key.X, key.Z, drift) - keep) _stale.Add(key);
            }
            foreach ((long X, long Z, long Bucket) key in _stale) cache.Remove(key);
        }

        private ulong CellSeed(long cellX, long cellZ) {
            return StableHash.Mix(StableHash.Mix(StableHash.Mix(Seed, CellSalt), (ulong)cellX), (ulong)cellZ);
        }

        // Knuth's product of uniforms against e^-mean.
        private int PoissonCount(SplitMix64 random) {
            int count = 0;
            double product = random.NextDouble();
            while (product > _exponent) {
                count++;
                product *= random.NextDouble();
            }
            return count;
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
