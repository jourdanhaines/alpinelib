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

        [Fact]
        public void PresenceRampsAcrossTheFadeBands() {
            Assert.Equal(0.0, Settings.Presence(BackdropFeatureKind.Mesa, 400.0));
            Assert.Equal(0.0, Settings.Presence(BackdropFeatureKind.Mesa, 420.0));
            Assert.Equal(0.5, Settings.Presence(BackdropFeatureKind.Mesa, 450.0), 12);
            Assert.Equal(1.0, Settings.Presence(BackdropFeatureKind.Ridge, 620.0));
            Assert.Equal(0.25, Settings.Presence(BackdropFeatureKind.Butte, 805.0), 12);
            Assert.Equal(0.0, Settings.Presence(BackdropFeatureKind.Butte, 820.0));
            Assert.Equal(0.0, Settings.Presence(BackdropFeatureKind.CloudBank, 590.0));
            Assert.Equal(1.0, Settings.Presence(BackdropFeatureKind.CloudBank, 700.0));
            Assert.Equal(0.0, Settings.Presence(BackdropFeatureKind.CloudBank, double.NaN));
        }

        [Fact]
        public void FarClipCoversTheRing() {
            Assert.Equal(940.0, Settings.RequiredFarClip);
            Assert.True(Settings.RequiredFarClip <= 1000.0);
        }

        [Fact]
        public void SettingsRejectInvertedBands() {
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { OuterRadius = 300.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { MaxFeaturesPerSector = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { ParallaxFactor = 1.0 }));
            Assert.Throws<ArgumentNullException>(() => Layout(1UL, 2UL, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(1UL, 2UL, new BackdropSettings { GroundOuterRadius = 300.0 }));
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
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.FrameX(Settings.SectorSize)));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.FrameZ(Settings.SectorSize)));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.Width));
            hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(feature.Height));
            return StableHash.Mix(hash, (ulong)feature.Kind);
        }

        private static BackdropPalette TestPalette() {
            return new BackdropPalette(new SkyAtlasBand(new Vector2(0.1f, 0.9f), new Vector2(0.1f, 0.8f)), new SkyAtlasBand(new Vector2(0.2f, 0.9f), new Vector2(0.2f, 0.8f)),
                new SkyAtlasBand(new Vector2(0.3f, 0.9f), new Vector2(0.3f, 0.8f)), new SkyAtlasBand(new Vector2(0.4f, 0.9f), new Vector2(0.4f, 0.8f)),
                new SkyAtlasBand(new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.8f)));
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
