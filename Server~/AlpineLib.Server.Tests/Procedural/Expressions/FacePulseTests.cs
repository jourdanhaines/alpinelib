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
