using System;
using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    /// <summary>The turn lead from a sampled facing: it depends on how fast the body turns, not on how it is polled.</summary>
    public sealed class FaceTurnLeadTests {
        private const float MousePollHertz = 48f;

        [Theory]
        [InlineData(20, 90f)]
        [InlineData(20, 180f)]
        [InlineData(30, 90f)]
        [InlineData(30, 180f)]
        [InlineData(60, 90f)]
        [InlineData(60, 180f)]
        [InlineData(144, 90f)]
        [InlineData(144, 180f)]
        [InlineData(240, 90f)]
        [InlineData(240, 180f)]
        public void NoisyOrCoarselyPolledTurnsLeadLikeASmoothOne(int framesPerSecond, float degreesPerSecond) {
            float smooth = SteadyLead(framesPerSecond, (time, step, random) => degreesPerSecond * step);
            float polled = SteadyLead(framesPerSecond, (time, step, random) => PolledTurn(time, step, degreesPerSecond));
            float noisy = SteadyLead(framesPerSecond, (time, step, random) => Math.Max(0f, degreesPerSecond + Gaussian(random) * 60f) * step);

            Assert.True(smooth > 0.2f, $"smooth {smooth}");
            Assert.InRange(polled / smooth, 0.9f, 1.1f);
            Assert.InRange(noisy / smooth, 0.9f, 1.1f);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(360)]
        [InlineData(1000)]
        public void LeadIsTheSameAtAnyFrameRate(int framesPerSecond) {
            float reference = SteadyLead(60, (time, step, random) => 180f * step);
            float lead = SteadyLead(framesPerSecond, (time, step, random) => 180f * step);

            Assert.InRange(lead / reference, 0.98f, 1.02f);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void AFlickStopsRisingOnceTheTurnStops(int framesPerSecond) {
            var rig = new LeadRig(framesPerSecond);
            int flickFrames = Math.Max(3, (int)Math.Round(0.05f * framesPerSecond));
            for (int frame = 0; frame < flickFrames; frame++) rig.Turn(400f * rig.StepSeconds);

            float atStop = rig.Gaze.TurnLook;
            float ceiling = FaceGaze.TurnTarget(rig.Gaze.SmoothedTurnRate, rig.Tuning);
            float previous = atStop;
            bool falling = false;
            for (int frame = 0; frame < framesPerSecond * 2; frame++) {
                float now = rig.Turn(0f);
                Assert.True(now <= ceiling + 1e-5f, $"frame {frame}: {now} above the lead the turn reached {ceiling}");
                Assert.True(now <= atStop + 0.03f, $"frame {frame}: rose {atStop} -> {now} after the turn stopped");
                if (falling) Assert.True(now <= previous, $"frame {frame}: rose again {previous} -> {now}");
                falling |= now < previous;
                previous = now;
            }

            Assert.True(atStop > 0.2f, $"the flick led only {atStop}");
            Assert.Equal(0f, rig.Gaze.TurnLook);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void AimTremorDoesNotTwitchTheEyes(int framesPerSecond) {
            var rig = new LeadRig(framesPerSecond);
            float[] tremor = Tremor(framesPerSecond * 30, rig.StepSeconds, 40f);
            float worst = 0f;
            foreach (float rate in tremor) worst = Math.Max(worst, Math.Abs(rig.Turn(rate * rig.StepSeconds)));

            Assert.True(worst < 0.03f, $"tremor looked {worst}");
        }

        [Theory]
        [InlineData(20, false)]
        [InlineData(30, false)]
        [InlineData(60, false)]
        [InlineData(144, false)]
        [InlineData(240, false)]
        [InlineData(144, true)]
        [InlineData(240, true)]
        public void PerFrameAimJitterDoesNotTwitchTheEyes(int framesPerSecond, bool gaussian) {
            var rig = new LeadRig(framesPerSecond);
            var random = new Random(11);
            float worst = 0f;
            for (int frame = 0; frame < framesPerSecond * 30; frame++) {
                float rate = gaussian ? Gaussian(random) * 40f : (float)(random.NextDouble() * 80 - 40);
                worst = Math.Max(worst, Math.Abs(rig.Turn(rate * rig.StepSeconds)));
            }

            Assert.True(worst < 0.03f, $"jitter looked {worst}");
        }

        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void TheReturnAfterATurnIsTheSameAtAnyFrameRate(int framesPerSecond) {
            float[] reference = ReturnTimes(500, 0.6f, 0.4f, 0.2f, 0.05f);
            float[] times = ReturnTimes(framesPerSecond, 0.6f, 0.4f, 0.2f, 0.05f);

            for (int index = 0; index < times.Length; index++) {
                float ratio = times[index] / reference[index];
                Assert.True(ratio > 0.85f && ratio < 1.15f, $"level {index}: {times[index]:F3} s vs {reference[index]:F3} s at 500 fps");
            }
        }

        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void TheReturnDecaysOnOneSmoothCurve(int framesPerSecond) {
            float[] times = ReturnTimes(framesPerSecond, 0.45f, 0.15f, 0.05f);
            float tail = (times[2] - times[1]) / (times[1] - times[0]);
            Assert.InRange(tail, 0.6f, 1.5f);

            var rig = new LeadRig(framesPerSecond);
            for (int frame = 0; frame < framesPerSecond / 2; frame++) rig.Turn(300f * rig.StepSeconds);

            // Once falling it never rises, and below 0.6 its speed never drops faster than a 30 ms ease would allow.
            float minimumKeep = (float)Math.Exp(-rig.StepSeconds / 0.03f);
            float previous = rig.Gaze.TurnLook;
            float previousSpeed = 0f;
            for (int frame = 0; frame < framesPerSecond * 2 && previous > 0.05f; frame++) {
                float now = rig.Turn(0f);
                float speed = (previous - now) / rig.StepSeconds;
                Assert.True(now <= previous + 1e-5f, $"frame {frame}: rose {previous} -> {now}");
                bool collapsed = now > 0.05f && now < 0.6f && previousSpeed > 0.5f && speed < previousSpeed * minimumKeep;
                Assert.False(collapsed, $"frame {frame} at {now}: easing back slowed {previousSpeed} -> {speed}/s");
                previous = now;
                previousSpeed = speed;
            }
        }

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        public void AReversedTurnSwingsAcrossWithoutAJump(int framesPerSecond) {
            var rig = new LeadRig(framesPerSecond);
            for (int frame = 0; frame < framesPerSecond / 2; frame++) rig.Turn(300f * rig.StepSeconds);

            float previous = rig.Gaze.TurnLook;
            float fastest = 0f;
            for (int frame = 0; frame < framesPerSecond; frame++) {
                float now = rig.Turn(-300f * rig.StepSeconds);
                fastest = Math.Max(fastest, Math.Abs(now - previous) / rig.StepSeconds);
                previous = now;
            }

            Assert.True(previous < -0.7f, $"reversed lead {previous}");
            Assert.True(fastest < 10f, $"swung across at {fastest}/s");
        }

        [Fact]
        public void SpringNeverOvershootsADroppingTarget() {
            foreach (float drop in new[] { 0.5f, 0.3f, 0.1f, 0f, -0.3f }) {
                var spring = new FaceSpring();
                for (int frame = 0; frame < 3; frame++) spring.Step(0.75f, 1f / 60f, 0.06f, 0.25f);

                float atDrop = spring.Value;
                float low = Math.Min(drop, atDrop);
                for (int frame = 0; frame < 120; frame++) {
                    float now = spring.Step(drop, 1f / 60f, 0.06f, 0.25f);
                    Assert.True(now <= Math.Max(atDrop, drop) + 1e-6f, $"drop {drop}: rose {atDrop} -> {now}");
                    Assert.True(now >= low - 1e-6f, $"drop {drop}: undershot to {now}");
                }

                Assert.Equal(drop, spring.Value);
            }
        }

        // Seconds after a half-second 300 deg/s turn stops until the lead falls under each level, between frames.
        private static float[] ReturnTimes(int framesPerSecond, params float[] levels) {
            var rig = new LeadRig(framesPerSecond);
            int turnFrames = framesPerSecond / 2;
            for (int frame = 0; frame < turnFrames; frame++) rig.Turn(300f * rig.StepSeconds);

            var times = new float[levels.Length];
            float previous = rig.Gaze.TurnLook;
            for (int frame = 1; frame <= framesPerSecond * 2; frame++) {
                float now = rig.Turn(0f);
                RecordCrossings(levels, times, previous, now, frame, rig.StepSeconds);
                previous = now;
            }

            foreach (float time in times) Assert.True(time > 0f, $"never eased back under a level at {framesPerSecond} fps");
            return times;
        }

        private static void RecordCrossings(float[] levels, float[] times, float before, float after, int frame, float step) {
            for (int index = 0; index < levels.Length; index++) {
                if (times[index] > 0f || before < levels[index] || after >= levels[index]) continue;

                times[index] = (frame - 1 + (before - levels[index]) / (before - after)) * step;
            }
        }

        // Hand tremor: five random 4-12 Hz waves, scaled so the turn rate peaks at exactly the given rate.
        private static float[] Tremor(int frames, float step, float peakDegreesPerSecond) {
            var random = new Random(7);
            var waves = new double[5, 3];
            for (int wave = 0; wave < 5; wave++) {
                waves[wave, 0] = 4 + random.NextDouble() * 8;
                waves[wave, 1] = random.NextDouble() * Math.PI * 2;
                waves[wave, 2] = 0.5 + random.NextDouble() * 0.5;
            }

            var rates = new double[frames];
            double peak = 0;
            for (int frame = 0; frame < frames; frame++) {
                for (int wave = 0; wave < 5; wave++) rates[frame] += waves[wave, 2] * Math.Sin(2 * Math.PI * waves[wave, 0] * frame * step + waves[wave, 1]);
                peak = Math.Max(peak, Math.Abs(rates[frame]));
            }

            return Array.ConvertAll(rates, (rate) => (float)(rate / peak * peakDegreesPerSecond));
        }

        // A mouse that reports at 48 Hz: the facing moves in steps, whatever the frame rate.
        private static float PolledTurn(float time, float step, float degreesPerSecond) {
            double before = Math.Floor(time * MousePollHertz);
            double after = Math.Floor((time + step) * MousePollHertz);
            return (float)((after - before) / MousePollHertz * degreesPerSecond);
        }

        private static float Gaussian(Random random) {
            double first = 1.0 - random.NextDouble();
            double second = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(first)) * Math.Cos(2.0 * Math.PI * second));
        }

        // Mean lead over the last ten seconds of a twelve-second turn.
        private static float SteadyLead(int framesPerSecond, Func<float, float, Random, float> turnOfFrame) {
            var rig = new LeadRig(framesPerSecond);
            var random = new Random(3);
            int frames = framesPerSecond * 12;
            double sum = 0;
            int counted = 0;
            for (int frame = 0; frame < frames; frame++) {
                float lead = rig.Turn(turnOfFrame(frame * rig.StepSeconds, rig.StepSeconds, random));
                if (frame < framesPerSecond * 2) continue;

                sum += lead;
                counted++;
            }

            return (float)(sum / counted);
        }

        /// <summary>A facing sampled every frame, through the tracker and the gaze the way the face driver runs them.</summary>
        private sealed class LeadRig {
            private readonly FaceTurnTracker _tracker = new FaceTurnTracker();
            private float _yaw;

            public LeadRig(int framesPerSecond) {
                StepSeconds = 1f / framesPerSecond;
                _tracker.Sample(_yaw, StepSeconds);
            }

            public float StepSeconds { get; }
            public FaceGaze Gaze { get; } = new FaceGaze();
            public FaceExpressionTuning Tuning { get; } = new FaceExpressionTuning();

            public float Turn(float degrees) {
                _yaw += degrees;
                var signals = new FaceSignalFrame { TurnRate = _tracker.Sample(_yaw, StepSeconds) };
                Gaze.Step(in signals, 0f, StepSeconds, Tuning);
                return Gaze.TurnLook;
            }
        }
    }
}
