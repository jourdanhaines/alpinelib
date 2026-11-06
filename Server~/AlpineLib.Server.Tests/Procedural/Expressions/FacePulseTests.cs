using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    public sealed class FacePulseTests {
        private const float Step = 1f / 120f;

        [Fact]
        public void RisesPastThePeakThenSettlesAndEndsAtZero() {
            var pulse = new FacePulse();
            pulse.Trigger(0.5f, 0.05f, 0.1f, 0.4f, 0.1f);
            float top = 0f;
            float atHoldEnd = 0f;
            for (float time = 0f; time < 0.6f; time += Step) {
                float value = pulse.Step(Step);
                top = System.Math.Max(top, value);
                if (time < 0.15f) atHoldEnd = value;
            }

            Assert.Equal(0.55f, top, 2);
            Assert.Equal(0.5f, atHoldEnd, 2);
            Assert.Equal(0f, pulse.Value);
            Assert.False(pulse.IsActive);
        }

        [Fact]
        public void FastInSlowOut() {
            var pulse = new FacePulse();
            pulse.Trigger(1f, 0.05f, 0f, 0.5f, 0f);
            float rise = TimeAbove(pulse, 0.5f, rising: true);
            float fall = TimeAbove(pulse, 0.5f, rising: false);

            Assert.True(rise < 0.05f);
            Assert.True(fall > 0.2f);
        }

        [Fact]
        public void RetriggerStartsFromTheCurrentValue() {
            var pulse = new FacePulse();
            pulse.Trigger(0.8f, 0.05f, 0.1f, 0.4f, 0f);
            for (int frame = 0; frame < 20; frame++) pulse.Step(Step);
            float before = pulse.Value;
            pulse.Trigger(0.3f, 0.05f, 0.1f, 0.4f, 0f);

            Assert.Equal(before, pulse.Value, 4);
        }

        [Fact]
        public void NonPositivePeakIsIgnored() {
            var pulse = new FacePulse();
            pulse.Trigger(0f, 0.05f, 0.1f, 0.4f, 0.1f);

            Assert.False(pulse.IsActive);
            Assert.Equal(0f, pulse.Step(Step));
        }

        [Fact]
        public void BadStepsAndTimingsNeverStick() {
            var pulse = new FacePulse();
            pulse.Trigger(0.5f, float.NaN, 0.1f, 0.2f, float.NaN);
            pulse.Step(float.NaN);
            pulse.Step(float.PositiveInfinity);
            pulse.Step(-1f);
            Assert.False(float.IsNaN(pulse.Value));

            for (int frame = 0; frame < 60; frame++) pulse.Step(Step);
            Assert.False(pulse.IsActive);
            Assert.Equal(0f, pulse.Value);

            pulse.Trigger(float.NaN, 0.05f, 0.1f, 0.2f, 0f);
            Assert.False(pulse.IsActive);
        }

        [Fact]
        public void ClippedPulseNeverRisesAgain() {
            var pulse = new FacePulse();
            pulse.Trigger(0.8f, 0.05f, 0.1f, 0.4f, 0.1f);
            pulse.Step(Step);
            pulse.Clip(0.2f);
            Assert.Equal(0.2f, pulse.Value);
            float previous = pulse.Value;
            while (pulse.IsActive) {
                float value = pulse.Step(Step);

                Assert.True(value <= previous + 1e-6f, $"{previous} -> {value}");
                previous = value;
            }

            Assert.Equal(0f, pulse.Value);
        }

        [Fact]
        public void FollowerIgnoresBadInputs() {
            var follower = new FaceFollower();
            follower.Step(0.5f, Step, 0.1f, 0.2f);
            float held = follower.Value;
            follower.Step(float.NaN, Step, 0.1f, 0.2f);
            follower.Step(0.5f, float.NaN, float.NaN, 0.2f);

            Assert.Equal(held, follower.Value);
            for (int frame = 0; frame < 240; frame++) follower.Step(0.5f, Step, 0.1f, 0.2f);
            Assert.Equal(0.5f, follower.Value);
        }

        private static float TimeAbove(FacePulse pulse, float level, bool rising) {
            float elapsed = 0f;
            while (pulse.IsActive && (rising ? pulse.Value < level : pulse.Value >= level)) {
                pulse.Step(Step);
                elapsed += Step;
            }

            return elapsed;
        }
    }
}
