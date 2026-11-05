using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class CloudFieldTests {
        private static readonly CloudFieldSettings Settings = CloudFieldSettings.Default;
        private static readonly CloudClass[] Classes = { CloudClass.Small, CloudClass.Medium, CloudClass.Large };
        private const double FarReachShare = 0.65;
        private static readonly CloudPalette Palette = new CloudPalette(
            new SkyAtlasBand(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 0.9f)), new SkyAtlasBand(new Vector2(0.3f, 0.9f), new Vector2(0.4f, 0.9f)));

        [Fact]
        public void SameSeedSameCellsEverywhere() {
            var first = new CloudFieldScatter(21UL, Settings);
            var second = new CloudFieldScatter(21UL, CloudFieldSettings.Default);
            var other = new CloudFieldScatter(22UL, Settings);
            foreach (CloudClass cloudClass in Classes) {
                int differing = 0;
                for (long cellZ = -6; cellZ <= 6; cellZ += 2) {
                    for (long cellX = -6; cellX <= 6; cellX += 2) {
                        string described = Describe(first.Cell(cloudClass, cellX, cellZ));
                        Assert.Equal(described, Describe(second.Cell(cloudClass, cellX, cellZ)));
                        differing += described != Describe(other.Cell(cloudClass, cellX, cellZ)) ? 1 : 0;
                    }
                }
                Assert.True(differing > 20, $"{cloudClass}: only {differing} cells differ between seeds");
                long far = 9_000_000_000L;
                Assert.Equal(Describe(first.Cell(cloudClass, far, -far)), Describe(second.Cell(cloudClass, far, -far)));
            }
        }

        [Fact]
        public void CloudsStayInsideTheirCellAndRanges() {
            var scatter = new CloudFieldScatter(5UL, Settings);
            foreach (CloudClass cloudClass in Classes) {
                for (long cellZ = -8; cellZ < 8; cellZ++) {
                    for (long cellX = -8; cellX < 8; cellX++) {
                        foreach (CloudInstance cloud in scatter.Cell(cloudClass, cellX, cellZ)) CheckCloud(scatter, cloudClass, cloud);
                    }
                }
            }
        }

        // Many small, a few large: about 6 : 3 : 1 per field cell, 1–3 clouds per 128 m overall.
        [Fact]
        public void ClassMixIsManySmallAndAFewLarge() {
            double[] perCell = MeanPerCell(new CloudFieldScatter(9UL, Settings), 15);
            Assert.InRange(perCell[0], 5.0, 8.0);
            Assert.InRange(perCell[1], 2.0, 3.5);
            Assert.InRange(perCell[2], 0.7, 1.4);
            Assert.True(perCell[0] / perCell[2] >= 4.5);

            double per128 = (perCell[0] + perCell[1] + perCell[2]) / (Settings.CellSize * Settings.CellSize / (128.0 * 128.0));
            Assert.InRange(per128, 1.0, 3.0);
        }

        // Drawn footprints spread over more than a decade, classes overlap, scales are log-uniform.
        [Fact]
        public void SizesSpreadWidely() {
            var scatter = new CloudFieldScatter(17UL, Settings);
            var diameters = new List<double>();
            double logScaleSum = 0.0;
            var classLow = new double[CloudFieldSettings.ClassCount];
            var classHigh = new double[CloudFieldSettings.ClassCount];
            for (int index = 0; index < classLow.Length; index++) classLow[index] = double.MaxValue;
            foreach (CloudClass cloudClass in Classes) {
                for (long cellZ = -10; cellZ < 10; cellZ++) {
                    for (long cellX = -10; cellX < 10; cellX++) {
                        foreach (CloudInstance cloud in scatter.Cell(cloudClass, cellX, cellZ)) {
                            double diameter = scatter.MeshDiameter(cloudClass, cloud.Variant) * cloud.Scale;
                            diameters.Add(diameter);
                            logScaleSum += Math.Log(cloud.Scale);
                            classLow[(int)cloudClass] = Math.Min(classLow[(int)cloudClass], diameter);
                            classHigh[(int)cloudClass] = Math.Max(classHigh[(int)cloudClass], diameter);
                        }
                    }
                }
            }

            diameters.Sort();
            double tenth = diameters[diameters.Count / 10];
            double ninetieth = diameters[diameters.Count * 9 / 10];
            Assert.True(diameters[0] < 9.0 && diameters[diameters.Count - 1] > 120.0, $"drawn diameters {diameters[0]:0.0}..{diameters[diameters.Count - 1]:0.0}");
            Assert.True(ninetieth / tenth >= 3.0, $"p10 {tenth:0.0} p90 {ninetieth:0.0}");
            Assert.True(classHigh[0] > classLow[1] && classHigh[1] > classLow[2], "class size bands do not overlap");
            double logMid = 0.5 * (Math.Log(Settings.MinScale) + Math.Log(Settings.MaxScale));
            Assert.InRange(logScaleSum / diameters.Count, logMid - 0.05, logMid + 0.05);
        }

        // Anchors keep their exclusion; at any time and wind heading two clouds of one class keep the gap between footprints.
        [Fact]
        public void SameClassCloudsNeverTouch() {
            var scatter = new CloudFieldScatter(13UL, Settings);
            double[] times = { 0.0, 37.5, 911.0, 86_400.25 };
            var headings = new[] { new WindVector(1.0, 0.0), new WindVector(-0.6, 0.8) };
            foreach (CloudClass cloudClass in Classes) {
                List<(double X, double Z, CloudInstance Cloud)> anchors = Anchors(scatter, cloudClass, 4);
                CloudClassSettings classSettings = Settings.ClassOf(cloudClass);
                for (int first = 0; first < anchors.Count; first++) {
                    for (int second = first + 1; second < anchors.Count; second++) {
                        double exclusion = Settings.Exclusion(classSettings, anchors[first].Cloud.Reach, anchors[second].Cloud.Reach);
                        Assert.True(Distance(anchors[first].X, anchors[first].Z, anchors[second].X, anchors[second].Z) >= exclusion - 1e-9);
                        foreach (double time in times) {
                            foreach (WindVector heading in headings) CheckDrawnGap(scatter, anchors[first], anchors[second], time, heading);
                        }
                    }
                }
            }
        }

        // Bands overlap like a real sky (classes pass through each other); every cloud's top stays under its class's top.
        [Fact]
        public void CloudTopsStayUnderTheirClassTop() {
            Assert.True(Settings.Classes[1].MinAltitude < Settings.Classes[0].MaxAltitude, "bands are expected to overlap");
            var scatter = new CloudFieldScatter(29UL, Settings);
            foreach (CloudClass cloudClass in Classes) {
                CloudClassSettings classSettings = Settings.ClassOf(cloudClass);
                foreach (CloudInstance cloud in scatter.Cell(cloudClass, 3, -2)) {
                    double top = cloud.Altitude + scatter.MeshDiameter(cloudClass, cloud.Variant) * cloud.Scale * classSettings.HeightShare;
                    Assert.True(top <= Settings.TopOf(classSettings) + 1e-9);
                }
            }
        }

        // The largest scale that fits validates, a hair more does not; the shipped one fits.
        [Fact]
        public void MaxScaleThatFitsIsTheValidLimit() {
            double fits = Settings.MaxScaleThatFits();
            Assert.InRange(fits, Settings.MaxScale, 2.0);
            new CloudFieldSettings { MaxScale = fits - 1e-9 }.Validate();
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldSettings { MaxScale = fits + 1e-6 }.Validate());
        }

        // Scales and mesh diameters sit on a 1/1024 grid, so libm differences between runtimes cannot reach them.
        [Fact]
        public void ScalesAndDiametersAreQuantised() {
            var scatter = new CloudFieldScatter(13UL, Settings);
            foreach (CloudClass cloudClass in Classes) {
                for (int variant = 0; variant < CloudFieldSettings.VariantsPerClass; variant++) AssertOnGrid(scatter.MeshDiameter(cloudClass, variant));
                foreach (CloudInstance cloud in scatter.Cell(cloudClass, 1, 4)) {
                    if (cloud.Scale != Settings.MinScale && cloud.Scale != Settings.MaxScale) AssertOnGrid(cloud.Scale);
                }
            }
        }

        // The drawn offset is the tick offsets blended: exact on ticks, between them inside both bounds and continuous.
        [Fact]
        public void SmoothOffsetBlendsTheTicks() {
            var scatter = new CloudFieldScatter(17UL, Settings);
            var wind = new WindField(17UL, WindSettings.Default);
            CloudInstance cloud = scatter.Cell(CloudClass.Large, 0, 0)[0];
            CloudClassSettings large = Settings.ClassOf(CloudClass.Large);
            double previousX = double.NaN;
            double previousZ = double.NaN;
            for (double time = 500.0; time < 520.0; time += 1.0 / 60.0) {
                scatter.SmoothOffset(cloud, time, wind, out double offsetX, out double offsetZ);
                Assert.True(Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ) <= large.MotionReach + 1e-9);
                if (!double.IsNaN(previousX)) Assert.True(Distance(offsetX, offsetZ, previousX, previousZ) < 0.05, $"jump at {time}");
                previousX = offsetX;
                previousZ = offsetZ;
            }

            long tick = CloudFieldScatter.MotionTick(731.125);
            scatter.SmoothOffset(cloud, CloudFieldScatter.TickTime(tick), wind, out double onX, out double onZ);
            scatter.OffsetAtTick(cloud, tick, CloudFieldScatter.HeadingAt(wind, tick), out double tickX, out double tickZ);
            Assert.Equal(tickX, onX, 9);
            Assert.Equal(tickZ, onZ, 9);
            Assert.Equal(2924L, tick);
            Assert.InRange(CloudFieldScatter.TickShare(731.125, tick), 0.49, 0.51);
        }

        // Over an hour: wander and surge stay in bounds, the surge swings at most 15 % of a cell in any 10 minutes and moves.
        [Fact]
        public void WanderAndSurgeAreBoundedAndAlive() {
            var scatter = new CloudFieldScatter(3UL, Settings);
            foreach (CloudClass cloudClass in Classes) {
                CloudInstance[] clouds = scatter.Cell(cloudClass, 0, 0);
                Assert.NotEmpty(clouds);
                CloudClassSettings classSettings = Settings.ClassOf(cloudClass);
                double mostSurge = 0.0;
                foreach (CloudInstance cloud in clouds) mostSurge = Math.Max(mostSurge, CheckMotion(scatter, classSettings, cloud));
                Assert.True(mostSurge > classSettings.SurgeAmplitude * 0.5, $"{cloudClass}: surge peaked at {mostSurge:0.0} m");

                scatter.Wander(clouds[0], 604_800.5, out double farX, out double farZ);
                Assert.True(Math.Sqrt(farX * farX + farZ * farZ) <= classSettings.WanderAmplitude + 1e-9);
                Assert.True(Math.Abs(scatter.Surge(clouds[0], 604_800.5)) <= classSettings.SurgeAmplitude + 1e-9);
            }
        }

        // An hour of real wind at 1 s steps: every cloud's centre stays inside its own field cell of its class's lattice.
        [Fact]
        public void NoCloudLeavesItsFieldCellOverAnHour() {
            var scatter = new CloudFieldScatter(31UL, Settings);
            var wind = new WindField(31UL, WindSettings.Default);
            foreach (CloudClass cloudClass in Classes) {
                var clouds = new List<CloudInstance>();
                for (long cellX = -1; cellX <= 1; cellX++) clouds.AddRange(scatter.Cell(cloudClass, cellX, 2));
                Assert.NotEmpty(clouds);
                for (double time = 0.0; time <= 3600.0; time += 1.0) {
                    foreach (CloudInstance cloud in clouds) {
                        scatter.SmoothOffset(cloud, time + 0.05, wind, out double offsetX, out double offsetZ);
                        Assert.InRange(cloud.AnchorX + offsetX, 0.0, Settings.CellSize - 1e-9);
                        Assert.InRange(cloud.AnchorZ + offsetZ, 0.0, Settings.CellSize - 1e-9);
                    }
                }
            }
        }

        // A cloud's absolute position moves by its class's factor of the drift, its own offset aside; big clouds run slower.
        [Fact]
        public void PerClassDriftFactorsApplied() {
            var scatter = new CloudFieldScatter(7UL, Settings);
            var wind = new WindField(7UL, WindSettings.Default);
            var speeds = new double[CloudFieldSettings.ClassCount];
            foreach (CloudClass cloudClass in Classes) {
                CloudInstance cloud = scatter.Cell(cloudClass, 0, 0)[0];
                double factor = Settings.ClassOf(cloudClass).DriftFactor;
                Assert.Equal(new WindVector(1e6 * factor, -2e5 * factor), scatter.DriftOf(cloudClass, new WindVector(1e6, -2e5)));
                AbsoluteOf(scatter, wind, cloud, 0, 0, 600.0, out double startX, out double startZ, out double startOffsetX, out double startOffsetZ);
                AbsoluteOf(scatter, wind, cloud, 0, 0, 1800.0, out double endX, out double endZ, out double endOffsetX, out double endOffsetZ);
                WindVector drift = wind.Displacement(1800.0) - wind.Displacement(600.0);
                Assert.Equal(drift.X * factor, (endX - endOffsetX) - (startX - startOffsetX), 6);
                Assert.Equal(drift.Z * factor, (endZ - endOffsetZ) - (startZ - startOffsetZ), 6);
                speeds[(int)cloudClass] = Distance(startX, startZ, endX, endZ) / 1200.0;
            }

            Assert.True(speeds[2] < speeds[1] && speeds[1] < speeds[0], $"speeds {speeds[0]:0.00} {speeds[1]:0.00} {speeds[2]:0.00} m/s");
            Assert.InRange(speeds[2] / speeds[0], 0.35, 0.7);
        }

        // Two sessions at the same clock, one stepped frame by frame and one joining cold, place every cloud on the same bits.
        [Fact]
        public void TwoSessionsAtEqualClockPlaceCloudsIdentically() {
            const double clock = 1234.5;
            var stepped = new WindField(55UL, WindSettings.Default);
            for (double time = 0.0; time < clock; time += 1.0 / 60.0) stepped.Displacement(time);
            var cold = new WindField(55UL, WindSettings.Default);
            var first = new CloudFieldScatter(77UL, Settings);
            var second = new CloudFieldScatter(77UL, CloudFieldSettings.Default);
            Assert.Equal(stepped.Displacement(clock), cold.Displacement(clock));
            Assert.Equal(Placements(first, stepped, clock), Placements(second, cold, clock));
            Assert.NotEqual(Placements(first, stepped, clock), Placements(first, stepped, clock + 5.0));
        }

        [Fact]
        public void PresenceDissolvesOnlyOutsideTheFullRadius() {
            var scatter = new CloudFieldScatter(1UL, Settings);
            Assert.Equal(1024.0, Settings.WindowRadius);
            double previous = 1.0;
            for (double distance = 0.0; distance <= Settings.WindowRadius + 50.0; distance += 2.5) {
                double presence = scatter.Presence(distance);
                if (distance <= Settings.FullRadius) Assert.Equal(1.0, presence);
                if (distance >= Settings.WindowRadius) Assert.Equal(0.0, presence);
                Assert.InRange(presence, 0.0, 1.0);
                Assert.True(presence <= previous);
                previous = presence;
            }
            Assert.InRange(scatter.Presence(0.5 * (Settings.FullRadius + Settings.WindowRadius)), 0.3, 0.7);
            // The shipped consumer fogs radially to 750 m at cloud fog scale 0.9: fully fogged before any shrink.
            Assert.True(Settings.FullRadius >= 750.0 / 0.9);
        }

        [Fact]
        public void FrameReducesTheDriftWithoutLosingTheMetres() {
            var drifts = new[] { new WindVector(0.0, 0.0), new WindVector(1e6 + 0.37, -1e6 - 255.9), new WindVector(-3.3e7, 8.1e6) };
            foreach (WindVector drift in drifts) {
                CloudFieldFrame frame = CloudFieldFrame.Locate(1234.5, -987.25, drift, Settings.CellSize);
                Assert.InRange(frame.LocalX, 0.0, Settings.CellSize);
                Assert.InRange(frame.LocalZ, 0.0, Settings.CellSize);
                double windX = frame.CellX * Settings.CellSize + frame.LocalX;
                Assert.Equal(1234.5 - drift.X, windX, 6);
            }
        }

        // A fixed wind anchor seen from a fixed camera moves exactly with the drift, across every cell boundary of a 1e6 m drift.
        [Fact]
        public void OffsetsAreContinuousAcrossAMillionMetreDrift() {
            double cell = Settings.CellSize;
            const double camX = 517.125;
            const double camZ = -77.5;
            const long anchorCellX = -3900;
            const long anchorCellZ = 3907;
            const double anchorX = 100.0;
            const double anchorZ = 40.0;
            double startX = 1e6 - 3.0 * cell - 0.3;
            double startZ = -1e6 + 2.0 * cell + 0.7;
            double previousX = double.NaN;
            double previousZ = double.NaN;
            for (int step = 0; step <= 2000; step++) {
                var drift = new WindVector(startX + step * 0.37, startZ - step * 0.29);
                CloudFieldFrame frame = CloudFieldFrame.Locate(camX, camZ, drift, cell);
                double offsetX = frame.OffsetX(anchorCellX, anchorX);
                double offsetZ = frame.OffsetZ(anchorCellZ, anchorZ);
                Assert.Equal(anchorCellX * cell + anchorX + drift.X - camX, offsetX, 6);
                Assert.Equal(anchorCellZ * cell + anchorZ + drift.Z - camZ, offsetZ, 6);
                if (step > 0) {
                    Assert.Equal(0.37, offsetX - previousX, 6);
                    Assert.Equal(-0.29, offsetZ - previousZ, 6);
                }
                previousX = offsetX;
                previousZ = offsetZ;
            }
        }

        [Fact]
        public void InvalidSettingsThrow() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { DissolveShare = 0.0 }));
            Assert.Throws<ArgumentException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { Classes = new CloudClassSettings[2] }));
            Assert.Throws<ArgumentOutOfRangeException>(() => CloudFieldFrame.Locate(0.0, 0.0, WindVector.Zero, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { CellSize = 128.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { SurgePeriodSeconds = 0.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { Classes = WithSmall(surge: 20.0, driftFactor: 1.1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { Classes = WithSmall(surge: 9.0, driftFactor: 0.0) }));
            new CloudFieldSettings { Classes = WithSmall(surge: 19.0, driftFactor: 1.3) }.Validate();
        }

        // Far enough for a cloud anywhere in the window, dissolving ones included, but within the shipped 1100 m clip.
        [Fact]
        public void FarClipCoversTheWholeWindow() {
            CloudClassSettings large = Settings.ClassOf(CloudClass.Large);
            double fullTop = Settings.FullRadius + Settings.MaxReach(large);
            double slant = Math.Sqrt(fullTop * fullTop + Math.Pow(Settings.TopOf(large), 2));
            Assert.True(Settings.RequiredFarClip >= slant);
            Assert.True(Settings.RequiredFarClip >= Math.Sqrt(Settings.WindowRadius * Settings.WindowRadius + large.MaxAltitude * large.MaxAltitude));
            Assert.InRange(Settings.RequiredFarClip, 1000.0, 1100.0);
        }

        // Every ray out of a point deep inside a puff crosses the heap's surface: no face was dropped where no puff covers it.
        [Fact]
        public void HeapsAreWatertightWherePuffsMeet() {
            Vector3[] directions = RayDirections();
            for (ulong seed = 1; seed <= 4; seed++) {
                for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) CheckWatertight(seed, meshIndex, directions);
            }
        }

        [Fact]
        public void InnerShareBoundsTheFacetedSurface() {
            var puff = new BlobPuff(new Vector3(0f, 5f, 0f), new Vector3(4f, 4.4f, 4f));
            foreach (int level in new[] { 0, 1 }) {
                var spec = new BlobSpec { Subdivisions = level, Radii = puff.Radii, NoiseAmplitude = 0.05f, NoiseFrequency = 1.3f, FacetJitter = 0.02f, Squash = 0.45f };
                FacetMesh blob = new FacetMesh();
                BlobMesh.AppendTo(blob, spec, 7UL, puff.Centre);
                double share = CloudHeap.InnerShare(blob, puff);
                Assert.InRange(share, level == 0 ? 0.6 : 0.8, level == 0 ? 0.85 : 0.97);
            }
        }

        // Round, flat-based, in budget, and inside the reach and height shares the field spaces and stacks clouds by.
        [Fact]
        public void HeapsAreRoundFlatBasedAndInBudget() {
            for (ulong seed = 1; seed <= 24; seed++) {
                for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) CheckHeap(seed, meshIndex);
            }
        }

        [Fact]
        public void HeapsAreDeterministic() {
            CloudClassSettings large = Settings.ClassOf(CloudClass.Large);
            ulong variantSeed = CloudHeap.VariantSeed(42UL, CloudClass.Large, 2);
            double diameter = CloudHeap.TargetDiameter(large, 2);
            FacetMesh first = CloudHeap.Build(large, diameter, variantSeed, Palette, out BlobPuff[] _);
            FacetMesh second = CloudHeap.Build(large, diameter, variantSeed, Palette, out BlobPuff[] _);
            Assert.Equal(first.ComputePositionHash(), second.ComputePositionHash());
            Assert.NotEqual(first.ComputePositionHash(), CloudHeap.Build(large, diameter, CloudHeap.VariantSeed(43UL, CloudClass.Large, 2), Palette, out BlobPuff[] _).ComputePositionHash());
        }

        private static double[] MeanPerCell(CloudFieldScatter scatter, int half) {
            var perCell = new double[CloudFieldSettings.ClassCount];
            int cells = 4 * half * half;
            foreach (CloudClass cloudClass in Classes) {
                int clouds = 0;
                for (long cellZ = -half; cellZ < half; cellZ++) {
                    for (long cellX = -half; cellX < half; cellX++) clouds += scatter.Cell(cloudClass, cellX, cellZ).Length;
                }
                perCell[(int)cloudClass] = clouds / (double)cells;
            }
            return perCell;
        }

        private static List<(double X, double Z, CloudInstance Cloud)> Anchors(CloudFieldScatter scatter, CloudClass cloudClass, int half) {
            var anchors = new List<(double X, double Z, CloudInstance Cloud)>();
            for (long cellZ = -half; cellZ < half; cellZ++) {
                for (long cellX = -half; cellX < half; cellX++) {
                    foreach (CloudInstance cloud in scatter.Cell(cloudClass, cellX, cellZ)) anchors.Add((cellX * Settings.CellSize + cloud.AnchorX, cellZ * Settings.CellSize + cloud.AnchorZ, cloud));
                }
            }
            return anchors;
        }

        // Most surge seen; fails when wander or surge leave their bounds or the surge swings past its share in a 10-minute window.
        private static double CheckMotion(CloudFieldScatter scatter, CloudClassSettings classSettings, CloudInstance cloud) {
            const double window = 600.0;
            const double step = 5.0;
            var surges = new List<double>();
            double most = 0.0;
            for (double time = 0.0; time <= 3600.0; time += step) {
                scatter.Wander(cloud, time, out double wanderX, out double wanderZ);
                Assert.True(Math.Sqrt(wanderX * wanderX + wanderZ * wanderZ) <= classSettings.WanderAmplitude + 1e-9);
                double surge = scatter.Surge(cloud, time);
                Assert.True(Math.Abs(surge) <= classSettings.SurgeAmplitude + 1e-9);
                most = Math.Max(most, Math.Abs(surge));
                surges.Add(surge);
            }

            int span = (int)(window / step);
            for (int start = 0; start + span < surges.Count; start++) {
                double low = double.MaxValue;
                double high = double.MinValue;
                for (int index = start; index <= start + span; index++) {
                    low = Math.Min(low, surges[index]);
                    high = Math.Max(high, surges[index]);
                }
                Assert.True(high - low <= CloudFieldSettings.MaxSurgeShare * Settings.CellSize + 1e-9);
            }
            return most;
        }

        // Absolute position of a cloud seen from a camera at the absolute origin, and its own offset (wander + surge).
        private static void AbsoluteOf(CloudFieldScatter scatter, WindField wind, CloudInstance cloud, long cellX, long cellZ, double time, out double absoluteX, out double absoluteZ, out double offsetX, out double offsetZ) {
            CloudFieldFrame frame = CloudFieldFrame.Locate(0.0, 0.0, scatter.DriftOf(cloud.Class, wind.Displacement(time)), Settings.CellSize);
            WindVector heading = wind.Direction(time);
            scatter.Offset(cloud, time, heading.X, heading.Z, out offsetX, out offsetZ);
            absoluteX = frame.OffsetX(cellX, cloud.AnchorX) + offsetX;
            absoluteZ = frame.OffsetZ(cellZ, cloud.AnchorZ) + offsetZ;
        }

        // Bits of every cloud's absolute position in a 5 x 5 window of each class around a camera at the absolute origin.
        private static string Placements(CloudFieldScatter scatter, WindField wind, double time) {
            var text = new System.Text.StringBuilder();
            foreach (CloudClass cloudClass in Classes) {
                CloudFieldFrame frame = CloudFieldFrame.Locate(3.25, -8.5, scatter.DriftOf(cloudClass, wind.Displacement(time)), Settings.CellSize);
                for (long cellZ = frame.CellZ - 2; cellZ <= frame.CellZ + 2; cellZ++) {
                    for (long cellX = frame.CellX - 2; cellX <= frame.CellX + 2; cellX++) {
                        foreach (CloudInstance cloud in scatter.Cell(cloudClass, cellX, cellZ)) {
                            scatter.SmoothOffset(cloud, time, wind, out double offsetX, out double offsetZ);
                            text.Append(BitConverter.DoubleToInt64Bits(frame.OffsetX(cellX, cloud.AnchorX) + offsetX)).Append(',')
                                .Append(BitConverter.DoubleToInt64Bits(frame.OffsetZ(cellZ, cloud.AnchorZ) + offsetZ)).Append(';');
                        }
                    }
                }
            }
            return text.ToString();
        }

        private static void AssertOnGrid(double value) {
            double steps = value * CloudFieldScatter.QuantumSteps;
            Assert.Equal(Math.Round(steps), steps);
        }

        private static CloudClassSettings[] WithSmall(double surge, double driftFactor) {
            CloudClassSettings[] classes = CloudFieldSettings.Default.Classes;
            CloudClassSettings small = classes[0];
            classes[0] = new CloudClassSettings(small.MinDiameter, small.MaxDiameter, small.MinAltitude, small.MaxAltitude, small.MinPuffs, small.MaxPuffs,
                small.CandidatesPerCell, small.FaceBudget, small.HeightShare, driftFactor, small.WanderAmplitude, surge);
            return classes;
        }

        // The coarse far mesh heaps the same puffs at the coarse level: far fewer faces, the same footprint and height, no hole.
        [Fact]
        public void FarHeapsAreCoarseCopiesOfTheirHeaps() {
            Vector3[] directions = RayDirections();
            foreach (ulong seed in new[] { 1UL, 7UL, 42UL }) {
                for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) CheckFarHeap(seed, meshIndex, directions);
            }
        }

        [Fact]
        public void FarMeshBandKeepsEachCloudsChoice() {
            var settings = new CloudFieldSettings { FarMeshRadius = 1200.0, FarMeshHysteresis = 50.0 };
            Assert.False(settings.IsFarMesh(1199.0));
            Assert.True(settings.IsFarMesh(1201.0));
            Assert.True(settings.IsFarMesh(1160.0, true));
            Assert.False(settings.IsFarMesh(1240.0, false));
            Assert.False(settings.IsFarMesh(1149.0, true));
            Assert.True(settings.IsFarMesh(1251.0, false));
            Assert.False(CloudFieldSettings.Default.IsFarMesh(1e9));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldSettings { FarMeshRadius = 40.0, FarMeshHysteresis = 50.0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldSettings { FarMeshRadius = 1200.0, FarMeshHysteresis = -1.0 }.Validate());

            bool isFar = false;
            int swaps = 0;
            for (int step = 0; step <= 4000; step++) {
                double distance = 1200.0 + 45.0 * Math.Sin(step * 0.05) + 0.01 * step;
                bool next = settings.IsFarMesh(distance, isFar);
                swaps += next != isFar ? 1 : 0;
                isFar = next;
            }
            Assert.Equal(1, swaps);
            Assert.True(isFar, "the drift past the band did not swap the cloud to its far mesh");
        }

        private static void CheckFarHeap(ulong seed, int meshIndex, Vector3[] directions) {
            CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(meshIndex);
            int variant = meshIndex % CloudFieldSettings.VariantsPerClass;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double diameter = CloudHeap.TargetDiameter(settings, variant);
            ulong variantSeed = CloudHeap.VariantSeed(seed, cloudClass, variant);
            FacetMesh fine = CloudHeap.Build(settings, diameter, variantSeed, Palette, out BlobPuff[] puffs);
            FacetMesh far = CloudHeap.BuildFar(settings, diameter, variantSeed, Palette, fine.Bounds.Max.Y);
            Assert.Equal(far.ComputePositionHash(), CloudHeap.BuildFar(settings, diameter, variantSeed, Palette, fine.Bounds.Max.Y).ComputePositionHash());
            string label = $"seed {seed} mesh {meshIndex}";
            Assert.InRange(CloudHeap.CoarseRadiusScale, 1.0, 1.25);
            Assert.True(far.FaceCount * 2 <= fine.FaceCount, $"{label}: far {far.FaceCount} faces vs fine {fine.FaceCount}");
            FacetBounds bounds = far.Bounds;
            FacetBounds fineBounds = fine.Bounds;
            Assert.InRange(bounds.Min.Y, -1e-3f, 1e-3f);
            double reach = 0.0;
            foreach (Vector3 corner in far.Positions) reach = Math.Max(reach, Math.Sqrt(corner.X * corner.X + corner.Z * corner.Z));
            Assert.True(reach <= diameter * FarReachShare, $"{label}: far reach {reach / diameter:0.000} of its diameter");
            Assert.InRange(bounds.Size.Y / fineBounds.Size.Y, 0.9, 1.1);
            Assert.InRange(Math.Max(bounds.Size.X, bounds.Size.Z) / Math.Max(fineBounds.Size.X, fineBounds.Size.Z), 0.9, 1.1);
            foreach (BlobPuff puff in puffs) {
                foreach (Vector3 toward in directions) {
                    Vector3 start = puff.Centre + toward * puff.Radii * 0.5f;
                    if (start.Y < 0.05f) continue;

                    foreach (Vector3 direction in directions) Assert.True(HitsAnyFace(far, start, direction), $"{label}: a ray from {start} along {direction} escapes the far mesh");
                }
            }
        }

        private static void CheckHeap(ulong seed, int meshIndex) {
            CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(meshIndex);
            int variant = meshIndex % CloudFieldSettings.VariantsPerClass;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double diameter = CloudHeap.TargetDiameter(settings, variant);
            Assert.InRange(diameter, settings.MinDiameter - 1e-9, settings.MaxDiameter + 1e-9);

            FacetMesh mesh = CloudHeap.Build(settings, diameter, CloudHeap.VariantSeed(seed, cloudClass, variant), Palette, out BlobPuff[] puffs);
            Assert.InRange(mesh.FaceCount, 1, settings.FaceBudget);
            Assert.InRange(puffs.Length, settings.MinPuffs, settings.MaxPuffs);
            FacetBounds bounds = mesh.Bounds;
            Assert.InRange(bounds.Min.Y, -1e-3f, 1e-3f);
            double across = Math.Max(bounds.Size.X, bounds.Size.Z);
            Assert.InRange(across, diameter * 0.8, diameter * 1.15);
            Assert.True(bounds.Size.Y <= diameter * settings.HeightShare, $"mesh {meshIndex} seed {seed}: height {bounds.Size.Y / diameter:0.000} of its diameter");
            Assert.True(bounds.Size.Y / across >= 0.3, $"mesh {meshIndex} seed {seed}: height {bounds.Size.Y / across:0.000} of its span");
            foreach (Vector3 corner in mesh.Positions) Assert.True(Math.Sqrt(corner.X * corner.X + corner.Z * corner.Z) <= diameter * CloudHeap.ReachShare);
            foreach (BlobPuff puff in puffs) {
                Assert.Equal(puff.Radii.X, puff.Radii.Z);
                Assert.InRange(puff.Radii.Y / puff.Radii.X, 0.9f, 1.2f);
            }
        }

        private static void CheckWatertight(ulong seed, int meshIndex, Vector3[] directions) {
            CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(meshIndex);
            int variant = meshIndex % CloudFieldSettings.VariantsPerClass;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double diameter = CloudHeap.TargetDiameter(settings, variant);
            FacetMesh mesh = CloudHeap.Build(settings, diameter, CloudHeap.VariantSeed(seed, cloudClass, variant), Palette, out BlobPuff[] puffs);
            foreach (BlobPuff puff in puffs) {
                foreach (Vector3 toward in directions) {
                    Vector3 start = puff.Centre + toward * puff.Radii * 0.5f;
                    if (start.Y < 0.05f) continue;

                    foreach (Vector3 direction in directions) {
                        Assert.True(HitsAnyFace(mesh, start, direction), $"seed {seed} mesh {meshIndex}: a ray from {start} along {direction} escapes");
                    }
                }
            }
        }

        // The 26 directions to a cube's faces, edges and corners.
        private static Vector3[] RayDirections() {
            var directions = new List<Vector3>();
            for (int stepX = -1; stepX <= 1; stepX++) {
                for (int stepY = -1; stepY <= 1; stepY++) {
                    for (int stepZ = -1; stepZ <= 1; stepZ++) {
                        if (stepX == 0 && stepY == 0 && stepZ == 0) continue;

                        directions.Add(Vector3.Normalize(new Vector3(stepX + 0.013f, stepY + 0.007f, stepZ - 0.011f)));
                    }
                }
            }
            return directions.ToArray();
        }

        // Möller–Trumbore against every face, in double.
        private static bool HitsAnyFace(FacetMesh mesh, Vector3 origin, Vector3 direction) {
            List<Vector3> corners = mesh.Positions;
            for (int face = 0; face < mesh.FaceCount; face++) {
                if (Hits(origin, direction, corners[face * 3], corners[face * 3 + 1], corners[face * 3 + 2])) return true;
            }
            return false;
        }

        private static bool Hits(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c) {
            Vector3 edgeAb = b - a;
            Vector3 edgeAc = c - a;
            Vector3 cross = Vector3.Cross(direction, edgeAc);
            double determinant = Vector3.Dot(edgeAb, cross);
            if (Math.Abs(determinant) < 1e-9) return false;

            Vector3 toOrigin = origin - a;
            double along = Vector3.Dot(toOrigin, cross) / determinant;
            if (along < -1e-6 || along > 1.0 + 1e-6) return false;

            Vector3 lift = Vector3.Cross(toOrigin, edgeAb);
            double across = Vector3.Dot(direction, lift) / determinant;
            if (across < -1e-6 || along + across > 1.0 + 1e-6) return false;

            return Vector3.Dot(edgeAc, lift) / determinant > 0.0;
        }

        private static void CheckCloud(CloudFieldScatter scatter, CloudClass cloudClass, CloudInstance cloud) {
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double inset = Settings.AnchorInset(settings);
            Assert.Equal(cloudClass, cloud.Class);
            Assert.InRange(cloud.AnchorX, inset, Settings.CellSize - inset);
            Assert.InRange(cloud.AnchorZ, inset, Settings.CellSize - inset);
            Assert.InRange(cloud.Altitude, settings.MinAltitude, settings.MaxAltitude);
            Assert.InRange(cloud.Variant, 0, CloudFieldSettings.VariantsPerClass - 1);
            Assert.InRange(cloud.Scale, Settings.MinScale, Settings.MaxScale);
            Assert.Equal(CloudFieldSettings.ReachOf(scatter.MeshDiameter(cloudClass, cloud.Variant), cloud.Scale), cloud.Reach);
            Assert.InRange(cloud.Yaw, 0.0, 2.0 * Math.PI);
            Assert.InRange(cloud.MeshIndex, (int)cloudClass * CloudFieldSettings.VariantsPerClass, (int)cloudClass * CloudFieldSettings.VariantsPerClass + 3);
        }

        private static void CheckDrawnGap(CloudFieldScatter scatter, (double X, double Z, CloudInstance Cloud) first, (double X, double Z, CloudInstance Cloud) second, double time, WindVector heading) {
            scatter.Offset(first.Cloud, time, heading.X, heading.Z, out double firstX, out double firstZ);
            scatter.Offset(second.Cloud, time, heading.X, heading.Z, out double secondX, out double secondZ);
            double centres = Distance(first.X + firstX, first.Z + firstZ, second.X + secondX, second.Z + secondZ);
            Assert.True(centres >= first.Cloud.Reach + second.Cloud.Reach + Settings.MinGap - 1e-9);
        }

        private static double Distance(double ax, double az, double bx, double bz) {
            double deltaX = ax - bx;
            double deltaZ = az - bz;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static string Describe(CloudInstance[] clouds) {
            var text = new System.Text.StringBuilder();
            foreach (CloudInstance cloud in clouds) {
                text.Append(BitConverter.DoubleToInt64Bits(cloud.AnchorX)).Append(',').Append(BitConverter.DoubleToInt64Bits(cloud.AnchorZ)).Append(',')
                    .Append(cloud.Class).Append(cloud.Variant).Append(',').Append(BitConverter.DoubleToInt64Bits(cloud.Altitude)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(cloud.Scale)).Append(',').Append(cloud.WanderSeed).Append(';');
            }
            return text.ToString();
        }
    }
}
