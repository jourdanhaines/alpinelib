using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // Spawns are a pure function of the seed, the cell and the drift: two sessions agree at the same clock, the rate
    // holds over an hour of the real wind, no cell ever has more than its cap out, and points sit inside the cell biased
    // to its upwind side.
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

        [Fact]
        public void TheRateHoldsOverAnHourOfWind() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner spawner = Create(wind, RollerSpawnSettings.Default);
            foreach ((double from, double to) in new[] { (0.0, 3600.0), (3600.0, 7200.0) }) {
                double start = spawner.DriftOf(wind.Displacement(from));
                double end = spawner.DriftOf(wind.Displacement(to));
                int count = 0;
                const int cells = 100;
                for (int cell = 0; cell < cells; cell++) {
                    var spawns = new List<RollerSpawn>();
                    for (long bucket = spawner.BucketOf(start); bucket <= spawner.BucketOf(end); bucket++) spawner.CandidatesIn(cell % 10, cell / 10, bucket, spawns);
                    foreach (RollerSpawn spawn in spawns) count += spawn.Drift >= start && spawn.Drift < end ? 1 : 0;
                }
                double perMinute = count / (double)cells / ((to - from) / 60.0);
                Assert.InRange(perMinute, 2.0 * 0.8, 2.0 * 1.2);
            }
        }

        [Fact]
        public void NoCellEverHasMoreThanItsCapOut() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            var busy = new RollerSpawnSettings { PerCellPerMinute = 8.0 };
            RollerSpawner spawner = Create(wind, busy);
            int capped = 0;
            for (double drift = 0.0; drift < 4000.0; drift += 7.3) {
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
            Assert.True(capped > 5, $"the cap bound only {capped} times");
        }

        [Fact]
        public void PointsSitInsideTheCellOnItsUpwindSide() {
            var wind = new WindField(WorldSeed, WindSettings.Default);
            RollerSpawner spawner = Create(wind, RollerSpawnSettings.Default);
            double sum = 0.0;
            int points = 0;
            for (long bucket = 0; bucket < 200; bucket++) {
                var spawns = new List<RollerSpawn>();
                spawner.CandidatesIn(4, 7, bucket, spawns);
                foreach (RollerSpawn spawn in spawns) {
                    for (int attempt = 0; attempt < spawner.Spawn.PointTries; attempt++) {
                        spawner.PointOf(spawn, attempt, out double x, out double z);
                        double localX = x - RollerCellWorld.CentreOf(4, CellSize), localZ = z - RollerCellWorld.CentreOf(7, CellSize);
                        double limit = CellSize * 0.5 - spawner.Spawn.EdgeMargin;
                        Assert.InRange(localX, -limit, limit);
                        Assert.InRange(localZ, -limit, limit);
                        sum += (localX * spawner.Prevailing.X + localZ * spawner.Prevailing.Z) / (CellSize * 0.5);
                        points++;
                    }
                }
            }
            Assert.True(points > 200);
            Assert.True(sum / points < -0.2, $"points sit {sum / points:0.000} half-cells along the wind on average (want upwind)");
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

        private static RollerSpawner Create(WindField wind, RollerSpawnSettings settings) {
            return new RollerSpawner(WorldSeed, CellSize, wind.Prevailing, RollerSpawner.AlongSpeedOf(wind), settings, RollerPathSettings.Default);
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
