using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// When and where wind-rolled props appear: per cell and per bucket of <see cref="RollerSpawnSettings.BucketDrift"/>
    /// drift metres a seeded Poisson count of spawns at seeded drifts inside the bucket, each at a seeded point inside the
    /// cell biased to its upwind side. Drift is the wind's travel along its prevailing direction (never decreasing), so
    /// spawns are a pure function of the world seed, the cell and the session clock: every client agrees without sync.
    /// </summary>
    /// <remarks>
    /// The count's mean is <see cref="RollerSpawnSettings.PerCellPerMinute"/> over a minute of the wind's mean drift
    /// (<see cref="AlongSpeed"/>), so windier spells bring more props. A spawn is admitted only while fewer than
    /// <see cref="RollerSpawnSettings.MaxPerCell"/> earlier candidates of its cell are still out (alive by their whole
    /// life, blocked or not): a rule over candidates alone, so it never depends on the world or on earlier admissions.
    /// Candidates are cached per cell and bucket; not thread safe, except <see cref="PointOf"/> and the static helpers.
    /// </remarks>
    public sealed class RollerSpawner {
        private const ulong CellSalt = 0x43454C4CUL;
        private const ulong BucketSalt = 0x4255434BUL;
        private const ulong LifeSalt = 0x4C494645UL;
        private const ulong PointSalt = 0x504F494EUL;

        private readonly double _meanPerBucket;
        private readonly double _exponent;
        private const int MostCachedBuckets = 4096;

        private readonly List<RollerSpawn> _scratch = new List<RollerSpawn>();
        private readonly Dictionary<(long X, long Z, long Bucket), RollerSpawn[]> _cache = new Dictionary<(long X, long Z, long Bucket), RollerSpawn[]>();
        private readonly Dictionary<(long X, long Z, long Bucket), RollerSpawn[]> _admitted = new Dictionary<(long X, long Z, long Bucket), RollerSpawn[]>();

        /// <summary>
        /// Spawns of world seed <paramref name="seed"/> in cells of <paramref name="cellSize"/> metres for a wind
        /// blowing toward <paramref name="prevailing"/> at a mean drift of <paramref name="alongSpeed"/> metres per second
        /// (<see cref="AlongSpeedOf"/>). Throws when the settings do not validate.
        /// </summary>
        public RollerSpawner(ulong seed, double cellSize, WindVector prevailing, double alongSpeed, RollerSpawnSettings spawn, RollerPathSettings paths) {
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
            _meanPerBucket = spawn.PerCellPerMinute / 60.0 / alongSpeed * spawn.BucketDrift;
            _exponent = Math.Exp(-_meanPerBucket);
        }

        public ulong Seed { get; }
        public double CellSize { get; }

        /// <summary>Unit direction the wind's drift is measured along.</summary>
        public WindVector Prevailing { get; }

        /// <summary>Mean drift metres per second the rate is set for.</summary>
        public double AlongSpeed { get; }

        public RollerSpawnSettings Spawn { get; }
        public RollerPathSettings Paths { get; }

        /// <summary>Mean candidates per cell and bucket.</summary>
        public double MeanPerBucket => _meanPerBucket;

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

        public long BucketOf(double drift) {
            return (long)Math.Floor(drift / Spawn.BucketDrift);
        }

        /// <summary>Every candidate of cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) in <paramref name="bucket"/>, in index order (admitted or not).</summary>
        public void CandidatesIn(long cellX, long cellZ, long bucket, List<RollerSpawn> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (bucket < 0) return;

            if (!_cache.TryGetValue((cellX, cellZ, bucket), out RollerSpawn[] spawns)) {
                if (_cache.Count >= MostCachedBuckets) ClearCache();
                spawns = DrawBucket(cellX, cellZ, bucket);
                _cache[(cellX, cellZ, bucket)] = spawns;
            }
            into.AddRange(spawns);
        }

        // The bucket's seeded Poisson count of spawns at seeded drifts inside it, each with its traits.
        private RollerSpawn[] DrawBucket(long cellX, long cellZ, long bucket) {
            ulong bucketSeed = StableHash.Mix(CellSeed(cellX, cellZ), StableHash.Mix(BucketSalt, (ulong)bucket));
            var random = new SplitMix64(bucketSeed);
            var spawns = new RollerSpawn[PoissonCount(random)];
            for (int index = 0; index < spawns.Length; index++) {
                double drift = (bucket + random.NextDouble()) * Spawn.BucketDrift;
                ulong id = StableHash.Mix(bucketSeed, (ulong)index + 1UL);
                double size = RollerPath.SizeOf(id, Paths);
                double share = RollerPath.GroundSpeedShareOf(id, size, Paths);
                double life = Lerp(Paths.MinLife, Paths.MaxLife, new SplitMix64(StableHash.Mix(id, LifeSalt)).NextDouble());
                spawns[index] = new RollerSpawn(id, cellX, cellZ, bucket, index, drift, size, share, life);
            }
            return spawns;
        }

        /// <summary>Admitted spawns of the cell out at <paramref name="drift"/> (appeared, whole life not yet rolled), in spawn order.</summary>
        public void AliveAt(long cellX, long cellZ, double drift, List<RollerSpawn> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));

            long lastBucket = BucketOf(drift);
            for (long bucket = Math.Max(0, BucketOf(drift - Paths.MostLifeDrift)); bucket <= lastBucket; bucket++) AddAlive(AdmittedIn(cellX, cellZ, bucket), drift, into);
        }

        private static void AddAlive(RollerSpawn[] spawns, double drift, List<RollerSpawn> into) {
            foreach (RollerSpawn spawn in spawns) {
                if (spawn.IsAliveAt(drift)) into.Add(spawn);
            }
        }

        // The bucket's admitted spawns in spawn order, worked out once per bucket.
        private RollerSpawn[] AdmittedIn(long cellX, long cellZ, long bucket) {
            if (_admitted.TryGetValue((cellX, cellZ, bucket), out RollerSpawn[] admitted)) return admitted;

            var candidates = new List<RollerSpawn>();
            CandidatesIn(cellX, cellZ, bucket, candidates);
            candidates.Sort(CompareSpawnOrder);
            candidates.RemoveAll((spawn) => !IsAdmitted(spawn));
            admitted = candidates.ToArray();
            _admitted[(cellX, cellZ, bucket)] = admitted;
            return admitted;
        }

        private void ClearCache() {
            _cache.Clear();
            _admitted.Clear();
        }

        /// <summary>True when fewer than the cap of earlier candidates of the spawn's cell are still out when it appears.</summary>
        public bool IsAdmitted(RollerSpawn spawn) {
            CollectBack(spawn.CellX, spawn.CellZ, spawn.Drift, Paths.MostLifeDrift);
            return IsAdmittedAmong(spawn, _scratch);
        }

        /// <summary>
        /// Absolute point of the spawn's try <paramref name="attempt"/> (0 first): the most upwind of
        /// <see cref="RollerSpawnSettings.UpwindPicks"/> uniform points inside its cell, clear of the edge margin.
        /// </summary>
        public void PointOf(RollerSpawn spawn, int attempt, out double x, out double z) {
            var random = new SplitMix64(StableHash.Mix(spawn.Id, StableHash.Mix(PointSalt, (ulong)attempt)));
            double half = Math.Max(0.0, CellSize * 0.5 - Spawn.EdgeMargin);
            double bestX = 0.0, bestZ = 0.0, bestAlong = double.PositiveInfinity;
            for (int pick = 0; pick < Spawn.UpwindPicks; pick++) {
                double localX = (random.NextDouble() * 2.0 - 1.0) * half;
                double localZ = (random.NextDouble() * 2.0 - 1.0) * half;
                double along = localX * Prevailing.X + localZ * Prevailing.Z;
                if (along >= bestAlong) continue;

                bestAlong = along;
                bestX = localX;
                bestZ = localZ;
            }
            x = RollerCellWorld.CentreOf(spawn.CellX, CellSize) + bestX;
            z = RollerCellWorld.CentreOf(spawn.CellZ, CellSize) + bestZ;
        }

        // Every candidate of the cell from `reach` drift metres before `drift` up to its bucket, in spawn order.
        private void CollectBack(long cellX, long cellZ, double drift, double reach) {
            _scratch.Clear();
            long lastBucket = BucketOf(drift);
            long firstBucket = Math.Max(0, BucketOf(drift - reach));
            for (long bucket = firstBucket; bucket <= lastBucket; bucket++) CandidatesIn(cellX, cellZ, bucket, _scratch);

            _scratch.Sort(CompareSpawnOrder);
        }

        private bool IsAdmittedAmong(RollerSpawn spawn, List<RollerSpawn> candidates) {
            int stillOut = 0;
            foreach (RollerSpawn earlier in candidates) {
                if (!earlier.IsBefore(spawn)) break;
                if (earlier.EndDrift > spawn.Drift) stillOut++;
            }
            return stillOut < Spawn.MaxPerCell;
        }

        /// <summary>Sort comparison in spawn order (<see cref="RollerSpawn.IsBefore"/>).</summary>
        public static int CompareSpawnOrder(RollerSpawn first, RollerSpawn second) {
            return first.IsBefore(second) ? -1 : second.IsBefore(first) ? 1 : 0;
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
