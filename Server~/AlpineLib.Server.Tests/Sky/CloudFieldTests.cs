using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class CloudFieldTests {
        private static readonly CloudFieldSettings Settings = CloudFieldSettings.Default;
        private static readonly CloudPalette Palette = new CloudPalette(
            new SkyAtlasBand(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 0.9f)), new SkyAtlasBand(new Vector2(0.3f, 0.9f), new Vector2(0.4f, 0.9f)));

        [Fact]
        public void SameSeedSameCellsEverywhere() {
            var first = new CloudFieldScatter(21UL, Settings);
            var second = new CloudFieldScatter(21UL, CloudFieldSettings.Default);
            var other = new CloudFieldScatter(22UL, Settings);
            int differing = 0;
            for (long cellZ = -6; cellZ <= 6; cellZ += 2) {
                for (long cellX = -6; cellX <= 6; cellX += 2) {
                    string described = Describe(first.Cell(cellX, cellZ));
                    Assert.Equal(described, Describe(second.Cell(cellX, cellZ)));
                    differing += described != Describe(other.Cell(cellX, cellZ)) ? 1 : 0;
                }
            }
            Assert.True(differing > 40);
            long far = 9_000_000_000L;
            Assert.Equal(Describe(first.Cell(far, -far)), Describe(second.Cell(far, -far)));
        }

        [Fact]
        public void CloudsStayInsideTheirCellAndRanges() {
            var scatter = new CloudFieldScatter(5UL, Settings);
            var classes = new int[CloudFieldSettings.ClassCount];
            for (long cellZ = -12; cellZ < 12; cellZ++) {
                for (long cellX = -12; cellX < 12; cellX++) {
                    foreach (CloudInstance cloud in scatter.Cell(cellX, cellZ)) CheckCloud(cloud, classes);
                }
            }

            int total = classes[0] + classes[1] + classes[2];
            Assert.InRange(classes[0] / (double)total, 0.38, 0.52);
            Assert.InRange(classes[1] / (double)total, 0.28, 0.42);
            Assert.InRange(classes[2] / (double)total, 0.14, 0.26);
        }

        [Fact]
        public void DensityIsOneToThreePer128Metres() {
            var scatter = new CloudFieldScatter(9UL, Settings);
            int clouds = 0;
            int cells = 0;
            for (long cellZ = -15; cellZ < 15; cellZ++) {
                for (long cellX = -15; cellX < 15; cellX++) {
                    clouds += scatter.Cell(cellX, cellZ).Length;
                    cells++;
                }
            }

            double per128 = clouds / (double)cells / (Settings.CellSize * Settings.CellSize / (128.0 * 128.0));
            Assert.InRange(per128, 1.0, 3.0);
        }

        [Fact]
        public void AnchorsAndDrawnCloudsKeepTheirSpacing() {
            var scatter = new CloudFieldScatter(13UL, Settings);
            var anchors = new List<(double X, double Z, CloudInstance Cloud)>();
            for (long cellZ = -4; cellZ < 4; cellZ++) {
                for (long cellX = -4; cellX < 4; cellX++) {
                    foreach (CloudInstance cloud in scatter.Cell(cellX, cellZ)) anchors.Add((cellX * Settings.CellSize + cloud.AnchorX, cellZ * Settings.CellSize + cloud.AnchorZ, cloud));
                }
            }

            double[] times = { 0.0, 37.5, 911.0, 86_400.25 };
            for (int first = 0; first < anchors.Count; first++) {
                for (int second = first + 1; second < anchors.Count; second++) {
                    Assert.True(Distance(anchors[first].X, anchors[first].Z, anchors[second].X, anchors[second].Z) >= Settings.AnchorSpacing - 1e-9);
                    foreach (double time in times) CheckDrawnSpacing(scatter, anchors[first], anchors[second], time);
                }
            }
        }

        [Fact]
        public void WanderIsBoundedAndAlive() {
            var scatter = new CloudFieldScatter(3UL, Settings);
            CloudInstance[] clouds = scatter.Cell(0, 0);
            Assert.NotEmpty(clouds);
            double most = 0.0;
            foreach (CloudInstance cloud in clouds) {
                for (double time = 0.0; time < 3600.0; time += 7.3) {
                    scatter.Wander(cloud, time, out double x, out double z);
                    double length = Math.Sqrt(x * x + z * z);
                    Assert.True(length <= Settings.WanderAmplitude + 1e-9);
                    most = Math.Max(most, length);
                }
            }
            Assert.True(most > Settings.WanderAmplitude * 0.5);

            scatter.Wander(clouds[0], 604_800.5, out double farX, out double farZ);
            Assert.True(Math.Sqrt(farX * farX + farZ * farZ) <= Settings.WanderAmplitude + 1e-9);
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
            // The shipped consumer fogs radially to 400 m at cloud fog scale 0.48: fully fogged before any shrink.
            Assert.True(Settings.FullRadius >= 400.0 / 0.48);
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { WanderAmplitude = 200.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { DissolveShare = 0.0 }));
            Assert.Throws<ArgumentException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { Classes = new CloudClassSettings[2] }));
            Assert.Throws<ArgumentOutOfRangeException>(() => CloudFieldFrame.Locate(0.0, 0.0, WindVector.Zero, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFieldScatter(1UL, new CloudFieldSettings { CellSize = 64.0, MinSpacing = 30.0, WanderAmplitude = 18.0 }));
            new CloudFieldSettings { CellSize = 64.0, MinSpacing = 16.0, WanderAmplitude = 12.0 }.Validate();
        }

        // Far enough for a cloud anywhere in the window, dissolving ones included, but within the shipped 1100 m clip.
        [Fact]
        public void FarClipCoversTheWholeWindow() {
            double slant = Math.Sqrt(Settings.WindowRadius * Settings.WindowRadius + 220.0 * 220.0) + 30.0;
            Assert.True(Settings.RequiredFarClip > slant);
            Assert.InRange(Settings.RequiredFarClip, 1090.0, 1100.0);
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

        [Fact]
        public void HeapsAreRoundFlatBasedAndInBudget() {
            for (ulong seed = 1; seed <= 6; seed++) {
                for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) CheckHeap(seed, meshIndex);
            }
        }

        [Fact]
        public void HeapsAreDeterministic() {
            CloudClassSettings large = Settings.ClassOf(CloudClass.Large);
            ulong variantSeed = CloudHeap.VariantSeed(42UL, CloudClass.Large, 2);
            double diameter = CloudHeap.TargetDiameter(large, Settings, 2);
            FacetMesh first = CloudHeap.Build(large, diameter, variantSeed, Palette, out BlobPuff[] _);
            FacetMesh second = CloudHeap.Build(large, diameter, variantSeed, Palette, out BlobPuff[] _);
            Assert.Equal(first.ComputePositionHash(), second.ComputePositionHash());
            Assert.NotEqual(first.ComputePositionHash(), CloudHeap.Build(large, diameter, CloudHeap.VariantSeed(43UL, CloudClass.Large, 2), Palette, out BlobPuff[] _).ComputePositionHash());
        }

        private static void CheckHeap(ulong seed, int meshIndex) {
            CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(meshIndex);
            int variant = meshIndex % CloudFieldSettings.VariantsPerClass;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double diameter = CloudHeap.TargetDiameter(settings, Settings, variant);
            Assert.InRange(diameter * Settings.MinScale, settings.MinDiameter - 1e-9, settings.MaxDiameter);
            Assert.InRange(diameter * Settings.MaxScale, settings.MinDiameter, settings.MaxDiameter + 1e-9);

            FacetMesh mesh = CloudHeap.Build(settings, diameter, CloudHeap.VariantSeed(seed, cloudClass, variant), Palette, out BlobPuff[] puffs);
            Assert.InRange(mesh.FaceCount, 1, settings.FaceBudget);
            Assert.InRange(puffs.Length, settings.MinPuffs, settings.MaxPuffs);
            FacetBounds bounds = mesh.Bounds;
            Assert.InRange(bounds.Min.Y, -1e-3f, 1e-3f);
            double across = Math.Max(bounds.Size.X, bounds.Size.Z);
            Assert.InRange(across, diameter * 0.8, diameter * 1.15);
            Assert.InRange(bounds.Size.Y / across, 0.3, 0.85);
            foreach (BlobPuff puff in puffs) {
                Assert.Equal(puff.Radii.X, puff.Radii.Z);
                Assert.InRange(puff.Radii.Y / puff.Radii.X, 0.9f, 1.2f);
            }
        }

        private static void CheckWatertight(ulong seed, int meshIndex, Vector3[] directions) {
            CloudClass cloudClass = CloudFieldSettings.ClassOfMesh(meshIndex);
            int variant = meshIndex % CloudFieldSettings.VariantsPerClass;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double diameter = CloudHeap.TargetDiameter(settings, Settings, variant);
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

        private static void CheckCloud(CloudInstance cloud, int[] classes) {
            CloudClassSettings settings = Settings.ClassOf(cloud.Class);
            classes[(int)cloud.Class]++;
            Assert.InRange(cloud.AnchorX, Settings.AnchorInset, Settings.CellSize - Settings.AnchorInset);
            Assert.InRange(cloud.AnchorZ, Settings.AnchorInset, Settings.CellSize - Settings.AnchorInset);
            Assert.InRange(cloud.Altitude, settings.MinAltitude, settings.MaxAltitude);
            Assert.InRange(cloud.Variant, 0, CloudFieldSettings.VariantsPerClass - 1);
            Assert.InRange(cloud.Scale, Settings.MinScale, Settings.MaxScale);
            Assert.InRange(cloud.Yaw, 0.0, 2.0 * Math.PI);
            Assert.InRange(cloud.MeshIndex, 0, CloudFieldSettings.MeshCount - 1);
        }

        private static void CheckDrawnSpacing(CloudFieldScatter scatter, (double X, double Z, CloudInstance Cloud) first, (double X, double Z, CloudInstance Cloud) second, double time) {
            scatter.Wander(first.Cloud, time, out double firstX, out double firstZ);
            scatter.Wander(second.Cloud, time, out double secondX, out double secondZ);
            Assert.True(Distance(first.X + firstX, first.Z + firstZ, second.X + secondX, second.Z + secondZ) >= Settings.MinSpacing - 1e-9);
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
                    .Append(cloud.WanderSeed).Append(';');
            }
            return text.ToString();
        }
    }
}
