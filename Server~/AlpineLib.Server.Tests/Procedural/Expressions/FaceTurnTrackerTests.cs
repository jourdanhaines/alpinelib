using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    public sealed class FaceTurnTrackerTests {
        private const float Step = 1f / 60f;

        [Fact]
        public void FirstSampleIsOnlyABaseline() {
            var tracker = new FaceTurnTracker();

            Assert.Equal(0f, tracker.Sample(123f, Step));
            Assert.Equal(120f, tracker.Sample(125f, Step), 2);
        }

        [Fact]
        public void TurningRightIsPositiveAndLeftNegative() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(10f, Step);

            Assert.True(tracker.Sample(13f, Step) > 0f);
            Assert.True(tracker.Sample(9f, Step) < 0f);
        }

        [Fact]
        public void WrapsAcrossOneEighty() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(179f, Step);
            Assert.Equal(120f, tracker.Sample(-179f, Step), 2);
            Assert.Equal(-120f, tracker.Sample(179f, Step), 2);
            tracker.Sample(359f, Step);
            Assert.Equal(120f, tracker.Sample(1f, Step), 2);
            Assert.Equal(120f, tracker.Sample(723f, Step), 2);
        }

        [Fact]
        public void SnapRestartsInsteadOfSpiking() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(0f, Step);
            tracker.Sample(2f, Step);

            Assert.Equal(0f, tracker.Sample(150f, Step));
            Assert.Equal(60f, tracker.Sample(151f, Step), 2);
        }

        [Fact]
        public void BadAndTinyStepsCannotSpike() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(0f, Step);
            float rate = tracker.Sample(2f, Step);

            Assert.Equal(rate, tracker.Sample(2f, 0f));
            Assert.Equal(rate, tracker.Sample(2f, float.NaN));
            Assert.Equal(rate, tracker.Sample(2f, -1f));
            Assert.Equal(rate, tracker.Sample(2f, float.PositiveInfinity));
            Assert.True(tracker.Sample(3f, 1e-6f) <= 1f / FaceTurnTracker.MinStepSeconds + 1e-3f);
            Assert.Equal(FaceTurnTracker.MaxRate, tracker.Sample(80f, 1e-6f));
            Assert.Equal(2f, tracker.Sample(82f, 1f), 3);
        }

        [Fact]
        public void BadYawRestarts() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(0f, Step);
            tracker.Sample(2f, Step);

            Assert.Equal(0f, tracker.Sample(float.NaN, Step));
            Assert.Equal(0f, tracker.Sample(float.PositiveInfinity, Step));
            Assert.Equal(0f, tracker.Sample(40f, Step));
            Assert.Equal(60f, tracker.Sample(41f, Step), 2);
        }

        [Fact]
        public void RestartForgetsTheFacing() {
            var tracker = new FaceTurnTracker();
            tracker.Sample(0f, Step);
            tracker.Sample(2f, Step);
            tracker.Restart();

            Assert.Equal(0f, tracker.Rate);
            Assert.Equal(0f, tracker.Sample(60f, Step));
        }
    }
}
