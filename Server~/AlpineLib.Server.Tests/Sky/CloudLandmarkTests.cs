using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;
using Xunit.Abstractions;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class CloudLandmarkTests {
        private static readonly CloudClass[] Classes = { CloudClass.Small, CloudClass.Medium, CloudClass.Large };
        private readonly ITestOutputHelper _output;

        public CloudLandmarkTests(ITestOutputHelper output) {
            _output = output;
        }

        // Same seed, same latent and regular clouds, bit for bit; latent clouds come after the regular ones and only with landmarks.
        [Fact]
        public void LatentCellsAreDeterministic() {
            var first = new CloudFieldScatter(21UL, LandmarkSettings());
            var second = new CloudFieldScatter(21UL, LandmarkSettings());
            var plain = new CloudFieldScatter(21UL, CloudFieldSettings.Default);
            int latent = 0;
            foreach (CloudClass cloudClass in Classes) {
                for (long cellZ = -4; cellZ <= 4; cellZ++) {
                    for (long cellX = -4; cellX <= 4; cellX++) {
                        CloudInstance[] clouds = first.Cell(cloudClass, cellX, cellZ);
                        Assert.Equal(Describe(clouds), Describe(second.Cell(cloudClass, cellX, cellZ)));
                        latent += CheckLatentOrder(clouds);
                        foreach (CloudInstance cloud in plain.Cell(cloudClass, cellX, cellZ)) Assert.False(cloud.IsLatent);
                    }
                }
            }
            Assert.True(latent > 50, $"only {latent} latent clouds");
        }

        // Away from giants no latent cloud shows and none grows; the envelope rises smoothly with the bias (no step past its slope).
        [Fact]
        public void EnvelopeIsZeroInOpenSkyAndContinuous() {
            var scatter = new CloudFieldScatter(5UL, LandmarkSettings());
            CloudLandmarkSettings landmarks = scatter.Settings.Landmarks;
            int rising = 0;
            foreach (CloudClass cloudClass in Classes) {
                double slope = 1.5 / landmarks.EnvelopeWidth * landmarks.WeightOf(cloudClass);
                foreach (CloudInstance cloud in scatter.Cell(cloudClass, 0, 0)) {
                    Assert.Equal(cloud.IsLatent ? 0.0 : 1.0, scatter.Envelope(cloud, 0.0));
                    if (!cloud.IsLatent) Assert.Equal(1.0, scatter.SizeBoost(cloud, 0.6));
                    Assert.InRange(cloud.BaseDensity, scatter.Settings.MinCoverage, 1.0);
                    rising += CheckEnvelope(scatter, cloud, slope);
                }
            }
            Assert.True(rising > 0, "no latent cloud condensed at full bias");
        }

        // The regular clouds are the approved plain sky bit for bit (golden from the scatter before landmarks), with landmarks on or off.
        [Fact]
        public void OpenSkyIsThePlainScatter() {
            ulong[] seeds = { 1UL, 7UL, 42UL };
            ulong[] goldens = { 0xF3C2B67A25A75F09UL, 0xD1FE1B393DF0E523UL, 0xA8CFB5F479A7C669UL };
            for (int index = 0; index < seeds.Length; index++) {
                Assert.Equal(goldens[index], CloudFieldFingerprint.Of(new CloudFieldScatter(seeds[index], CloudFieldSettings.Default), 6));
                Assert.Equal(goldens[index], CloudFieldFingerprint.Of(new CloudFieldScatter(seeds[index], LandmarkSettings()), 6));
            }
        }

        // Regular pairs keep the full gap at any time and heading; a latent cloud keeps the latent share of the exclusion
        // (at its largest drawn size) from every cloud of its class.
        [Fact]
        public void LatentCloudsKeepTheirSpacing() {
            var scatter = new CloudFieldScatter(13UL, LandmarkSettings());
            CloudFieldSettings settings = scatter.Settings;
            double[] times = { 0.0, 37.5, 911.0, 86_400.25 };
            var headings = new[] { new WindVector(1.0, 0.0), new WindVector(-0.6, 0.8) };
            foreach (CloudClass cloudClass in Classes) {
                Assert.InRange(scatter.LargestBoost(cloudClass), 1.0, 1.0 + settings.Landmarks.SizeBoost * settings.Landmarks.GiantBias);
                List<(double X, double Z, CloudInstance Cloud)> anchors = Anchors(scatter, cloudClass, 4);
                for (int first = 0; first < anchors.Count; first++) {
                    for (int second = first + 1; second < anchors.Count; second++) CheckPair(scatter, anchors[first], anchors[second], times, headings);
                }
            }
        }

        private static void CheckPair(CloudFieldScatter scatter, (double X, double Z, CloudInstance Cloud) first, (double X, double Z, CloudInstance Cloud) second, double[] times, WindVector[] headings) {
            if (first.Cloud.IsLatent || second.Cloud.IsLatent) {
                CloudClassSettings classSettings = scatter.Settings.ClassOf(first.Cloud.Class);
                double boost = scatter.LatentReachScale(first.Cloud.Class);
                double firstReach = first.Cloud.Reach * (first.Cloud.IsLatent ? boost : 1.0);
                double secondReach = second.Cloud.Reach * (second.Cloud.IsLatent ? boost : 1.0);
                double exclusion = scatter.Settings.Exclusion(classSettings, firstReach, secondReach) * scatter.Settings.Landmarks.LatentSpacing;
                double deltaX = first.X - second.X;
                double deltaZ = first.Z - second.Z;
                Assert.True(Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ) >= exclusion - 1e-9, "latent clouds packed past their share");
                return;
            }
            foreach (double time in times) {
                foreach (WindVector heading in headings) CheckGap(scatter, first, second, 1.0, time, heading);
            }
        }

        // A shipped-size field at its largest scale validates with landmarks: the cap keeps the largest pair inside a cell.
        [Fact]
        public void BoostCapFitsTheCell() {
            CloudFieldSettings settings = LandmarkSettings();
            settings.MaxScale = settings.MaxScaleThatFits();
            settings.Validate();
            foreach (CloudClassSettings cloudClass in settings.Classes) {
                double widest = settings.MaxReach(cloudClass) * settings.BoostCap(cloudClass);
                Assert.True(settings.Exclusion(cloudClass, widest, widest) <= settings.CellSize + 1e-9);
            }
            Assert.Equal(1.0, CloudFieldSettings.Default.BoostCap(CloudFieldSettings.Default.Classes[2]));
        }

        // Dissolve: gone over the hull and within 20 m of it, smoothstepped back to whole 180 m out, symmetric, only for a
        // cloud whose base is below the top + 5 m (any shape that tall blocks it, mesas too); the nearest blocking hull wins.
        [Fact]
        public void CloudsDissolveNearABlockingHull() {
            var influence = new CloudLandmarkInfluence(CloudLandmarkSettings.Default);
            var shapes = new List<LandmarkSkyShape> { Giant(0.0, 0.0, 150.0, 120.0) };
            Assert.Equal(0.0, influence.Dissolve(shapes, 0.0, 0.0, 10.0, 140.0));
            Assert.Equal(0.0, influence.Dissolve(shapes, 120.0 + 10.0 + 20.0, 0.0, 10.0, 140.0), 9);
            Assert.Equal(1.0, influence.Dissolve(shapes, 120.0 + 10.0 + 181.0, 0.0, 10.0, 140.0));
            Assert.Equal(1.0, influence.Dissolve(shapes, 0.0, 0.0, 10.0, 155.0));
            Assert.Equal(0.0, influence.Dissolve(shapes, 0.0, 0.0, 10.0, 154.9));
            Assert.Equal(influence.Dissolve(shapes, 230.0, 0.0, 10.0, 140.0), influence.Dissolve(shapes, -230.0, 0.0, 10.0, 140.0), 6);
            var mesa = new List<LandmarkSkyShape> { Mesa(0.0, 0.0, 90.0, 50.0) };
            Assert.Equal(0.0, influence.Dissolve(mesa, 0.0, 0.0, 10.0, 94.0));
            Assert.Equal(1.0, influence.Dissolve(mesa, 0.0, 0.0, 10.0, 120.0));
            double previous = 0.0;
            for (double x = 120.0; x < 400.0; x += 0.5) {
                double share = influence.Dissolve(shapes, x, 0.0, 10.0, 140.0);
                Assert.True(share >= previous - 1e-9 && share - previous < 0.006, $"dissolve step at {x}: {previous} -> {share}");
                previous = share;
            }
        }

        // The bias zone: full within 400 m of the anchor and 600 m downwind of it, none 200 m past that, nothing for mesas.
        [Fact]
        public void GiantBiasZoneStretchesDownwind() {
            var influence = new CloudLandmarkInfluence(CloudLandmarkSettings.Default);
            var heading = new WindVector(0.6, 0.8);
            var shapes = new List<LandmarkSkyShape> { Giant(1000.0, -500.0, 140.0, 150.0), Mesa(-3000.0, 0.0, 80.0, 50.0) };
            Assert.Equal(0.6, influence.GiantBias(shapes, 1000.0, -500.0, heading));
            Assert.Equal(0.6, influence.GiantBias(shapes, 1000.0 + 0.6 * 900.0, -500.0 + 0.8 * 900.0, heading));
            Assert.Equal(0.0, influence.GiantBias(shapes, 1000.0 - 0.6 * 601.0, -500.0 - 0.8 * 601.0, heading));
            Assert.InRange(influence.GiantBias(shapes, 1000.0 - 0.6 * 500.0, -500.0 - 0.8 * 500.0, heading), 0.25, 0.35);
            Assert.Equal(0.0, influence.GiantBias(shapes, -3000.0, 0.0, heading));
        }

        // A synthetic giant and mesa under the real wind for 600 s, every class: no drawn cloud's footprint overlaps a hull
        // that blocks it, clouds do dissolve near them, and no cloud's share changes by more than 0.15 a second.
        [Fact]
        public void CloudsDissolveBeforeASyntheticGiant() {
            var total = new CloudDissolveCheck();
            for (ulong seed = 1; seed <= 6; seed++) {
                var scatter = new CloudFieldScatter(seed, LandmarkSettings());
                var sweep = new CloudLandmarkSweep(scatter, new WindField(seed, WindSettings.Default));
                var shapes = new List<LandmarkSkyShape> { Giant(0.0, 0.0, 150.0, 170.0), Mesa(900.0, 300.0, 130.0, 55.0) };
                total.Merge(sweep.Dissolve(shapes, 100.0 * seed, 600.0, 2.0, 1.0));
            }
            _output.WriteLine($"dissolve: {total.Overlaps} overlaps over {total.Drawn} drawn samples, fastest {total.MostRate:0.000}/s");
            Assert.True(total.Drawn > 1000, $"only {total.Drawn} drawn samples");
            Assert.Equal(0, total.Overlaps);
            Assert.InRange(total.MostRate, 0.001, 0.15);
        }

        // Giants' zones hold 1.4–1.8 times the clouds per km² of open sky (cover reported alongside).
        [Fact]
        public void GiantZonesAreDenser() {
            var total = new CloudDensity();
            for (ulong seed = 1; seed <= 6; seed++) {
                var scatter = new CloudFieldScatter(seed, LandmarkSettings());
                var sweep = new CloudLandmarkSweep(scatter, new WindField(seed, WindSettings.Default));
                var shapes = new List<LandmarkSkyShape>();
                for (int row = -1; row <= 1; row++) {
                    for (int column = -1; column <= 1; column++) shapes.Add(Giant(column * 2200.0, row * 2200.0, 120.0, 150.0));
                }
                total.Merge(sweep.Density(shapes, 0.0, 0.0, 3200.0, 300.0 * seed, 25.0));
            }
            _output.WriteLine($"zone {total.ZoneClouds:0} clouds / {total.ZoneArea:0.00} km², open {total.OpenClouds:0} / {total.OpenArea:0.00} km², ratio {total.Ratio:0.000}, cover ratio {total.CoverRatio:0.000}");
            Assert.InRange(total.Ratio, 1.4, 1.8);
        }

        // The binned lookups give exactly the dissolve and bias of testing every landmark.
        [Fact]
        public void IndexMatchesTheFullList() {
            var influence = new CloudLandmarkInfluence(CloudLandmarkSettings.Default);
            var shapes = new List<LandmarkSkyShape> { Giant(0.0, 0.0, 150.0, 170.0), Giant(1900.0, 700.0, 95.0, 120.0), Mesa(600.0, -800.0, 90.0, 50.0), Mesa(-900.0, 400.0, 145.0, 60.0) };
            var index = new CloudLandmarkIndex();
            index.Build(shapes, influence.Settings, 120.0, 140.0);
            Assert.Equal(3, index.Count);
            var random = new Random(3);
            var heading = new WindVector(-0.8, 0.6);
            for (int sample = 0; sample < 20000; sample++) {
                double x = random.NextDouble() * 6000.0 - 2500.0;
                double z = random.NextDouble() * 6000.0 - 3000.0;
                double altitude = 140.0 + random.NextDouble() * 60.0;
                Assert.Equal(influence.Dissolve(shapes, x, z, 60.0, altitude), influence.Dissolve(index.BlockShapesAt(x, z), x, z, 60.0, altitude));
                Assert.Equal(influence.GiantBias(shapes, x, z, heading), influence.GiantBias(index.BiasShapesAt(x, z), x, z, heading));
            }
            CheckBoxes(influence, shapes, index, random, heading);
        }

        // A box's collected landmarks give every point inside it the full list's dissolve and bias.
        private static void CheckBoxes(CloudLandmarkInfluence influence, List<LandmarkSkyShape> shapes, CloudLandmarkIndex index, Random random, WindVector heading) {
            for (int box = 0; box < 200; box++) {
                double minX = random.NextDouble() * 6000.0 - 3000.0;
                double minZ = random.NextDouble() * 6000.0 - 3000.0;
                var block = new List<LandmarkSkyShape>();
                var bias = new List<LandmarkSkyShape>();
                index.CollectNear(minX, minZ, minX + 300.0, minZ + 300.0, influence.Settings, 140.0, block, bias);
                for (int sample = 0; sample < 50; sample++) {
                    double x = minX + random.NextDouble() * 300.0;
                    double z = minZ + random.NextDouble() * 300.0;
                    Assert.Equal(influence.Dissolve(shapes, x, z, 60.0, 150.0), influence.Dissolve(block, x, z, 60.0, 150.0));
                    Assert.Equal(influence.GiantBias(shapes, x, z, heading), influence.GiantBias(bias, x, z, heading));
                }
            }
        }

        private static CloudFieldSettings LandmarkSettings() {
            return new CloudFieldSettings { Landmarks = CloudLandmarkSettings.Default };
        }

        // A 16-gon of radius r about (x, z).
        private static LandmarkSkyShape Giant(double x, double z, double top, double radius) {
            return new LandmarkSkyShape(top, Ring(x, z, radius), x, z, LandmarkSkyTier.Giant);
        }

        private static LandmarkSkyShape Mesa(double x, double z, double top, double radius) {
            return new LandmarkSkyShape(top, Ring(x, z, radius), x, z, LandmarkSkyTier.Mesa);
        }

        private static List<PlanPoint> Ring(double x, double z, double radius) {
            var ring = new List<PlanPoint>();
            for (int index = 0; index < 16; index++) {
                double angle = index * Math.PI / 8.0;
                ring.Add(new PlanPoint(x + radius * Math.Cos(angle), z + radius * Math.Sin(angle)));
            }
            return ring;
        }

        private static int CheckLatentOrder(CloudInstance[] clouds) {
            int latent = 0;
            bool isLatentRun = false;
            foreach (CloudInstance cloud in clouds) {
                Assert.True(cloud.IsLatent || !isLatentRun, "a regular cloud after a latent one");
                isLatentRun |= cloud.IsLatent;
                if (!cloud.IsLatent) continue;

                latent++;
                Assert.InRange(cloud.Threshold, 1.0 + CloudLandmarkSettings.Default.EnvelopeWidth, 1.0 + CloudLandmarkSettings.Default.GiantBias);
            }
            return latent;
        }

        // Steps of 0.001 in bias move the envelope by at most its slope; returns 1 when the cloud shows at full bias.
        private static int CheckEnvelope(CloudFieldScatter scatter, CloudInstance cloud, double slope) {
            double previous = scatter.Envelope(cloud, 0.0);
            for (double bias = 0.001; bias <= 0.6 + 1e-9; bias += 0.001) {
                double envelope = scatter.Envelope(cloud, bias);
                Assert.True(Math.Abs(envelope - previous) <= slope * 0.001 + 1e-9, $"envelope jumps at bias {bias}");
                Assert.True(envelope >= previous - 1e-12, "envelope falls as the bias rises");
                previous = envelope;
            }
            return cloud.IsLatent && previous > 0.0 ? 1 : 0;
        }

        private static List<(double X, double Z, CloudInstance Cloud)> Anchors(CloudFieldScatter scatter, CloudClass cloudClass, int half) {
            var anchors = new List<(double X, double Z, CloudInstance Cloud)>();
            double size = scatter.Settings.CellSize;
            for (long cellZ = -half; cellZ < half; cellZ++) {
                for (long cellX = -half; cellX < half; cellX++) {
                    foreach (CloudInstance cloud in scatter.Cell(cloudClass, cellX, cellZ)) anchors.Add((cellX * size + cloud.AnchorX, cellZ * size + cloud.AnchorZ, cloud));
                }
            }
            return anchors;
        }

        private static void CheckGap(CloudFieldScatter scatter, (double X, double Z, CloudInstance Cloud) first, (double X, double Z, CloudInstance Cloud) second, double cap, double time, WindVector heading) {
            scatter.Offset(first.Cloud, time, heading.X, heading.Z, out double firstX, out double firstZ);
            scatter.Offset(second.Cloud, time, heading.X, heading.Z, out double secondX, out double secondZ);
            double deltaX = first.X + firstX - second.X - secondX;
            double deltaZ = first.Z + firstZ - second.Z - secondZ;
            double centres = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            Assert.True(centres >= (first.Cloud.Reach + second.Cloud.Reach) * cap + scatter.Settings.MinGap - 1e-9, $"{first.Cloud.Class} clouds touch at {time}");
        }

        private static string Describe(CloudInstance[] clouds) {
            var text = new System.Text.StringBuilder();
            foreach (CloudInstance cloud in clouds) {
                text.Append(BitConverter.DoubleToInt64Bits(cloud.AnchorX)).Append(',').Append(BitConverter.DoubleToInt64Bits(cloud.AnchorZ)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(cloud.Altitude)).Append(',').Append(cloud.Variant).Append(',').Append(cloud.IsLatent)
                    .Append(',').Append(BitConverter.DoubleToInt64Bits(cloud.Threshold)).Append(';');
            }
            return text.ToString();
        }
    }
}
