using System;
using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    public sealed class FaceExpressionMixerTests {
        private const float Step = 1f / 60f;

        [Fact]
        public void JumpOpensWide() {
            var mixer = Quiet();
            mixer.Step(new FaceSignalFrame { Jumped = true, Airborne = true }, Step);
            float peak = Run(mixer, new FaceSignalFrame { Airborne = true, AirTime = 0.1f }, 0.2f, FaceChannel.Wide);

            Assert.InRange(peak, mixer.Tuning.jumpWide * 0.95f, mixer.Tuning.jumpWide * (1f + mixer.Tuning.pulseOvershoot) + 1e-4f);
        }

        [Fact]
        public void LandingSquashGrowsWithAirTime() {
            float shortHop = LandPeak(0.3f);
            float longFall = LandPeak(1.5f);

            Assert.True(longFall > shortHop + 0.3f, $"short {shortHop}, long {longFall}");
            Assert.Equal(0f, LandPeak(0.02f));
        }

        [Fact]
        public void LongFallWorriesAndShortHopDoesNot() {
            var mixer = Quiet();
            float hop = Run(mixer, new FaceSignalFrame { Airborne = true, AirTime = 0.6f }, 0.5f, FaceChannel.Worried);
            float fall = Run(mixer, new FaceSignalFrame { Airborne = true, AirTime = 1.4f }, 0.6f, FaceChannel.Worried);

            Assert.Equal(0f, hop);
            Assert.InRange(fall, mixer.Tuning.worriedPeak * 0.9f, mixer.Tuning.worriedPeak + 1e-4f);
        }

        [Fact]
        public void SquintFollowsExertionOnTheGroundOnly() {
            var mixer = Quiet();
            float jog = Run(mixer, new FaceSignalFrame { Exertion = 0.5f }, 2f, FaceChannel.Squint);
            float sprint = Run(mixer, new FaceSignalFrame { Exertion = 1f }, 2f, FaceChannel.Squint);
            Run(mixer, new FaceSignalFrame { Exertion = 1f, Airborne = true }, 3f, FaceChannel.Squint);

            Assert.True(jog > 0.1f && sprint > jog + 0.15f, $"jog {jog}, sprint {sprint}");
            Assert.Equal(0f, mixer.WeightOf(FaceChannel.Squint));
        }

        [Fact]
        public void GlanceLooksDownAndReturns() {
            var mixer = Quiet();
            float down = Run(mixer, new FaceSignalFrame { GlanceDown = 1f }, 1f, FaceChannel.LookDown);
            Run(mixer, new FaceSignalFrame(), 2f, FaceChannel.LookDown);

            Assert.Equal(mixer.Tuning.glanceLookDown, down, 2);
            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void BudgetHoldsUnderAnySignals() {
            var mixer = new FaceExpressionMixer(new FaceExpressionTuning(), 5u);
            var random = new Random(3);
            float air = 0f;
            for (int frame = 0; frame < 20000; frame++) {
                var signals = new FaceSignalFrame {
                    Airborne = random.NextDouble() < 0.5, Jumped = random.NextDouble() < 0.05, Landed = random.NextDouble() < 0.05,
                    LandAirTime = (float)random.NextDouble() * 3f, Exertion = (float)random.NextDouble() * 1.5f,
                    GlanceDown = (float)random.NextDouble(), LookRight = (float)(random.NextDouble() * 4 - 2), LookUp = (float)(random.NextDouble() * 4 - 2),
                };
                air = signals.Airborne ? air + Step : 0f;
                signals.AirTime = air;
                mixer.Step(signals, Step);

                Assert.True(mixer.ExpressionSum() <= mixer.Tuning.expressionBudget + 1e-5f, $"frame {frame}: sum {mixer.ExpressionSum()}");
                foreach (float weight in mixer.Weights) Assert.InRange(weight, 0f, 1f);
            }
        }

        [Fact]
        public void LandingWinsTheBudgetOverWorry() {
            var mixer = Quiet();
            Run(mixer, new FaceSignalFrame { Airborne = true, AirTime = 1.6f }, 0.6f, FaceChannel.Worried);
            mixer.Step(new FaceSignalFrame { Landed = true, LandAirTime = 1.6f }, Step);
            float squash = Run(mixer, new FaceSignalFrame(), 0.1f, FaceChannel.Squash);

            Assert.True(squash >= mixer.Tuning.landSquashMax * 0.95f, $"squash {squash}");
            Assert.True(mixer.ExpressionSum() <= 1f + 1e-5f);
        }

        [Fact]
        public void SettlesToExactZeros() {
            var mixer = Quiet();
            mixer.Step(new FaceSignalFrame { Jumped = true, Airborne = true }, Step);
            Run(mixer, new FaceSignalFrame { Airborne = true, AirTime = 1.5f, Exertion = 1f, GlanceDown = 1f, LookRight = 1f }, 1f, FaceChannel.Worried);
            mixer.Step(new FaceSignalFrame { Landed = true, LandAirTime = 1.5f }, Step);
            Run(mixer, new FaceSignalFrame(), 3f, FaceChannel.Blink);

            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void BadTimeStepsNeverPoisonTheWeights() {
            var mixer = Quiet();
            mixer.Step(new FaceSignalFrame { Jumped = true, Exertion = float.NaN, GlanceDown = float.NaN }, float.NaN);
            mixer.Step(new FaceSignalFrame(), -1f);
            mixer.Step(new FaceSignalFrame(), float.PositiveInfinity);

            foreach (float weight in mixer.Weights) Assert.False(float.IsNaN(weight));
        }

        // Blinks pushed far away so the other channels read alone.
        private static FaceExpressionMixer Quiet() {
            var tuning = new FaceExpressionTuning { blinkIntervalMin = 1000f, blinkIntervalMax = 1000f };
            return new FaceExpressionMixer(tuning, 1u);
        }

        private static float LandPeak(float airTime) {
            var mixer = Quiet();
            mixer.Step(new FaceSignalFrame { Landed = true, LandAirTime = airTime }, Step);
            return Math.Max(mixer.WeightOf(FaceChannel.Squash), Run(mixer, new FaceSignalFrame(), 0.3f, FaceChannel.Squash));
        }

        private static float Run(FaceExpressionMixer mixer, FaceSignalFrame signals, float seconds, FaceChannel watched) {
            float peak = 0f;
            for (float time = 0f; time < seconds; time += Step) {
                mixer.Step(signals, Step);
                peak = Math.Max(peak, mixer.WeightOf(watched));
            }

            return peak;
        }
    }
}
