using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // Spawns are a pure function of the seed, the cell and the drift: two sessions agree at the same clock, the admitted
    // and built rate holds over hours of the real wind, no cell ever has more than its cap out, points are uniform over
    // the cell, each cell's buckets are phase-shifted so they never turn over together, and pruning the caches changes
    // nothing a later query sees.
    public sealed class RollerSpawnerTests {
        private const double CellSize = 128.0;
        private const ulong WorldSeed = 0x5EEDUL;

        [Fact]
        public void TwoSessionsAgreeAtTheSameClock() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner first = Create(wind, RollerSpawnSettings.Default);
            RollerSpawner second = Create(new WindField(WorldSeed, WindSettings.Default), RollerSpawnSettings.Default);
            int seen = 0;
            foreach (double time in new[] { 900.0, 61.0, 3600.0, 1234.5 }) {
                double drift = first.DriftOf(wind.Displacement(time));
                for (long cellX = -3; cellX <= 3; cellX++) {
                    for (long cellZ = -3; cellZ <= 3; cellZ++) {
                        var one = new List<RollerSpawn>();
                        var two = new List<RollerSpawn>();
                        first.AliveAt(cellX, cellZ, drift, one);
                        second.AliveAt(cellX, cellZ, drift, two);
                        Assert.Equal(Describe(one, first), Describe(two, second));
                        seen += one.Count;
                    }
                }
            }
            Assert.True(seen > 50, $"only {seen} spawns seen");
        }

        // Admitted spawns (the cap counts only admitted ones) and the rolls built from them in an open world both hold
        // the configured rate within 20 % over two hours of the real wind.
        [Fact]
        public void TheAdmittedAndBuiltRateHoldsOverHoursOfWind() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawnSettings settings = RollerSpawnSettings.Default;
            RollerSpawner spawner = Create(wind, settings);
            const int cells = 100;
            const int builtCells = 12;
            foreach ((double from, double to) in new[] { (0.0, 3600.0), (3600.0, 7200.0) }) {
                double start = spawner.DriftOf(wind.Displacement(from));
                double end = spawner.DriftOf(wind.Displacement(to));
                int admitted = 0, built = 0, tried = 0;
                for (int cell = 0; cell < cells; cell++) {
                    foreach (RollerSpawn spawn in AdmittedBetween(spawner, cell % 10, cell / 10, start, end)) {
                        admitted++;
                        if (cell >= builtCells) continue;

                        tried++;
                        built += BuildOpen(spawner, spawn) != null ? 1 : 0;
                    }
                }
                double minutes = (to - from) / 60.0;
                Assert.InRange(admitted / (double)cells / minutes, settings.PerCellPerMinute * 0.8, settings.PerCellPerMinute * 1.2);
                Assert.True(tried > 100, $"only {tried} rolls built");
                Assert.InRange(built / (double)builtCells / minutes, settings.PerCellPerMinute * 0.8, settings.PerCellPerMinute * 1.2);
            }
        }

        [Fact]
        public void NoCellEverHasMoreThanItsCapOut() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            var busy = new RollerSpawnSettings { PerCellPerMinute = 8.0 };
            RollerSpawner spawner = Create(wind, busy);
            int capped = 0;
            for (double drift = 0.0; drift < 6000.0; drift += 7.3) {
                var alive = new List<RollerSpawn>();
                spawner.AliveAt(1, -2, drift, alive);
                Assert.True(alive.Count <= busy.MaxPerCell, $"{alive.Count} out at drift {drift:0.0}");
                capped += alive.Count == busy.MaxPerCell ? 1 : 0;
                foreach (RollerSpawn spawn in alive) {
                    Assert.True(spawner.IsAdmitted(spawn));
                    Assert.True(spawn.IsAliveAt(drift));
                    Assert.InRange(spawn.Life, spawner.Paths.MinLife, spawner.Paths.MaxLife);
                    Assert.Equal(RollerPath.SizeOf(spawn.Id, spawner.Paths), spawn.Size);
                }
            }
            Assert.True(capped > 100, $"the cap bound only {capped} times");
        }

        [Fact]
        public void PointsAreUniformOverTheCell() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner spawner = Create(wind, new RollerSpawnSettings { PerCellPerMinute = 4.0 });
            double along = 0.0;
            var quarters = new int[4];
            int points = 0;
            for (long bucket = 0; bucket < 400; bucket++) {
                var spawns = new List<RollerSpawn>();
                spawner.CandidatesIn(4, 7, bucket, spawns);
                foreach (RollerSpawn spawn in spawns) {
                    spawner.PointOf(spawn, 0, out double x, out double z);
                    double localX = x - RollerCellWorld.CentreOf(4, CellSize), localZ = z - RollerCellWorld.CentreOf(7, CellSize);
                    double limit = CellSize * 0.5 - spawner.Spawn.EdgeMargin;
                    Assert.InRange(localX, -limit, limit);
                    Assert.InRange(localZ, -limit, limit);
                    along += (localX * spawner.Prevailing.X + localZ * spawner.Prevailing.Z) / (CellSize * 0.5);
                    quarters[(localX < 0.0 ? 0 : 1) + (localZ < 0.0 ? 0 : 2)]++;
                    points++;
                }
            }
            Assert.True(points > 1000, $"only {points} points");
            Assert.True(Math.Abs(along / points) < 0.05, $"points sit {along / points:0.000} half-cells along the wind on average (want none)");
            foreach (int quarter in quarters) Assert.InRange(quarter / (double)points, 0.22, 0.28);
        }

        // Over a 7 x 7 window, every cell has its own bucket phase, and no drift metre turns more than three cells' buckets over.
        [Fact]
        public void BucketsArePhaseShiftedPerCell() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner spawner = Create(wind, RollerSpawnSettings.Default);
            var phases = new HashSet<long>();
            int worst = 0;
            for (double drift = 1000.0; drift < 1240.0; drift += 1.0) {
                int turning = 0;
                for (long cellX = -3; cellX <= 3; cellX++) {
                    for (long cellZ = -3; cellZ <= 3; cellZ++) {
                        if (drift == 1000.0) phases.Add((long)Math.Round(spawner.PhaseOf(cellX, cellZ) * 1000.0));
                        turning += spawner.BucketOf(cellX, cellZ, drift + 1.0) != spawner.BucketOf(cellX, cellZ, drift) ? 1 : 0;
                    }
                }
                worst = Math.Max(worst, turning);
            }
            Assert.Equal(49, phases.Count);
            Assert.InRange(worst, 1, 3);
        }

        // Prefetching and pruning (old buckets, unloaded cells) never change what a later query returns.
        [Fact]
        public void PruningAndPrefetchChangeNothingSeen() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner pruned = Create(wind, RollerSpawnSettings.Default);
            RollerSpawner plain = Create(new WindField(WorldSeed, WindSettings.Default), RollerSpawnSettings.Default);
            int most = 0;
            for (double drift = 0.0; drift < 8000.0; drift += 37.0) {
                for (long cellX = 0; cellX < 3; cellX++) {
                    var one = new List<RollerSpawn>();
                    var two = new List<RollerSpawn>();
                    pruned.Prefetch(cellX, 0, drift + 10.0);
                    pruned.AliveAt(cellX, 0, drift, one);
                    plain.AliveAt(cellX, 0, drift, two);
                    Assert.Equal(Describe(two, plain), Describe(one, pruned));
                }
                pruned.Prune(drift, (cellX, cellZ) => cellX != 2 || (int)(drift / 500.0) % 2 == 0);
                most = Math.Max(most, pruned.CachedBuckets);
            }
            Assert.True(most < plain.CachedBuckets / 2 && pruned.ChainCount <= 3, $"pruned cache held {most} buckets ({pruned.ChainCount} chains), unpruned {plain.CachedBuckets}");
        }

        // Stepping the drift forward and adding only what appeared since the last step finds every spawn a full query does.
        [Fact]
        public void StepsFindEverySpawnAFullQueryFinds() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner spawner = Create(wind, new RollerSpawnSettings { PerCellPerMinute = 2.0 });
            var known = new HashSet<ulong>();
            var step = new List<RollerSpawn>();
            var full = new List<RollerSpawn>();
            spawner.AliveAt(2, 3, 900.0, full);
            foreach (RollerSpawn spawn in full) known.Add(spawn.Id);
            for (double drift = 900.25; drift < 5000.0; drift += 0.25) {
                step.Clear();
                spawner.AppearedBetween(2, 3, drift - 0.25, drift, step);
                foreach (RollerSpawn spawn in step) known.Add(spawn.Id);
                full.Clear();
                spawner.AliveAt(2, 3, drift, full);
                foreach (RollerSpawn spawn in full) Assert.True(known.Contains(spawn.Id), $"spawn {spawn.Id:X} out at {drift:0.00} never appeared in a step");
            }
            Assert.True(known.Count > 30, $"only {known.Count} spawns stepped over");
        }

        [Fact]
        public void AFadeOverTheSlowestDriftNeverTakesLongerThanASecond() {
            WindSettings settings = WindSettings.Default;
            double slowest = RollerSpawner.SlowestDriftSpeed(settings);
            var wind = new WindField(WorldSeed, settings);
            for (double time = 0.0; time < 7200.0; time += 0.5) {
                double along = (wind.Displacement(time + 1.0) - wind.Displacement(time)).X * wind.Prevailing.X
                    + (wind.Displacement(time + 1.0) - wind.Displacement(time)).Z * wind.Prevailing.Z;
                Assert.True(along >= slowest - 1e-9, $"at {time} s the wind drifts only {along:0.000} m in a second (slowest {slowest:0.000})");
            }
        }

        public static RollerSpawner Create(WindField wind, RollerSpawnSettings settings) {
            return new RollerSpawner(WorldSeed, CellSize, wind.Prevailing, RollerSpawner.AlongSpeedOf(wind), settings, RollerPathSettings.Default, new RollerWindClock(wind));
        }

        private static IEnumerable<RollerSpawn> AdmittedBetween(RollerSpawner spawner, long cellX, long cellZ, double start, double end) {
            for (long bucket = Math.Max(0, spawner.BucketOf(cellX, cellZ, start)); bucket <= spawner.BucketOf(cellX, cellZ, end); bucket++) {
                foreach (RollerSpawn spawn in spawner.AdmittedOf(cellX, cellZ, bucket)) {
                    if (spawn.Drift >= start && spawn.Drift < end) yield return spawn;
                }
            }
        }

        private static RollerPath BuildOpen(RollerSpawner spawner, RollerSpawn spawn) {
            spawner.PointOf(spawn, 0, out double x, out double z);
            return RollerPath.Build(spawn, x, z, 0.45 * spawn.Size, spawn.Life, spawner.Clock, spawner.Paths, RollerTestWorld.Open(), null);
        }

        private static string Describe(List<RollerSpawn> spawns, RollerSpawner spawner) {
            var parts = new List<string>();
            foreach (RollerSpawn spawn in spawns) {
                spawner.PointOf(spawn, 0, out double x, out double z);
                parts.Add($"{spawn.Id:X}:{BitConverter.DoubleToInt64Bits(spawn.Drift)}:{BitConverter.DoubleToInt64Bits(spawn.Life)}:{BitConverter.DoubleToInt64Bits(x)}:{BitConverter.DoubleToInt64Bits(z)}");
            }
            return string.Join(" ", parts);
        }
    }
}
