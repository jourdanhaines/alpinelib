using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class BackdropLayoutTests {
        private const int RockGoldenCount = 24;
        private const ulong RockGoldenHash = 2965748000915924096UL;
        private static readonly BackdropSettings Settings = BackdropSettings.Default;

        [Fact]
        public void SameSeedsSameSectorsEverywhere() {
            var first = Layout(11UL, 12UL, Settings);
            var second = Layout(11UL, 12UL, BackdropSettings.Default);
            for (int sectorZ = -20; sectorZ <= 20; sectorZ += 3) {
                for (int sectorX = -20; sectorX <= 20; sectorX += 3) {
                    Assert.Equal(Describe(first.Sector(sectorX, sectorZ)), Describe(second.Sector(sectorX, sectorZ)));
                }
            }
        }

        [Fact]
        public void OtherSeedsMoveTheSkyline() {
            var layout = Layout(11UL, 12UL, Settings);
            var otherLayout = Layout(13UL, 12UL, Settings);
            var otherLooks = Layout(11UL, 14UL, Settings);
            var features = Around(layout);
            Assert.NotEqual(Describe(features), Describe(Around(otherLayout)));

            BackdropFeature[] relooked = Around(otherLooks);
            Assert.Equal(features.Length, relooked.Length);
            int differentLooks = 0;
            for (int index = 0; index < features.Length; index++) {
                Assert.Equal(features[index].LocalX, relooked[index].LocalX);
                Assert.Equal(features[index].Height, relooked[index].Height);
                Assert.Equal(features[index].Kind, relooked[index].Kind);
                if (features[index].Kind != BackdropFeatureKind.CloudBank) Assert.Equal(features[index].Width, relooked[index].Width);
                differentLooks += features[index].Variant != relooked[index].Variant || features[index].Yaw != relooked[index].Yaw ? 1 : 0;
            }
            Assert.True(differentLooks > features.Length / 2);
        }

        [Fact]
        public void FeaturesStayInsideTheirSectorAndSizeRanges() {
            var layout = Layout(5UL, 6UL, Settings);
            var counts = new int[Settings.MaxFeaturesPerSector + 1];
            var kinds = new int[BackdropSettings.KindCount];
            for (int sectorZ = -30; sectorZ < 30; sectorZ++) {
                for (int sectorX = -30; sectorX < 30; sectorX++) {
                    BackdropFeature[] features = layout.Sector(sectorX, sectorZ);
                    Assert.InRange(features.Length, Settings.MinFeaturesPerSector, Settings.MaxFeaturesPerSector);
                    counts[features.Length]++;
                    foreach (BackdropFeature feature in features) CheckFeature(feature, sectorX, sectorZ, kinds);
                }
            }

            foreach (int count in counts) Assert.True(count > 0);
            foreach (int count in kinds) Assert.True(count > 0);
            double cloudShare = (double)kinds[(int)BackdropFeatureKind.CloudBank] / (kinds[0] + kinds[1] + kinds[2] + kinds[3]);
            Assert.InRange(cloudShare, Settings.CloudBankShare * 0.7, Settings.CloudBankShare * 1.3);
        }

        [Fact]
        public void AroundCoversEverySectorTheSquareTouches() {
            var layout = Layout(5UL, 6UL, Settings);
            var features = new List<BackdropFeature>();
            layout.AppendAround(-1000.0, 333.0, 930.0, features);
            var expected = new List<BackdropFeature>();
            layout.SectorOf(-1930.0, -597.0, out int minX, out int minZ);
            layout.SectorOf(-70.0, 1263.0, out int maxX, out int maxZ);
            for (int sectorZ = minZ; sectorZ <= maxZ; sectorZ++) {
                for (int sectorX = minX; sectorX <= maxX; sectorX++) expected.AddRange(layout.Sector(sectorX, sectorZ));
            }

            Assert.Equal(Describe(expected.ToArray()), Describe(features.ToArray()));
            Assert.Equal(-13, minX);
            Assert.Equal(-1, maxX);
        }

        // Every drawn feature is the sector's own, at its absolute position: what a camera draws never depends on where it is.
        [Fact]
        public void DrawnFeaturesAreWorldFixed() {
            BackdropLayout layout = Layout(5UL, 6UL, Settings);
            var random = new SplitMix64(77UL);
            int drawn = 0;
            for (int trial = 0; trial < 200; trial++) {
                double cameraX = (random.NextDouble() - 0.5) * 2.0e5;
                double cameraZ = (random.NextDouble() - 0.5) * 2.0e5;
                var features = new List<BackdropFeature>();
                layout.AppendDrawn(cameraX, cameraZ, features);
                drawn += features.Count;
                foreach (BackdropFeature feature in features) {
                    string own = Describe(layout.Sector(feature.SectorX, feature.SectorZ));
                    Assert.Contains(Describe(new[] { feature }), own);
                    Assert.Equal(feature.SectorX * Settings.SectorSize + feature.LocalX, feature.AbsoluteX(Settings.SectorSize));
                }
            }

            Assert.True(drawn > 200 * 10, $"only {drawn} features over 200 cameras");
        }

        // From any camera, every part of every drawn feature is at least the clear distance from every point of the
        // camera's cell (so past the loaded 7x7 by the keep-out margin), and its centre within the outer radius.
        [Fact]
        public void NoDrawnFeatureComesNearTheLoadedWorld() {
            BackdropLayout layout = Layout(9UL, 10UL, Settings);
            var random = new SplitMix64(78UL);
            double size = Settings.KeepOutCellSize;
            double least = double.MaxValue;
            for (int trial = 0; trial < 400; trial++) {
                double cameraX = (random.NextDouble() - 0.5) * 2.0e5;
                double cameraZ = (random.NextDouble() - 0.5) * 2.0e5;
                var features = new List<BackdropFeature>();
                layout.AppendDrawn(cameraX, cameraZ, features);
                layout.KeepOutCellOf(cameraX, cameraZ, out long cellX, out long cellZ);
                foreach (BackdropFeature feature in features) {
                    double x = feature.AbsoluteX(Settings.SectorSize);
                    double z = feature.AbsoluteZ(Settings.SectorSize);
                    double fromCell = Gap(x, cellX * size, (cellX + 1) * size, z, cellZ * size, (cellZ + 1) * size) - feature.Reach;
                    double fromWindow = layout.KeepOutGap(x, z, cellX, cellZ) - feature.Reach;
                    least = Math.Min(least, fromCell);
                    Assert.True(fromCell >= Settings.ClearDistance - 1e-6, $"a feature reaches {fromCell:0.0} m from the camera's cell");
                    Assert.True(fromWindow >= Settings.KeepOutMargin - 1e-6, $"a feature reaches {fromWindow:0.0} m from the loaded window");
                    Assert.True(Math.Sqrt((x - cameraX) * (x - cameraX) + (z - cameraZ) * (z - cameraZ)) <= Settings.OuterRadius);
                }
            }

            Assert.InRange(least, Settings.ClearDistance, Settings.ClearDistance + 40.0);
        }

        [Fact]
        public void RepeatQueriesAgree() {
            var features = new List<BackdropFeature>();
            var again = new List<BackdropFeature>();
            var fresh = new List<BackdropFeature>();
            Layout(3UL, 4UL, Settings).AppendDrawn(-40123.5, 9876.25, features);
            BackdropLayout layout = Layout(3UL, 4UL, BackdropSettings.Default);
            layout.AppendDrawn(-40123.5, 9876.25, again);
            layout.AppendDrawn(-40123.5, 9876.25, fresh);
            Assert.NotEmpty(features);
            Assert.Equal(Describe(features.ToArray()), Describe(again.ToArray()));
            Assert.Equal(Describe(features.ToArray()), Describe(fresh.ToArray()));
        }

        // A 500 m walk in quarter-metre steps: features join or leave only as the camera crosses a cell (then at least
        // the clear distance away) or as their centre crosses the outer radius (in full fog), and none comes nearer.
        [Theory]
        [InlineData(0.0, 1.0, 0.0)]
        [InlineData(37.0, 0.8, 0.6)]
        [InlineData(-5000.5, -0.6, 0.8)]
        public void MembershipChangesOnlyAcrossCellsOrAtTheOuterEdge(double startX, double directionX, double directionZ) {
            BackdropLayout layout = Layout(21UL, 22UL, Settings);
            const double step = 0.25;
            var previous = DrawnKeys(layout, startX, 64.0);
            layout.KeepOutCellOf(startX, 64.0, out long previousX, out long previousZ);
            int joins = 0;
            for (int index = 1; index <= 2000; index++) {
                double x = startX + directionX * step * index;
                double z = 64.0 + directionZ * step * index;
                layout.KeepOutCellOf(x, z, out long cellX, out long cellZ);
                bool isCrossing = cellX != previousX || cellZ != previousZ;
                var current = DrawnKeys(layout, x, z);
                joins += CheckChanges(previous, current, x, z, isCrossing, step);
                CheckClear(current, x, z);
                previous = current;
                previousX = cellX;
                previousZ = cellZ;
            }

            Assert.True(joins > 0, "a 500 m walk changed nothing");
        }

        [Fact]
        public void FarClipCoversTheRing() {
            Assert.Equal(1050.0, Settings.RequiredFarClip);
            Assert.True(Settings.RequiredFarClip <= 1100.0);
            Assert.Equal(316.0, Settings.KeepOutMargin, 12);
            Assert.True(Settings.GroundOuterRadius - Settings.GroundLatticeSnap * Math.Sqrt(0.5) >= Settings.OuterRadius);
        }

        [Fact]
        public void SettingsRejectInvertedBands() {
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { OuterRadius = 650.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { ClearDistance = 300.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { KeepOutRadiusCells = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { MaxFeaturesPerSector = -1 }));
            Assert.Throws<ArgumentNullException>(() => Layout(1UL, 2UL, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { GroundOuterRadius = 300.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { GroundOuterRadius = 1060.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { CloudBankWidthJitter = 1.0 }));
            Assert.Throws<ArgumentNullException>(() => new BackdropLayout(1UL, 2UL, Settings, null));
            Assert.Throws<ArgumentException>(() => new BackdropLayout(1UL, 2UL, Settings, new[] { 4.0, 4.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackdropLayout(1UL, 2UL, Settings, new[] { 4.0, 0.0, 4.0, 4.0 }));
        }

        // A bank is its mesh's own aspect wide and about its height deep, so a puff built round stays round in the world.
        [Fact]
        public void CloudBanksScaleByTheirMeshAspectSoPuffsStayRound() {
            BackdropLayout layout = Layout(5UL, 6UL, Settings);
            int banks = 0;
            for (int sectorZ = -30; sectorZ < 30; sectorZ++) {
                for (int sectorX = -30; sectorX < 30; sectorX++) {
                    foreach (BackdropFeature feature in layout.Sector(sectorX, sectorZ)) {
                        if (feature.Kind != BackdropFeatureKind.CloudBank) continue;

                        banks++;
                        double across = feature.Width / (feature.Height * layout.CloudBankAspect(feature.Variant));
                        Assert.InRange(across, 1.0 - Settings.CloudBankWidthJitter - 1e-9, 1.0 + Settings.CloudBankWidthJitter + 1e-9);
                        Assert.InRange(feature.Depth / feature.Height, 1.0 - Settings.CloudBankDepthJitter - 1e-9, 1.0 + Settings.CloudBankDepthJitter + 1e-9);
                    }
                }
            }

            Assert.True(banks > 100, $"only {banks} banks");
        }

        // The shipped heap is a long low bank (at least 4:1 before jitter) that fits the far clip uncapped.
        [Fact]
        public void ShippedCloudBanksAreLongAndFitTheFarClip() {
            double widest = Settings.CloudBankMaxAspect / (1.0 + Settings.CloudBankWidthJitter);
            for (ulong seed = 1; seed <= 64; seed++) {
                foreach (double aspect in BackdropSilhouette.CloudBankAspects(seed, CloudBankShape.Default)) Assert.InRange(aspect, 4.0, widest);
            }
        }

        // Rock placement and size are pinned for one seed: a change here moves every player's skyline.
        [Fact]
        public void RockPlacementGolden() {
            BackdropLayout layout = Layout(42UL, 43UL, Settings);
            ulong hash = 0x524F434BUL;
            int rocks = 0;
            for (int sectorX = 0; rocks < RockGoldenCount; sectorX++) {
                foreach (BackdropFeature feature in layout.Sector(sectorX, -3)) {
                    if (feature.Kind == BackdropFeatureKind.CloudBank || rocks >= RockGoldenCount) continue;

                    hash = MixRock(hash, feature);
                    rocks++;
                }
            }

            Assert.Equal(RockGoldenHash, hash);
        }

        [Fact]
        public void SilhouettesAreUnitSizedPaintedAndRepeatable() {
            BackdropPalette palette = TestPalette();
            for (int kind = 0; kind < BackdropSettings.KindCount; kind++) {
                for (int variant = 0; variant < BackdropSettings.VariantsPerKind; variant++) {
                    var featureKind = (BackdropFeatureKind)kind;
                    FacetMesh mesh = BackdropSilhouette.Build(featureKind, variant, 99UL, palette);
                    FacetMesh again = BackdropSilhouette.Build(featureKind, variant, 99UL, palette);
                    Assert.Equal(mesh.ComputePositionHash(), again.ComputePositionHash());
                    int most = featureKind == BackdropFeatureKind.CloudBank ? CloudBankShape.Default.FaceBudget : 260;
                    Assert.True(mesh.FaceCount >= 30 && mesh.FaceCount <= most, $"{featureKind} {variant}: {mesh.FaceCount} faces");
                    FacetBounds bounds = mesh.Bounds;
                    Assert.InRange(bounds.Min.X, -0.5f - 1e-5f, -0.3f);
                    Assert.InRange(bounds.Max.X, 0.3f, 0.5f + 1e-5f);
                    Assert.InRange(bounds.Min.Y, -1e-5f, 1e-5f);
                    Assert.InRange(bounds.Max.Y, 1f - 1e-5f, 1f + 1e-5f);
                    Assert.True(bounds.Max.Z <= (featureKind == BackdropFeatureKind.CloudBank ? 1f : 0.5f * 0.61f), $"{featureKind} {variant}: z reaches {bounds.Max.Z}");
                    CheckPainted(mesh, featureKind, variant, palette);
                }
            }

            Assert.NotEqual(BackdropSilhouette.Build(BackdropFeatureKind.Mesa, 0, 99UL, palette).ComputePositionHash(),
                BackdropSilhouette.Build(BackdropFeatureKind.Mesa, 1, 99UL, palette).ComputePositionHash());
        }

        [Theory]
        [InlineData(6, 0)]
        [InlineData(7, 2)]
        [InlineData(10, 0)]
        [InlineData(8, 2)]
        public void CloudBanksHeapRoundPuffsWithinTheBudget(int rowPuffs, int crownPuffs) {
            var shape = new CloudBankShape { RowPuffs = rowPuffs, CrownPuffs = crownPuffs };
            for (int variant = 0; variant < BackdropSettings.VariantsPerKind; variant++) {
                ulong seed = BackdropSilhouette.VariantSeed(5UL, BackdropFeatureKind.CloudBank, variant);
                FacetMesh mesh = BackdropSilhouette.BuildCloudBank(seed, TestPalette(), shape, out double aspect);
                Assert.Equal(BackdropSilhouette.CloudBankAspects(5UL, shape)[variant], aspect);
                Assert.InRange(mesh.FaceCount, (rowPuffs + crownPuffs) * 20, shape.FaceBudget);
                Assert.True(mesh.FaceCount >= 80 * 4, $"{mesh.FaceCount} faces: too few round puffs");
                FacetBounds bounds = mesh.Bounds;
                Assert.InRange(bounds.Min.Y, -1e-5f, 1e-5f);
                Assert.InRange(bounds.Max.Y, 1f - 1e-5f, 1f + 1e-5f);
                Assert.InRange(bounds.Max.X - bounds.Min.X, 1f - 1e-5f, 1f + 1e-5f);
                CheckPainted(mesh, BackdropFeatureKind.CloudBank, variant, TestPalette());
            }
        }

        [Fact]
        public void CloudBankBellyIsItsUndersideAndTopIsItsTopStrip() {
            BackdropPalette palette = TestPalette();
            FacetMesh mesh = BackdropSilhouette.Build(BackdropFeatureKind.CloudBank, 1, 3UL, palette);
            for (int face = 0; face < mesh.FaceCount; face++) {
                Vector2 uv = mesh.Uvs[face * 3];
                bool isTop = mesh.FaceCentroid(face).Y >= BackdropSilhouette.CloudSplit;
                Assert.True(isTop ? OnBand(uv, palette.CloudTop) : OnBand(uv, palette.CloudUnderside), $"face {face} at {mesh.FaceCentroid(face).Y} is on the wrong strip");
            }
        }

        [Fact]
        public void GroundAnnulusFacesUpAcrossItsBand() {
            var settings = new BackdropSettings();
            FacetMesh mesh = BackdropSilhouette.BuildGround(settings, TestPalette());
            Assert.Equal(BackdropSilhouette.GroundSegments * 2, mesh.FaceCount);
            for (int face = 0; face < mesh.FaceCount; face++) Assert.True(mesh.FaceNormal(face).Y > 0.99f, $"face {face} faces {mesh.FaceNormal(face)}");
            foreach (Vector3 position in mesh.Positions) {
                float radius = new Vector2(position.X, position.Z).Length();
                Assert.InRange(radius, (float)settings.GroundInnerRadius - 1e-3f, (float)settings.GroundOuterRadius + 1e-3f);
                Assert.Equal(0f, position.Y);
            }
        }

        // The lattice's anchor sits up to half a snap diagonal from the camera, so its hole shrinks by that much.
        [Fact]
        public void GroundLatticeHoleNeverOpensPastTheAnnulus() {
            BackdropSettings settings = BackdropSettings.Default;
            Assert.Equal(settings.GroundInnerRadius - settings.GroundLatticeSnap * Math.Sqrt(0.5), settings.GroundLatticeHoleRadius, 12);
            Assert.True(settings.GroundLatticeHoleRadius > 0.0);

            settings.GroundLatticeSnap = settings.GroundInnerRadius * 2.0;
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
        }

        [Fact]
        public void RockFootFogGivesWaySmoothlyToItsOwn() {
            Assert.Equal(1.0, BackdropSilhouette.FootFogWeight(0.0));
            Assert.Equal(1.0, BackdropSilhouette.FootFogWeight(-0.04));
            Assert.Equal(0.5, BackdropSilhouette.FootFogWeight(BackdropSilhouette.FootFogShare * 0.5), 12);
            Assert.Equal(0.0, BackdropSilhouette.FootFogWeight(BackdropSilhouette.FootFogShare));
            Assert.Equal(0.0, BackdropSilhouette.FootFogWeight(1.0));
            double previous = 1.0;
            for (double height = 0.0; height <= 1.0; height += 0.005) {
                double weight = BackdropSilhouette.FootFogWeight(height);
                Assert.True(weight <= previous && previous - weight < 0.03, $"weight jumps {previous} -> {weight} at {height}");
                previous = weight;
            }
        }

        [Fact]
        public void RockIsDarkestAtItsFoot() {
            BackdropPalette palette = TestPalette();
            FacetMesh mesh = BackdropSilhouette.Build(BackdropFeatureKind.Butte, 0, 3UL, palette);
            int lowest = 0;
            int highest = 0;
            for (int face = 1; face < mesh.FaceCount; face++) {
                if (mesh.FaceCentroid(face).Y < mesh.FaceCentroid(lowest).Y) lowest = face;
                if (mesh.FaceCentroid(face).Y > mesh.FaceCentroid(highest).Y) highest = face;
            }

            Assert.True(mesh.Uvs[lowest * 3].Y < mesh.Uvs[highest * 3].Y);
        }

        private static void CheckFeature(BackdropFeature feature, int sectorX, int sectorZ, int[] kinds) {
            Assert.Equal(sectorX, feature.SectorX);
            Assert.Equal(sectorZ, feature.SectorZ);
            Assert.InRange(feature.LocalX, 0.0, Settings.SectorSize);
            Assert.InRange(feature.LocalZ, 0.0, Settings.SectorSize);
            Assert.InRange(feature.Variant, 0, BackdropSettings.VariantsPerKind - 1);
            Assert.InRange(feature.Yaw, 0.0, 2.0 * Math.PI);
            Assert.InRange(feature.MeshIndex, 0, BackdropSettings.MeshCount - 1);
            kinds[(int)feature.Kind]++;
            if (feature.Kind == BackdropFeatureKind.CloudBank) {
                Assert.InRange(feature.Altitude, Settings.CloudBankMinAltitude, Settings.CloudBankMaxAltitude);
                Assert.InRange(feature.Height, Settings.CloudBankMinHeight, Settings.CloudBankMaxHeight);
                Assert.InRange(feature.Width / feature.Height, 3.6, Settings.CloudBankMaxAspect + 1e-9);
                return;
            }
            Assert.InRange(feature.Width, Settings.MinWidth, Settings.MaxWidth);
            Assert.True(feature.Width <= feature.Height * Settings.MaxRockAspect + 1e-9, $"rock {feature.Width:0.0} m wide on {feature.Height:0.0} m is a plank");
            Assert.Equal(feature.Width, feature.Depth);
            Assert.Equal(0.0, feature.Altitude);
            Assert.InRange(feature.Height, Settings.MinHeight, Settings.MaxHeight);
        }

        private static void CheckPainted(FacetMesh mesh, BackdropFeatureKind kind, int variant, BackdropPalette palette) {
            foreach (Vector2 uv in mesh.Uvs) {
                bool isCloud = kind == BackdropFeatureKind.CloudBank;
                bool onBand = isCloud ? OnBand(uv, palette.CloudTop) || OnBand(uv, palette.CloudUnderside) : OnBand(uv, palette.RockFor(variant));
                Assert.True(onBand, $"{kind} {variant}: uv {uv} is off its strip");
            }
        }

        private static bool OnBand(Vector2 uv, SkyAtlasBand band) {
            float low = Math.Min(band.Light.Y, band.Dark.Y) - 1e-6f;
            float high = Math.Max(band.Light.Y, band.Dark.Y) + 1e-6f;
            return Math.Abs(uv.X - band.Light.X) < 1e-6f && uv.Y >= low && uv.Y <= high;
        }

        private static BackdropLayout Layout(ulong layoutSeed, ulong variantSeed, BackdropSettings settings) {
            return new BackdropLayout(layoutSeed, variantSeed, settings, BackdropSilhouette.CloudBankAspects(variantSeed, CloudBankShape.Default));
        }

        private static ulong MixRock(ulong hash, BackdropFeature feature) {
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.AbsoluteX(Settings.SectorSize)));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.AbsoluteZ(Settings.SectorSize)));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.Width));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.Height));
            return StableHash.Mix(hash, (ulong)feature.Kind);
        }

        private static BackdropPalette TestPalette() {
            return new BackdropPalette(new SkyAtlasBand(new Vector2(0.1f, 0.9f), new Vector2(0.1f, 0.8f)), new SkyAtlasBand(new Vector2(0.2f, 0.9f), new Vector2(0.2f, 0.8f)),
                new SkyAtlasBand(new Vector2(0.3f, 0.9f), new Vector2(0.3f, 0.8f)), new SkyAtlasBand(new Vector2(0.4f, 0.9f), new Vector2(0.4f, 0.8f)),
                new SkyAtlasBand(new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.8f)));
        }

        private static Dictionary<string, BackdropFeature> DrawnKeys(BackdropLayout layout, double cameraX, double cameraZ) {
            var features = new List<BackdropFeature>();
            layout.AppendDrawn(cameraX, cameraZ, features);
            var keys = new Dictionary<string, BackdropFeature>();
            foreach (BackdropFeature feature in features) keys.Add(Describe(new[] { feature }), feature);
            return keys;
        }

        // Each join or leave between two steps: on a cell crossing it is at least the clear distance away, else its centre is at the outer radius.
        private static int CheckChanges(Dictionary<string, BackdropFeature> previous, Dictionary<string, BackdropFeature> current, double cameraX, double cameraZ, bool isCrossing, double step) {
            int changes = 0;
            foreach (KeyValuePair<string, BackdropFeature> entry in current) {
                if (previous.ContainsKey(entry.Key)) continue;

                changes++;
                CheckChange(entry.Value, cameraX, cameraZ, isCrossing, step);
            }
            foreach (KeyValuePair<string, BackdropFeature> entry in previous) {
                if (current.ContainsKey(entry.Key)) continue;

                changes++;
                CheckChange(entry.Value, cameraX, cameraZ, isCrossing, step);
            }
            return changes;
        }

        private static void CheckChange(BackdropFeature feature, double cameraX, double cameraZ, bool isCrossing, double step) {
            double centre = Distance(feature, cameraX, cameraZ);
            bool isAtOuterEdge = Math.Abs(centre - Settings.OuterRadius) <= step + 1e-9;
            Assert.True(isAtOuterEdge || isCrossing, $"a feature {centre:0.0} m away changed inside a cell");
            Assert.True(centre - feature.Reach >= Settings.ClearDistance - step - 1e-9, $"a feature changed only {centre - feature.Reach:0.0} m away");
        }

        private static void CheckClear(Dictionary<string, BackdropFeature> current, double cameraX, double cameraZ) {
            foreach (BackdropFeature feature in current.Values) {
                double nearest = Distance(feature, cameraX, cameraZ) - feature.Reach;
                Assert.True(nearest >= Settings.ClearDistance - 1e-6, $"a drawn feature reaches {nearest:0.0} m from the camera");
            }
        }

        private static double Distance(BackdropFeature feature, double cameraX, double cameraZ) {
            double deltaX = feature.AbsoluteX(Settings.SectorSize) - cameraX;
            double deltaZ = feature.AbsoluteZ(Settings.SectorSize) - cameraZ;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static double Gap(double x, double minX, double maxX, double z, double minZ, double maxZ) {
            double gapX = x < minX ? minX - x : x > maxX ? x - maxX : 0.0;
            double gapZ = z < minZ ? minZ - z : z > maxZ ? z - maxZ : 0.0;
            return Math.Sqrt(gapX * gapX + gapZ * gapZ);
        }

        private static BackdropFeature[] Around(BackdropLayout layout) {
            var features = new List<BackdropFeature>();
            layout.AppendAround(12345.0, -6789.0, 930.0, features);
            return features.ToArray();
        }

        private static string Describe(BackdropFeature[] features) {
            var parts = new List<string>();
            foreach (BackdropFeature feature in features) {
                parts.Add($"{feature.SectorX},{feature.SectorZ},{BitConverter.DoubleToInt64Bits(feature.LocalX)},{BitConverter.DoubleToInt64Bits(feature.LocalZ)},{feature.Kind},{feature.Variant},{BitConverter.DoubleToInt64Bits(feature.Yaw)},{feature.Width},{feature.Height},{feature.Depth},{feature.Altitude}");
            }
            return string.Join("|", parts);
        }
    }
}
