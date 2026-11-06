using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    public sealed class FaceBlinkSchedulerTests {
        private const float Step = 1f / 60f;

        [Fact]
        public void FirstBlinkStartsInsideTheInterval() {
            var tuning = new FaceExpressionTuning();
            for (uint seed = 1; seed <= 50; seed++) {
                var blink = new FaceBlinkScheduler(seed, tuning);
                float started = TimeToFirstBlink(blink, tuning);

                Assert.InRange(started, tuning.blinkIntervalMin - Step, tuning.blinkIntervalMax + Step);
            }
        }

        [Fact]
        public void BlinkShutsFullyAndReopens() {
            var tuning = new FaceExpressionTuning { blinkIntervalMin = 0.5f, blinkIntervalMax = 0.5f, doubleBlinkChance = 0f };
            var blink = new FaceBlinkScheduler(7u, tuning);
            float peak = 0f;
            float duration = tuning.blinkCloseSeconds + tuning.blinkShutSeconds + tuning.blinkOpenSeconds;
            for (float time = 0f; time < 0.5f + duration + 0.05f; time += Step) peak = System.Math.Max(peak, blink.Step(Step, tuning));

            Assert.Equal(1f, peak, 3);
            Assert.Equal(0f, blink.Value);
            Assert.Equal(1, blink.BlinkCount);
        }

        [Fact]
        public void PausesStayInsideTheIntervalAndSomeBlinksAreDoubles() {
            var tuning = new FaceExpressionTuning();
            var blink = new FaceBlinkScheduler(12345u, tuning);
            for (float time = 0f; time < 600f; time += Step) blink.Step(Step, tuning);

            float meanInterval = 600f / blink.BlinkCount;
            Assert.InRange(meanInterval, 2.5f, 5f);
            Assert.InRange(blink.DoubleBlinkCount, 1, blink.BlinkCount / 2);
        }

        [Fact]
        public void SameSeedReplaysAndOtherSeedsDiffer() {
            var tuning = new FaceExpressionTuning();
            var first = new FaceBlinkScheduler(99u, tuning);
            var again = new FaceBlinkScheduler(99u, tuning);
            var other = new FaceBlinkScheduler(100u, tuning);

            Assert.Equal(first.SecondsUntilNext, again.SecondsUntilNext);
            Assert.NotEqual(first.SecondsUntilNext, other.SecondsUntilNext);
        }

        [Fact]
        public void ZeroSeedStillBlinks() {
            var tuning = new FaceExpressionTuning();
            var blink = new FaceBlinkScheduler(0u, tuning);

            Assert.InRange(TimeToFirstBlink(blink, tuning), tuning.blinkIntervalMin - Step, tuning.blinkIntervalMax + Step);
        }

        [Fact]
        public void BadStepsNeverStopTheBlinking() {
            var tuning = new FaceExpressionTuning { blinkIntervalMin = 0.5f, blinkIntervalMax = 0.5f, doubleBlinkChance = 0f };
            var blink = new FaceBlinkScheduler(7u, tuning);
            blink.Step(float.NaN, tuning);
            blink.Step(float.PositiveInfinity, tuning);
            blink.Step(-1f, tuning);
            Assert.False(float.IsNaN(blink.Value));
            Assert.Equal(0.5f, blink.SecondsUntilNext, 4);

            for (float time = 0f; time < 3f; time += Step) Assert.False(float.IsNaN(blink.Step(Step, tuning)));
            Assert.True(blink.BlinkCount >= 4, $"{blink.BlinkCount} blinks");
        }

        [Fact]
        public void BadTuningNeverPoisonsTheLids() {
            var tuning = new FaceExpressionTuning { blinkIntervalMin = 0.2f, blinkIntervalMax = float.NaN, blinkCloseSeconds = float.NaN, doubleBlinkGap = float.NaN };
            var blink = new FaceBlinkScheduler(3u, tuning);
            for (float time = 0f; time < 3f; time += Step) Assert.False(float.IsNaN(blink.Step(Step, tuning)));

            Assert.True(blink.BlinkCount > 0);
        }

        private static float TimeToFirstBlink(FaceBlinkScheduler blink, FaceExpressionTuning tuning) {
            for (float time = 0f; time < tuning.blinkIntervalMax * 2f; time += Step) {
                blink.Step(Step, tuning);
                if (blink.BlinkCount > 0) return time;
            }

            return float.PositiveInfinity;
        }
    }
}
