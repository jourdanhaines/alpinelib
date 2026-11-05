using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Sky;
using AlpineLib.Procedural.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace AlpineLib.Server.Tests.Sky {
    // The ring keeps clear of every loaded window (camera, remote pawns, the train), follows the camera's cell with
    // hysteresis, and skips the draws the fog hides whole. Numbers go to the test output for the review notes.
    public sealed class BackdropKeepOutTests {
        private const double CellSize = 128.0;
        private const int LoadRadius = 3;
        private const double Slack = 8.0;
        private const int Seeds = 40;
        private static readonly BackdropFog GameFog = new BackdropFog(150.0, 750.0, 0.3);
        private readonly ITestOutputHelper _output;

        public BackdropKeepOutTests(ITestOutputHelper output) {
            _output = output;
        }

        [Fact]
        public void KeepOutMergesRowsAndMeasuresTheNearestCell() {
            var keepOut = new BackdropKeepOut(CellSize);
            Assert.True(double.IsPositiveInfinity(keepOut.Gap(0.0, 0.0)));

            keepOut.SetCells(new[] { new CellCoord(2, 0), new CellCoord(0, 0), new CellCoord(1, 0), new CellCoord(1, 0), new CellCoord(5, 0), new CellCoord(0, -2) });
            Assert.Equal(5, keepOut.CellCount);
            Assert.Equal(3, keepOut.RunCount);
            Assert.Equal(0.0, keepOut.Gap(300.0, 10.0));
            Assert.Equal(20.0, keepOut.Gap(404.0, 64.0), 9);
            Assert.Equal(10.0, keepOut.Gap(64.0, -266.0), 9);
            Assert.Equal(Math.Sqrt(2.0) * 10.0, keepOut.Gap(-10.0, 138.0), 9);
        }

        // Reviewer's probe as a gate: a remote 7x7 window 5..11 cells away. Without the loaded cells about a quarter
        // of the camera's ring stands on it; with them, none comes within the keep-out margin of any loaded cell.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void NoFeatureStandsOnAnyLoadedWindow(int remoteObservers) {
            BackdropSettings settings = ProjectSettings();
            int overlapBefore = 0;
            int keptBefore = 0;
            int kept = 0;
            for (int remoteCells = 5; remoteCells <= 11; remoteCells++) {
                for (ulong seed = 1; seed <= Seeds; seed++) {
                    BackdropLayout layout = Layout(seed, settings);
                    double cameraX = 64.0 + seed * 977.0;
                    double cameraZ = 64.0 - seed * 613.0;
                    layout.KeepOutCellOf(cameraX, cameraZ, out long cellX, out long cellZ);
                    BackdropKeepOut loaded = Loaded(cameraX, cameraZ, Remotes(cellX, cellZ, remoteCells, remoteObservers));
                    var before = new List<BackdropFeature>();
                    layout.AppendDrawn(cameraX, cameraZ, cellX, cellZ, null, before);
                    keptBefore += before.Count;
                    foreach (BackdropFeature feature in before) overlapBefore += loaded.Gap(feature.AbsoluteX(settings.SectorSize), feature.AbsoluteZ(settings.SectorSize)) < feature.Reach ? 1 : 0;

                    var after = new List<BackdropFeature>();
                    layout.AppendDrawn(cameraX, cameraZ, cellX, cellZ, loaded, after);
                    kept += after.Count;
                    foreach (BackdropFeature feature in after) CheckClearOfLoaded(feature, loaded, settings);
                }
            }

            _output.WriteLine($"{remoteObservers} remote window(s) 5..11 cells away: {overlapBefore} of {keptBefore} ring features stood on a loaded window before ({(double)overlapBefore / keptBefore:P1}), 0 of {kept} after");
            Assert.True(overlapBefore > keptBefore / 10, "the probe saw no overlap, so it proves nothing");
            Assert.True(kept > 0);
        }

        // A camera parked on a cell edge, swaying +-2 m across it for 200 frames, on an x edge, a z edge and a corner:
        // the ring changes at most once, with the camera's own window only and with its pawn's streamed cells too.
        // The old ring (no hysteresis, camera window only) swapped features on every crossing.
        [Theory]
        [InlineData(1.0, 0.0)]
        [InlineData(0.0, 1.0)]
        [InlineData(0.7071, 0.7071)]
        public void SwayingAcrossACellEdgeChangesTheRingAtMostOnce(double directionX, double directionZ) {
            BackdropSettings settings = ProjectSettings();
            int worstAfter = 0;
            int worstOwn = 0;
            int worstBefore = 0;
            for (ulong seed = 1; seed <= 10; seed++) {
                BackdropLayout layout = Layout(seed, settings);
                double edgeX = CellSize * (int)(seed * 7);
                double edgeZ = CellSize * (int)(seed * 3) + (directionZ == 0.0 ? 50.0 : 0.0);
                if (directionX == 0.0) edgeX += 50.0;
                worstAfter = Math.Max(worstAfter, Sway(layout, edgeX, edgeZ, directionX, directionZ, true, true));
                worstOwn = Math.Max(worstOwn, Sway(layout, edgeX, edgeZ, directionX, directionZ, true, false));
                worstBefore = Math.Max(worstBefore, Sway(layout, edgeX, edgeZ, directionX, directionZ, false, false));
            }

            _output.WriteLine($"sway +-2 m across ({directionX}, {directionZ}) for 200 frames: ring changed {worstBefore} times before, {worstOwn} with hysteresis (camera window only), {worstAfter} with hysteresis and streamed cells (worst of 10 seeds)");
            Assert.InRange(worstAfter, 0, 1);
            Assert.InRange(worstOwn, 0, 1);
            Assert.True(worstBefore > 10, $"without hysteresis the sway changed the ring only {worstBefore} times, so the gate proves nothing");
        }

        // Wherever the hysteretic cell lags, every kept feature is still the clear distance from the camera itself.
        [Fact]
        public void HysteresisKeepsTheClearDistanceFromTheCamera() {
            BackdropSettings settings = ProjectSettings();
            BackdropLayout layout = Layout(5UL, settings);
            var random = new SplitMix64(91UL);
            double x = 10.0;
            double z = -20.0;
            layout.KeepOutCellOf(x, z, out long cellX, out long cellZ);
            double least = double.MaxValue;
            for (int step = 0; step < 600; step++) {
                x += (random.NextDouble() - 0.45) * 9.0;
                z += (random.NextDouble() - 0.5) * 9.0;
                layout.FollowKeepOutCell(x, z, ref cellX, ref cellZ);
                var kept = new List<BackdropFeature>();
                layout.AppendDrawn(x, z, cellX, cellZ, null, kept);
                foreach (BackdropFeature feature in kept) least = Math.Min(least, layout.NearestDistance(feature, x, z));
            }

            Assert.True(least >= settings.ClearDistance - 1e-6, $"a kept feature came {least:0.0} m from the camera");
        }

        // With the game's fog (150-750 m, scale 1) a feature is drawn only while its nearest point is under 99 % hidden
        // allowing 8 m of approach; most frames draw nothing. Every culled one is wholly hidden now and >= 99 % after 8 m.
        [Fact]
        public void FogCullSkipsWhollyHiddenDraws() {
            BackdropSettings settings = ProjectSettings();
            int cameras = 0;
            int keptTotal = 0;
            int drawnTotal = 0;
            int meshesBefore = 0;
            int meshesAfter = 0;
            int mostMeshes = 0;
            int emptyFrames = 0;
            double leastHidden = 1.0;
            for (ulong seed = 1; seed <= Seeds; seed++) {
                BackdropLayout layout = Layout(seed, settings);
                for (int spot = 0; spot < 25; spot++) {
                    double x = seed * 311.0 + spot * 41.3;
                    double z = spot * 97.1 - seed * 53.0;
                    var kept = new List<BackdropFeature>();
                    layout.KeepOutCellOf(x, z, out long cellX, out long cellZ);
                    layout.AppendDrawn(x, z, cellX, cellZ, null, kept);
                    var meshes = new HashSet<int>();
                    var drawnMeshes = new HashSet<int>();
                    foreach (BackdropFeature feature in kept) {
                        meshes.Add(feature.MeshIndex);
                        bool isHidden = layout.IsFogHidden(feature, x, z, GameFog, 1.0, Slack);
                        double hidden = GameFog.HiddenShare(layout.NearestDistance(feature, x, z), 1.0);
                        if (isHidden) {
                            Assert.Equal(1.0, hidden);
                            Assert.True(GameFog.HiddenShare(layout.NearestDistance(feature, x, z) - Slack, 1.0) >= settings.FogCullShare);
                            continue;
                        }
                        drawnTotal++;
                        drawnMeshes.Add(feature.MeshIndex);
                        leastHidden = Math.Min(leastHidden, hidden);
                    }
                    cameras++;
                    keptTotal += kept.Count;
                    meshesBefore += meshes.Count;
                    meshesAfter += drawnMeshes.Count;
                    mostMeshes = Math.Max(mostMeshes, drawnMeshes.Count);
                    emptyFrames += drawnMeshes.Count == 0 ? 1 : 0;
                }
            }

            _output.WriteLine($"fog cull over {cameras} cameras: silhouettes {keptTotal / (double)cameras:0.0} -> {drawnTotal / (double)cameras:0.00} per frame; instanced draws {meshesBefore / (double)cameras:0.0} -> {meshesAfter / (double)cameras:0.00} (max {mostMeshes}, {emptyFrames} frames draw none); least hidden of anything drawn {leastHidden:P1} (contrast <= {1.0 - leastHidden:P1})");
            Assert.True(meshesAfter * 4 < meshesBefore, "the fog cull skipped too few draws");
            Assert.True(leastHidden >= 0.9, $"something drawn is only {leastHidden:P1} hidden");
        }

        // A remote pawn walks 1.3 km past a parked camera 6 cells to the side: its window loads and unloads cells, so
        // drawn features come and go; each one that does is at least as hidden as an own-crossing change (>= 90 %).
        [Fact]
        public void RemoteWindowChangesStayInTheFog() {
            BackdropSettings settings = ProjectSettings();
            int changes = 0;
            double leastHidden = 1.0;
            for (ulong seed = 1; seed <= 10; seed++) {
                BackdropLayout layout = Layout(seed, settings);
                double cameraX = 64.0 + seed * 977.0;
                double cameraZ = 64.0 - seed * 613.0;
                layout.KeepOutCellOf(cameraX, cameraZ, out long cellX, out long cellZ);
                Dictionary<string, BackdropFeature> previous = null;
                for (int step = 0; step <= 260; step++) {
                    var remote = new List<(double X, double Z)> { (cameraX - 650.0 + step * 5.0, cameraZ + 6.5 * CellSize) };
                    Dictionary<string, BackdropFeature> current = DrawnUnderFog(layout, cameraX, cameraZ, cellX, cellZ, Loaded(cameraX, cameraZ, remote));
                    if (previous != null) changes += CountChanged(layout, previous, current, cameraX, cameraZ, ref leastHidden);
                    previous = current;
                }
            }

            _output.WriteLine($"remote pawn walking past 6.5 cells out: {changes} drawn-set changes over 10 seeds, least hidden of any {leastHidden:P1} (contrast <= {1.0 - leastHidden:P1})");
            Assert.True(leastHidden >= 0.9, $"a remote window change showed a feature only {leastHidden:P1} hidden");
        }

        [Fact]
        public void FogMatchesTheShadersLinearRadialFog() {
            Assert.Equal(0.0, BackdropFog.None.HiddenShare(5000.0, 1.0));
            Assert.Equal((700.0 - 0.3 - 150.0) / 600.0, GameFog.HiddenShare(700.0, 1.0), 12);
            Assert.Equal(1.0, GameFog.HiddenShare(751.0, 1.0));
            Assert.Equal(0.0, GameFog.HiddenShare(100.0, 1.0));
            Assert.Equal((700.0 - 0.3) * 0.5 - 150.0, GameFog.HiddenShare(700.0, 0.5) * 600.0, 9);
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackdropFog(750.0, 150.0, 0.3));
        }

        [Fact]
        public void SettingsBoundTheHysteresisAndCull() {
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, new BackdropSettings { KeepOutHysteresis = -1.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, new BackdropSettings { KeepOutHysteresis = 64.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, new BackdropSettings { FogCullShare = 0.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, new BackdropSettings { FogCullShare = 1.5 }));
        }

        // One sway: the streamer's interest follows the pawn's raw cell; the ring's cell follows with hysteresis (or not).
        // Like BackdropRing, features are re-picked only when the cell or the loaded cells change, or after 8 m of motion.
        private static int Sway(BackdropLayout layout, double edgeX, double edgeZ, double directionX, double directionZ, bool hasHysteresis, bool hasStreamedCells) {
            var interest = new CellInterestSet(LoadRadius, CellInterestSet.DefaultUnloadMargin);
            var added = new List<CellCoord>();
            var removed = new List<CellCoord>();
            var loaded = new BackdropKeepOut(CellSize);
            layout.KeepOutCellOf(edgeX - 2.0 * directionX, edgeZ - 2.0 * directionZ, out long cellX, out long cellZ);
            HashSet<string> previous = null;
            int changes = 0;
            double pickedX = double.NaN;
            double pickedZ = double.NaN;
            for (int frame = 0; frame < 200; frame++) {
                double offset = 2.0 * Math.Sin(frame * 0.37);
                double x = edgeX + directionX * offset;
                double z = edgeZ + directionZ * offset;
                interest.SetObserver(1UL, new CellCoord((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize)));
                interest.Update(added, removed);
                bool isLoadChanged = added.Count > 0 || removed.Count > 0;
                if (isLoadChanged) loaded.SetCells(new List<CellCoord>(interest.Loaded));
                long lastX = cellX;
                long lastZ = cellZ;
                if (hasHysteresis) layout.FollowKeepOutCell(x, z, ref cellX, ref cellZ);
                else layout.KeepOutCellOf(x, z, out cellX, out cellZ);
                bool isRepick = frame == 0 || (hasStreamedCells && isLoadChanged) || cellX != lastX || cellZ != lastZ || Math.Abs(x - pickedX) + Math.Abs(z - pickedZ) > Slack;
                if (!isRepick) continue;

                pickedX = x;
                pickedZ = z;
                var kept = new List<BackdropFeature>();
                layout.AppendDrawn(x, z, cellX, cellZ, hasStreamedCells ? loaded : null, kept);
                HashSet<string> current = Keys(kept);
                if (previous != null && !current.SetEquals(previous)) changes++;
                previous = current;
            }
            return changes;
        }

        private static Dictionary<string, BackdropFeature> DrawnUnderFog(BackdropLayout layout, double x, double z, long cellX, long cellZ, BackdropKeepOut loaded) {
            var kept = new List<BackdropFeature>();
            layout.AppendDrawn(x, z, cellX, cellZ, loaded, kept);
            var drawn = new Dictionary<string, BackdropFeature>();
            foreach (BackdropFeature feature in kept) {
                if (!layout.IsFogHidden(feature, x, z, GameFog, 1.0, Slack)) drawn[Key(feature)] = feature;
            }
            return drawn;
        }

        private static int CountChanged(BackdropLayout layout, Dictionary<string, BackdropFeature> previous, Dictionary<string, BackdropFeature> current, double x, double z, ref double leastHidden) {
            int changed = 0;
            foreach (KeyValuePair<string, BackdropFeature> entry in previous) {
                if (current.ContainsKey(entry.Key)) continue;

                changed++;
                leastHidden = Math.Min(leastHidden, GameFog.HiddenShare(layout.NearestDistance(entry.Value, x, z), 1.0));
            }
            foreach (KeyValuePair<string, BackdropFeature> entry in current) {
                if (previous.ContainsKey(entry.Key)) continue;

                changed++;
                leastHidden = Math.Min(leastHidden, GameFog.HiddenShare(layout.NearestDistance(entry.Value, x, z), 1.0));
            }
            return changed;
        }

        private static List<(double X, double Z)> Remotes(long cellX, long cellZ, int remoteCells, int count) {
            var remotes = new List<(double X, double Z)> { ((cellX + remoteCells + 0.5) * CellSize, (cellZ + 0.5) * CellSize) };
            if (count > 1) remotes.Add(((cellX - 1.5) * CellSize, (cellZ - remoteCells + 0.3) * CellSize));
            return remotes;
        }

        // The streamer's loaded set for the camera's pawn plus the remote observers.
        private static BackdropKeepOut Loaded(double cameraX, double cameraZ, List<(double X, double Z)> remotes) {
            var interest = new CellInterestSet(LoadRadius, CellInterestSet.DefaultUnloadMargin);
            interest.SetObserver(0UL, CellOf(cameraX, cameraZ));
            for (int index = 0; index < remotes.Count; index++) interest.SetObserver((ulong)index + 1UL, CellOf(remotes[index].X, remotes[index].Z));
            interest.Update(new List<CellCoord>(), new List<CellCoord>());
            var loaded = new BackdropKeepOut(CellSize);
            loaded.SetCells(new List<CellCoord>(interest.Loaded));
            return loaded;
        }

        private static void CheckClearOfLoaded(BackdropFeature feature, BackdropKeepOut loaded, BackdropSettings settings) {
            double gap = loaded.Gap(feature.AbsoluteX(settings.SectorSize), feature.AbsoluteZ(settings.SectorSize)) - feature.Reach;
            Assert.True(gap >= settings.KeepOutMargin - 1e-6, $"a feature reaches {gap:0.0} m from a loaded cell");
        }

        private static CellCoord CellOf(double x, double z) {
            return new CellCoord((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));
        }

        private static HashSet<string> Keys(List<BackdropFeature> features) {
            var keys = new HashSet<string>();
            foreach (BackdropFeature feature in features) keys.Add(Key(feature));
            return keys;
        }

        private static string Key(BackdropFeature feature) {
            return $"{feature.SectorX},{feature.SectorZ},{feature.LocalX:R},{feature.LocalZ:R}";
        }

        // The project's backdrop settings (rock at least 55 m tall).
        private static BackdropSettings ProjectSettings() {
            return new BackdropSettings { MinHeight = 55.0 };
        }

        private static BackdropLayout Layout(ulong seed, BackdropSettings settings) {
            return new BackdropLayout(seed, seed + 1UL, settings, BackdropSilhouette.CloudBankAspects(seed + 1UL, CloudBankShape.Default));
        }
    }
}
