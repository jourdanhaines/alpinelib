using System;
using System.Numerics;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class SmokeStyleTests {
        private const double Frame = 1.0 / 60.0;
        private const int Black = 0;
        private const int Brown = 1;
        private const int Dark = 2;
        private const int Grey = 3;
        private const int Light = 4;

        // The loco's three stages over five strips: labouring black/dark/brown, then dark/grey, then grey/light.
        public static SmokeStyle Staged(double riseSeconds = SmokeStyle.DefaultRiseSeconds, double fallSeconds = SmokeStyle.DefaultFallSeconds) {
            var shades = new SkyAtlasBand[5];
            for (int shade = 0; shade < shades.Length; shade++) shades[shade] = new SkyAtlasBand(new Vector2(0.1f * shade, 0.1f), new Vector2(0.1f * shade, 0.05f));
            var stages = new[] {
                new SmokeStage(0.1, new[] { new SmokeShadeWeight(Black, 0.4), new SmokeShadeWeight(Dark, 0.3), new SmokeShadeWeight(Brown, 0.3) }, 1.3, 1.0, 0.85),
                new SmokeStage(0.5, new[] { new SmokeShadeWeight(Dark, 0.5), new SmokeShadeWeight(Grey, 0.5) }, 1.1),
                new SmokeStage(0.9, new[] { new SmokeShadeWeight(Grey, 0.5), new SmokeShadeWeight(Light, 0.5) }, 1.0, 0.9, 1.1),
            };
            return new SmokeStyle(shades, stages, riseSeconds, fallSeconds);
        }

        [Fact]
        public void ShadeSharesSumToOneAndStagesArePureAtTheirPoints() {
            SmokeStyle style = Staged();
            for (int step = 0; step <= 100; step++) {
                double total = 0.0;
                for (int shade = 0; shade < style.Shades.Count; shade++) total += style.ShadeShare(step / 100.0, shade);
                Assert.Equal(1.0, total, 9);
            }

            Assert.Equal(0.4, style.ShadeShare(0.0, Black), 9);
            Assert.Equal(0.0, style.ShadeShare(0.1, Grey), 9);
            Assert.Equal(0.5, style.ShadeShare(0.5, Grey), 9);
            Assert.Equal(0.5, style.ShadeShare(1.0, Light), 9);
            Assert.Equal(0.0, style.ShadeShare(0.5, Light), 9);
        }

        // The mix never moves more than 10 % (per shade and in total variation) for a 0.05 change in efficiency.
        [Fact]
        public void StageBlendIsContinuous() {
            SmokeStyle style = Staged();
            for (int step = 0; step + 5 <= 100; step++) {
                double from = step / 100.0;
                double to = from + 0.05;
                double variation = 0.0;
                for (int shade = 0; shade < style.Shades.Count; shade++) {
                    double change = Math.Abs(style.ShadeShare(to, shade) - style.ShadeShare(from, shade));
                    Assert.True(change < 0.1, $"shade {shade} moved {change:0.000} from {from:0.00} to {to:0.00}");
                    variation += change * 0.5;
                }
                Assert.True(variation < 0.1, $"the mix moved {variation:0.000} from {from:0.00} to {to:0.00}");
            }
        }

        [Fact]
        public void SamplesFollowTheSharesAndStayInTheBlendedStages() {
            SmokeStyle style = Staged();
            foreach (double efficiency in new[] { 0.0, 0.2, 0.3, 0.45, 0.7, 1.0 }) {
                var counts = new int[style.Shades.Count];
                const int samples = 20000;
                for (int index = 0; index < samples; index++) counts[style.Sample(efficiency, (ulong)index * 7919UL + 3UL).Shade]++;
                for (int shade = 0; shade < counts.Length; shade++) {
                    double expected = style.ShadeShare(efficiency, shade);
                    double seen = counts[shade] / (double)samples;
                    if (expected == 0.0) Assert.Equal(0, counts[shade]);
                    else Assert.True(Math.Abs(seen - expected) < 0.02, $"shade {shade} at {efficiency}: {seen:0.000} vs {expected:0.000}");
                }
            }
        }

        [Fact]
        public void SampleIsDeterministicPerSeed() {
            SmokeStyle style = Staged();
            for (ulong seed = 0; seed < 200; seed++) {
                SmokeSample first = style.Sample(0.33, seed);
                SmokeSample second = style.Sample(0.33, seed);
                Assert.Equal(first.Shade, second.Shade);
                Assert.Equal(first.Thickness, second.Thickness);
            }

            Assert.Equal(Run(5UL), Run(5UL));
            Assert.NotEqual(Run(5UL), Run(6UL));
        }

        [Fact]
        public void ThicknessLifeAndRiseBlendWithinTheirStages() {
            SmokeStyle style = Staged();
            Assert.Equal(1.3, style.Sample(0.0, 1UL).Thickness, 9);
            Assert.Equal(1.2, style.Sample(0.3, 1UL).Thickness, 9);
            Assert.Equal(1.0, style.Sample(1.0, 1UL).Thickness, 9);
            Assert.Equal(1.0, style.MaxLifeScale, 9);
            double previous = double.MaxValue;
            for (int step = 0; step <= 100; step++) {
                SmokeSample sample = style.Sample(step / 100.0, 9UL);
                Assert.InRange(sample.Thickness, 1.0, 1.3);
                Assert.InRange(sample.LifeScale, 0.9, 1.0);
                Assert.InRange(sample.RiseScale, 0.85, 1.1);
                Assert.True(sample.Thickness <= previous + 1e-12, $"thickness rose at {step / 100.0}");
                previous = sample.Thickness;
            }

            var simulation = new PuffSimulation(new PuffSettings(), 3UL, style);
            simulation.Step(Frame, Vector3.Zero, 1.0 / Frame, 1, 0.0);
            Puff born = simulation[0];
            Assert.Equal(1.3f, born.Thickness, 5);
            Assert.Equal(simulation.SizeAt(simulation.ProgressOf(born), 0.0) * 1.3, simulation.SizeOf(born), 6);
        }

        [Fact]
        public void ThickSmokeLeansToLumpierShapes() {
            double plain = 0.0;
            double thick = 0.0;
            for (int index = 0; index < 1000; index++) {
                double roll = (index + 0.5) / 1000.0;
                plain += PuffSimulation.ShapeFor(roll, 1.0);
                thick += PuffSimulation.ShapeFor(roll, 1.3);
            }

            Assert.Equal(2.5, plain / 1000.0, 1);
            Assert.True(thick > plain, "thick smoke should pick more-blob shapes");
            Assert.Equal(PuffSettings.VariantCount - 1, PuffSimulation.ShapeFor(1.0, 1.0));
        }

        // A step from 0 to 1 reaches 1 − 1/e after the rise constant; the fall back is slower; nothing jumps in a frame.
        [Fact]
        public void EfficiencyFollowsItsTargetByTheTimeConstants() {
            var simulation = new PuffSimulation(new PuffSettings(), 4UL, Staged());
            simulation.TargetEfficiency = 1.0;
            double previous = 0.0;
            double worstStep = 0.0;
            for (int frame = 0; frame < 120; frame++) {
                simulation.Step(Frame, Vector3.Zero, 1.5, 1, 0.0);
                worstStep = Math.Max(worstStep, simulation.Efficiency - previous);
                previous = simulation.Efficiency;
            }
            Assert.Equal(1.0 - Math.Exp(-1.0), simulation.Efficiency, 3);
            Assert.True(worstStep <= 1.0 - Math.Exp(-Frame / 2.0) + 1e-9, $"a frame moved the efficiency by {worstStep}");

            simulation.ResetEfficiency(1.0);
            simulation.TargetEfficiency = 0.0;
            for (int frame = 0; frame < 180; frame++) simulation.Step(Frame, Vector3.Zero, 1.5, 1, 0.0);
            Assert.Equal(Math.Exp(-1.0), simulation.Efficiency, 3);
        }

        // Without smoothing the colour pops: the same step jumps the efficiency in one frame.
        [Fact]
        public void NoSmoothingJumps() {
            var simulation = new PuffSimulation(new PuffSettings(), 4UL, Staged(0.0, 0.0));
            simulation.TargetEfficiency = 1.0;
            simulation.Step(Frame, Vector3.Zero, 1.5, 1, 0.0);
            Assert.Equal(1.0, simulation.Efficiency, 9);
        }

        [Fact]
        public void FrameRateDoesNotChangeTheSmoothing() {
            var fine = new PuffSimulation(new PuffSettings(), 4UL, Staged());
            var coarse = new PuffSimulation(new PuffSettings(), 4UL, Staged());
            fine.TargetEfficiency = coarse.TargetEfficiency = 0.8;
            for (int frame = 0; frame < 1440; frame++) fine.Step(1.0 / 144.0, Vector3.Zero, 0.0, 1, 0.0);
            for (int frame = 0; frame < 300; frame++) coarse.Step(1.0 / 30.0, Vector3.Zero, 0.0, 1, 0.0);
            Assert.Equal(fine.Efficiency, coarse.Efficiency, 9);
        }

        [Fact]
        public void LifeScaleSetsWhenAPuffDies() {
            var shades = new[] { new SkyAtlasBand(Vector2.Zero, Vector2.One) };
            var style = new SmokeStyle(shades, new[] { new SmokeStage(0.0, new[] { new SmokeShadeWeight(0, 1.0) }, 1.0, 0.5) });
            var simulation = new PuffSimulation(new PuffSettings(), 2UL, style);
            simulation.Step(Frame, Vector3.Zero, 1.0 / Frame, 1, 0.0);
            for (int frame = 0; frame < 100; frame++) simulation.Step(Frame, Vector3.Zero, 0.0, 1, 0.0);
            Assert.Equal(1, simulation.Count);
            for (int frame = 0; frame < 6; frame++) simulation.Step(Frame, Vector3.Zero, 0.0, 1, 0.0);
            Assert.Equal(0, simulation.Count);
            Assert.Equal(64.0 / (2 * 3.5 * 0.5), new PuffSettings().SafeBurstRate(2, 1.0, 0.5), 9);
        }

        [Fact]
        public void InvalidStylesThrow() {
            var band = new SkyAtlasBand(Vector2.Zero, Vector2.One);
            var one = new[] { new SmokeShadeWeight(0, 1.0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new SkyAtlasBand[6], new[] { new SmokeStage(0.0, one) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new[] { band }, new SmokeStage[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new[] { band }, new[] { new SmokeStage(0.5, one), new SmokeStage(0.5, one) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new[] { band }, new[] { new SmokeStage(0.0, new[] { new SmokeShadeWeight(1, 1.0) }) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new[] { band }, new[] { new SmokeStage(0.0, one, 0.0) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmokeStyle(new[] { band }, new[] { new SmokeStage(0.0, one) }, -1.0));
        }

        private static string Run(ulong seed) {
            var simulation = new PuffSimulation(new PuffSettings(), seed, Staged());
            var text = new System.Text.StringBuilder();
            for (int frame = 0; frame < 600; frame++) {
                simulation.TargetEfficiency = frame / 600.0;
                simulation.Step(Frame, Vector3.Zero, 6.0, 1, 0.5);
                if (frame % 60 != 0) continue;

                for (int index = 0; index < simulation.Count; index++) text.Append(simulation[index].Shade).Append(simulation[index].Variant).Append(';');
            }

            return text.ToString();
        }
    }
}
