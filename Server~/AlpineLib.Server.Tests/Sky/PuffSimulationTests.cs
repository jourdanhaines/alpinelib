using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class PuffSimulationTests {
        private const double Frame = 1.0 / 60.0;
        private static readonly SkyAtlasBand Light = new SkyAtlasBand(new Vector2(0.1f, 0.9f), new Vector2(0.1f, 0.8f));
        private static readonly SkyAtlasBand Dark = new SkyAtlasBand(new Vector2(0.2f, 0.9f), new Vector2(0.2f, 0.8f));

        [Fact]
        public void PoolNeverExceedsItsCapacity() {
            var simulation = new PuffSimulation(new PuffSettings(), 3UL);
            int most = 0;
            for (int frame = 0; frame < 600; frame++) {
                simulation.Step(Frame, new Vector3(-12f, 0f, 0f), 40.0, 3, 1.0);
                most = Math.Max(most, simulation.Count);
            }

            Assert.Equal(64, most);
            Assert.True(simulation.Dropped > 0, "a flood should drop puffs");
        }

        [Fact]
        public void FullChuffStaysUnderTheCap() {
            var simulation = new PuffSimulation(new PuffSettings(), 4UL);
            for (int frame = 0; frame < 1200; frame++) simulation.Step(Frame, new Vector3(-20f, 0f, 3f), 8.0, 2, 1.0);

            Assert.Equal(0UL, simulation.Dropped);
            Assert.InRange(simulation.Count, 50, 60);
        }

        [Fact]
        public void PuffsDieAtTheirLifetime() {
            var simulation = new PuffSimulation(new PuffSettings(), 5UL);
            simulation.Step(Frame, Vector3.Zero, 1.0 / Frame, 1, 0.0);
            int born = simulation.Count;
            Assert.True(born >= 1);
            double age = 0.0;
            while (age < 3.5 - 2 * Frame) {
                simulation.Step(Frame, Vector3.Zero, 0.0, 1, 0.0);
                age += Frame;
            }
            Assert.Equal(born, simulation.Count);
            for (int frame = 0; frame < 3; frame++) simulation.Step(Frame, Vector3.Zero, 0.0, 1, 0.0);

            Assert.Equal(0, simulation.Count);
        }

        [Fact]
        public void LifetimeCurveGrowsRisesSlowsAndShrinksAway() {
            var simulation = new PuffSimulation(new PuffSettings(), 1UL);
            Assert.Equal(0.6, simulation.SizeAt(0.0, 0.0), 9);
            Assert.Equal(0.0, simulation.SizeAt(1.0, 0.0), 9);
            Assert.Equal(1.0, simulation.ShrinkAt(0.75), 9);
            Assert.True(simulation.ShrinkAt(0.76) < 1.0);
            Assert.Equal(2.5, simulation.RiseAt(0.0, 0.0), 9);
            Assert.Equal(0.6, simulation.RiseAt(1.0, 0.0), 9);
            double grownAtShrink = 0.6 + (2.4 - 0.6) * (1.0 - 0.25 * 0.25);
            Assert.Equal(grownAtShrink, simulation.SizeAt(0.75, 0.0), 9);
            Assert.True(simulation.SizeAt(0.0, 1.0) > simulation.SizeAt(0.0, 0.0));
            Assert.True(simulation.RiseAt(0.1, 1.0) > simulation.RiseAt(0.1, 0.0));
            Assert.Equal(simulation.RiseAt(0.5, 1.0), simulation.RiseAt(0.5, 0.0), 9);

            double previousSize = 0.0;
            double previousRise = double.MaxValue;
            for (int step = 0; step <= 100; step++) {
                double progress = step / 100.0;
                double size = simulation.SizeAt(progress, 0.0);
                double rise = simulation.RiseAt(progress, 0.0);
                if (step <= 75) Assert.True(size >= previousSize - 1e-12, $"size shrank before the last quarter at {progress}");
                else Assert.True(size <= previousSize + 1e-12, $"size grew in the last quarter at {progress}");
                Assert.True(rise <= previousRise + 1e-12, $"rise sped up at {progress}");
                previousSize = size;
                previousRise = rise;
            }
        }

        [Fact]
        public void SameSeedSameStepsSamePuffs() {
            string first = Run(11UL);
            string second = Run(11UL);
            string other = Run(12UL);
            Assert.Equal(first, second);
            Assert.NotEqual(first, other);
        }

        [Fact]
        public void IdleRateAveragesOverTime() {
            var simulation = new PuffSimulation(new PuffSettings(), 8UL);
            for (int frame = 0; frame < 600; frame++) simulation.Step(Frame, Vector3.Zero, 1.5, 1, 0.0);

            Assert.InRange((long)simulation.Emitted, 14L, 16L);
        }

        [Fact]
        public void PuffsRideTheRelativeAir() {
            var settings = new PuffSettings { Spread = 0.0 };
            var simulation = new PuffSimulation(settings, 9UL);
            simulation.Step(Frame, Vector3.Zero, 1.0 / Frame, 1, 0.0);
            Puff born = simulation[0];
            for (int frame = 0; frame < 60; frame++) simulation.Step(Frame, new Vector3(-10f, 0f, 2f), 0.0, 1, 0.0);

            Puff later = simulation[0];
            Assert.Equal(-10.0, later.Offset.X - born.Offset.X, 3);
            Assert.Equal(2.0, later.Offset.Z - born.Offset.Z, 3);
            Assert.InRange(later.Offset.Y - born.Offset.Y, 1.0, 2.5);
        }

        [Fact]
        public void FrameRateBarelyChangesWherePuffsGo() {
            var settings = new PuffSettings { Spread = 0.0 };
            var fine = new PuffSimulation(settings, 9UL);
            var coarse = new PuffSimulation(settings, 9UL);
            fine.Step(1e-3, Vector3.Zero, 1000.0, 1, 1.0);
            coarse.Step(1e-3, Vector3.Zero, 1000.0, 1, 1.0);
            for (int frame = 0; frame < 2400; frame++) fine.Step(1e-3, new Vector3(-6f, 0f, 0f), 0.0, 1, 1.0);
            for (int frame = 0; frame < 80; frame++) coarse.Step(0.03, new Vector3(-6f, 0f, 0f), 0.0, 1, 1.0);

            Assert.True(Vector3.Distance(fine[0].Offset, coarse[0].Offset) < 0.05f, $"{fine[0].Offset} vs {coarse[0].Offset}");
        }

        [Fact]
        public void ClearForgetsLivePuffsOnly() {
            var simulation = new PuffSimulation(new PuffSettings(), 2UL);
            for (int frame = 0; frame < 120; frame++) simulation.Step(Frame, Vector3.Zero, 4.0, 1, 0.0);
            ulong emitted = simulation.Emitted;
            simulation.Clear();
            Assert.Equal(0, simulation.Count);
            Assert.Equal(emitted, simulation.Emitted);
        }

        [Fact]
        public void InvalidSettingsThrow() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuffSimulation(new PuffSettings { Capacity = 0 }, 1UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuffSimulation(new PuffSettings { EndSize = 0.1 }, 1UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuffSimulation(new PuffSettings { ShrinkShare = 0.0 }, 1UL));
        }

        [Fact]
        public void PuffMeshesAreAiryClumpsInBudget() {
            var hashes = new HashSet<ulong>();
            for (ulong seed = 1; seed <= 40; seed++) {
                for (int variant = 0; variant < PuffSettings.VariantCount; variant++) {
                    FacetMesh mesh = PuffShape.Build(variant, seed, variant % 2 == 0 ? Light : Dark, SmokeStyle.DefaultTone);
                    Assert.InRange(PuffShape.BlobsOf(variant), 5, 8);
                    Assert.InRange(mesh.FaceCount, 40, PuffShape.FaceBudget);
                    Vector3 size = mesh.Bounds.Size;
                    Assert.Equal(1.0, Math.Max(size.X, Math.Max(size.Y, size.Z)), 4);
                    Assert.True(Math.Min(size.X, Math.Min(size.Y, size.Z)) > 0.6f, $"variant {variant} seed {seed} is flat: {size}");
                    Assert.True(mesh.Bounds.Centre.Length() < 1e-4f);
                    float column = variant % 2 == 0 ? Light.Light.X : Dark.Light.X;
                    foreach (Vector2 uv in mesh.Uvs) Assert.Equal(column, uv.X, 5);
                    hashes.Add(mesh.ComputePositionHash());
                }
            }

            Assert.Equal(40 * PuffSettings.VariantCount, hashes.Count);
            Assert.Equal(PuffShape.Build(2, 7UL, Light, 0.2).ComputePositionHash(), PuffShape.Build(2, 7UL, Dark, 0.7).ComputePositionHash());
        }

        [Fact]
        public void EveryFaceOfAPuffSharesOneUv() {
            SmokeStyle style = SmokeStyleTests.Staged();
            for (ulong seed = 1; seed <= 20; seed++) {
                for (int shade = 0; shade < style.Shades.Count; shade++) {
                    for (int variant = 0; variant < PuffSettings.VariantCount; variant++) {
                        FacetMesh mesh = PuffShape.Build(variant, seed, style.Shades[shade], style.ToneOf(variant));
                        Vector2 expected = style.Shades[shade].At(style.ToneOf(variant));
                        foreach (Vector2 uv in mesh.Uvs) Assert.Equal(expected, uv);
                    }
                }
            }
        }

        [Fact]
        public void ToneStepsStayFewAndSmall() {
            SmokeStyle style = SmokeStyleTests.Staged();
            var tones = new HashSet<double>();
            for (int variant = 0; variant < PuffSettings.VariantCount; variant++) {
                tones.Add(style.ToneOf(variant));
                Assert.InRange(Math.Abs(style.ToneOf(variant) - style.Tone), 0.0, SmokeStyle.MaxToneJitter + 1e-12);
            }

            Assert.Equal(PuffShape.ToneSteps, tones.Count);
            Assert.Equal(0, PuffSettings.VariantCount % PuffShape.ToneSteps);
            Assert.Equal(0.0, PuffShape.ToneOf(0, 0.0, 0.15));
            Assert.Equal(1.0, PuffShape.ToneOf(2, 1.0, 0.15));
        }

        [Fact]
        public void BurstMatesShareShadeThicknessAndTone() {
            var simulation = new PuffSimulation(new PuffSettings(), 21UL, SmokeStyleTests.Staged(0.0, 0.0));
            int bursts = 0;
            for (int frame = 0; frame < 600; frame++) {
                simulation.TargetEfficiency = frame / 600.0;
                ulong before = simulation.Emitted;
                simulation.Step(Frame, Vector3.Zero, 3.0, 3, 0.5);
                if (simulation.Emitted == before) continue;

                bursts++;
                AssertMatesAlike(simulation, before);
            }

            Assert.True(bursts > 20, $"only {bursts} bursts");
        }

        [Fact]
        public void MatesKeepTheirToneStep() {
            for (int first = 0; first < PuffSettings.VariantCount; first++) {
                for (int mate = 0; mate < 4; mate++) Assert.Equal(PuffShape.ToneStepOf(first), PuffShape.ToneStepOf(PuffSimulation.MateVariant(first, mate)));
                Assert.NotEqual(first, PuffSimulation.MateVariant(first, 1));
            }
        }

        // Every puff emitted from <paramref name="first"/> on this step came from one burst.
        private static void AssertMatesAlike(PuffSimulation simulation, ulong first) {
            Puff? lead = null;
            for (int index = 0; index < simulation.Count; index++) {
                Puff puff = simulation[index];
                if (puff.Index < first) continue;
                if (lead == null) {
                    lead = puff;
                    continue;
                }

                Assert.Equal(lead.Value.Shade, puff.Shade);
                Assert.Equal(lead.Value.Thickness, puff.Thickness);
                Assert.Equal(PuffShape.ToneStepOf(lead.Value.Variant), PuffShape.ToneStepOf(puff.Variant));
            }
        }

        private static string Run(ulong seed) {
            var simulation = new PuffSimulation(new PuffSettings(), seed);
            var text = new StringBuilder();
            for (int frame = 0; frame < 400; frame++) {
                double effort = frame > 200 ? 1.0 : 0.0;
                simulation.Step(Frame, new Vector3(-0.02f * frame, 0f, 1f), frame > 200 ? 6.0 : 1.5, frame > 200 ? 2 : 1, effort);
                if (frame % 50 != 0) continue;

                for (int index = 0; index < simulation.Count; index++) {
                    Puff puff = simulation[index];
                    text.Append(BitConverter.SingleToInt32Bits(puff.Offset.X)).Append(',').Append(BitConverter.SingleToInt32Bits(puff.Offset.Y)).Append(',')
                        .Append(BitConverter.SingleToInt32Bits(puff.Offset.Z)).Append(',').Append(puff.Variant).Append(',').Append(puff.Index).Append(';');
                }
            }

            return text.ToString();
        }
    }
}
