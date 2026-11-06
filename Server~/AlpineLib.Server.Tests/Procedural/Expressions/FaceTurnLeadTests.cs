using System;
using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    /// <summary>The turn lead from a sampled facing: it depends on how fast the body turns, not on how it is polled.</summary>
    public sealed class FaceTurnLeadTests {
        private const float MousePollHertz = 48f;

        [Theory]
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
        [InlineData(0f)]
        [InlineData(0.15f)]
        public void SpringNeverOvershootsADroppingTarget(float trackBand) {
            foreach (float drop in new[] { 0.5f, 0.3f, 0.1f, 0f, -0.3f }) {
                var spring = new FaceSpring();
                for (int frame = 0; frame < 3; frame++) spring.Step(0.75f, 1f / 60f, 0.06f, 0.25f, trackBand);

                float atDrop = spring.Value;
                float low = Math.Min(drop, atDrop);
                for (int frame = 0; frame < 120; frame++) {
                    float now = spring.Step(drop, 1f / 60f, 0.06f, 0.25f, trackBand);
                    Assert.True(now <= Math.Max(atDrop, drop) + 1e-6f, $"drop {drop}: rose {atDrop} -> {now}");
                    Assert.True(now >= low - 1e-6f, $"drop {drop}: undershot to {now}");
                }

                Assert.Equal(drop, spring.Value);
            }
        }

        [Fact]
        public void SpringSettlesSmallDipsAtTheOutTimeOnlyInsideTheBand() {
            var banded = new FaceSpring();
            var plain = new FaceSpring();
            for (int frame = 0; frame < 120; frame++) {
                banded.Step(0.6f, 1f / 60f, 0.02f, 0.25f, 0.15f);
                plain.Step(0.6f, 1f / 60f, 0.02f, 0.25f);
            }

            for (int frame = 0; frame < 6; frame++) {
                banded.Step(0.5f, 1f / 60f, 0.02f, 0.25f, 0.15f);
                plain.Step(0.5f, 1f / 60f, 0.02f, 0.25f);
            }

            Assert.Equal(0.5f, banded.Value);
            Assert.True(plain.Value > 0.55f, $"plain {plain.Value}");
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
