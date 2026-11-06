using System;
using AlpineLib.Procedural.Expressions;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Expressions {
    public sealed class FaceGazeTests {
        private const float Step = 1f / 60f;
        private const float Tolerance = 1e-5f;

        [Fact]
        public void TurnLeadRisesWithTheRate() {
            float slow = Hold(new FaceSignalFrame { TurnRate = 60f }, 0.5f, FaceChannel.LookRight);
            float medium = Hold(new FaceSignalFrame { TurnRate = 180f }, 0.5f, FaceChannel.LookRight);
            float fast = Hold(new FaceSignalFrame { TurnRate = 700f }, 0.5f, FaceChannel.LookRight);
            float creep = Hold(new FaceSignalFrame { TurnRate = 10f }, 0.5f, FaceChannel.LookRight);
            var tuning = new FaceExpressionTuning();

            Assert.Equal(0f, creep);
            Assert.True(slow > 0.05f && medium > slow + 0.15f && fast > medium, $"slow {slow}, medium {medium}, fast {fast}");
            Assert.Equal(tuning.turnLook, fast, 3);
        }

        [Fact]
        public void TurnLeadIsQuickAndEasesBackToExactlyZero() {
            var mixer = Quiet();
            float reachedAt = -1f;
            for (int frame = 0; frame < 30; frame++) {
                mixer.Step(new FaceSignalFrame { TurnRate = 400f }, Step);
                if (reachedAt < 0f && mixer.WeightOf(FaceChannel.LookRight) > 0.5f) reachedAt = frame * Step;
            }

            Assert.InRange(reachedAt, 0f, 0.15f);
            float previous = mixer.WeightOf(FaceChannel.LookRight);
            int settledAt = -1;
            for (int frame = 0; frame < 120 && settledAt < 0; frame++) {
                mixer.Step(new FaceSignalFrame(), Step);
                float now = mixer.WeightOf(FaceChannel.LookRight);
                Assert.True(now <= previous + 0.02f, $"frame {frame}: rose {previous} -> {now}");
                Assert.Equal(0f, mixer.WeightOf(FaceChannel.LookLeft));
                previous = now;
                if (now == 0f) settledAt = frame;
            }

            Assert.InRange(settledAt * Step, 0.2f, 1f);
            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void TurnLeftMirrorsTurnRight() {
            float right = Hold(new FaceSignalFrame { TurnRate = 250f }, 0.4f, FaceChannel.LookRight);
            float left = Hold(new FaceSignalFrame { TurnRate = -250f }, 0.4f, FaceChannel.LookLeft);
            float wrong = Hold(new FaceSignalFrame { TurnRate = -250f }, 0.4f, FaceChannel.LookRight);

            Assert.Equal(right, left, 5);
            Assert.Equal(0f, wrong);
        }

        [Fact]
        public void PitchMapsGentlyWithADeadZone() {
            var tuning = new FaceExpressionTuning();
            float up = Hold(new FaceSignalFrame { LookPitch = -45f }, 1f, FaceChannel.LookUp);
            float down = Hold(new FaceSignalFrame { LookPitch = 45f }, 1f, FaceChannel.LookDown);
            float level = Hold(new FaceSignalFrame { LookPitch = 5f }, 1f, FaceChannel.LookDown);
            float straightUp = Hold(new FaceSignalFrame { LookPitch = -90f }, 1f, FaceChannel.LookUp);
            float straightDown = Hold(new FaceSignalFrame { LookPitch = 90f }, 1f, FaceChannel.LookDown);

            Assert.Equal(0f, level);
            Assert.True(up > 0.3f && down > 0.3f, $"up {up}, down {down}");
            Assert.Equal(tuning.pitchLookUp, straightUp, 3);
            Assert.Equal(tuning.pitchLookDown, straightDown, 3);
            Assert.True(straightUp < 0.9f && straightDown < 0.9f);
            Assert.Equal(0f, Hold(new FaceSignalFrame { LookPitch = -45f }, 1f, FaceChannel.LookDown));
        }

        [Fact]
        public void StrafeGlancesTheWayTheBodySlides() {
            var tuning = new FaceExpressionTuning();
            float right = Hold(new FaceSignalFrame { MoveRight = 1f }, 1f, FaceChannel.LookRight);
            float left = Hold(new FaceSignalFrame { MoveRight = -1f }, 1f, FaceChannel.LookLeft);
            float forward = Hold(new FaceSignalFrame { MoveForward = 1.75f }, 1f, FaceChannel.LookRight)
                + Hold(new FaceSignalFrame { MoveForward = 1.75f }, 1f, FaceChannel.LookLeft);
            float shuffle = Hold(new FaceSignalFrame { MoveRight = 0.1f }, 1f, FaceChannel.LookRight);

            Assert.Equal(tuning.strafeLook, right, 3);
            Assert.Equal(tuning.strafeLook, left, 3);
            Assert.Equal(0f, forward);
            Assert.Equal(0f, shuffle);
            Assert.Equal(0f, Worried(new FaceSignalFrame { MoveRight = 1f }, 1f));
        }

        [Fact]
        public void BackingUpDartsSideToSideAndLooksWorried() {
            var mixer = Quiet();
            float right = 0f;
            float left = 0f;
            for (float time = 0f; time < 4f; time += Step) {
                mixer.Step(new FaceSignalFrame { MoveForward = -1f }, Step);
                right = Math.Max(right, mixer.WeightOf(FaceChannel.LookRight));
                left = Math.Max(left, mixer.WeightOf(FaceChannel.LookLeft));
                Assert.True(mixer.ExpressionSum() <= 1f + Tolerance);
            }

            var tuning = mixer.Tuning;
            Assert.True(right > tuning.backGlance * 0.8f && left > tuning.backGlance * 0.8f, $"right {right}, left {left}");
            Assert.Equal(tuning.backWorried, mixer.WeightOf(FaceChannel.Worried), 2);
            Assert.Equal(tuning.backWide, mixer.WeightOf(FaceChannel.Wide), 2);

            Run(mixer, new FaceSignalFrame(), 2f);
            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void BackingUpDiagonallyGlancesTheSideWalked() {
            var mixer = Quiet();
            float right = 0f;
            for (float time = 0f; time < 4f; time += Step) {
                mixer.Step(new FaceSignalFrame { MoveForward = -0.7f, MoveRight = -0.7f }, Step);
                right = Math.Max(right, mixer.WeightOf(FaceChannel.LookRight));
            }

            Assert.Equal(0f, right);
            Assert.True(mixer.WeightOf(FaceChannel.LookLeft) > mixer.Tuning.strafeLook);
            Assert.True(mixer.WeightOf(FaceChannel.Worried) > 0.1f);
        }

        [Theory]
        [InlineData(-1f)]
        [InlineData(1f)]
        public void StraightBackAfterADiagonalDartsTheSideJustWalkedFirst(float side) {
            var gaze = new FaceGaze();
            var tuning = new FaceExpressionTuning();
            for (float time = 0f; time < tuning.backScanSeconds * 1.5f; time += Step) {
                gaze.Step(new FaceSignalFrame { MoveForward = -0.7f, MoveRight = 0.7f * side }, 0f, Step, tuning);
            }

            for (float time = 0f; time < tuning.backScanSeconds * 0.9f; time += Step) {
                gaze.Step(new FaceSignalFrame { MoveForward = -1f }, 0f, Step, tuning);
                Assert.True(gaze.MoveLook * side > 0f, $"at {time:0.00} s straight back looked {gaze.MoveLook} with the walked side {side}");
            }
        }

        [Fact]
        public void SourcesAddButStayInsideTheSquare() {
            var mixer = Quiet();
            Run(mixer, new FaceSignalFrame { TurnRate = 900f, MoveRight = 2f, LookRight = 1f, LookPitch = -90f, LookUp = 1f }, 1f);

            Assert.Equal(1f, mixer.WeightOf(FaceChannel.LookRight));
            Assert.Equal(1f, mixer.WeightOf(FaceChannel.LookUp));
            Assert.Equal(0f, mixer.WeightOf(FaceChannel.LookLeft));
            Assert.Equal(0f, mixer.WeightOf(FaceChannel.LookDown));
        }

        [Fact]
        public void LooksNeverLeaveTheSquareUnderAnySignals() {
            var mixer = new FaceExpressionMixer(new FaceExpressionTuning(), 9u);
            var random = new Random(11);
            float air = 0f;
            for (int frame = 0; frame < 20000; frame++) {
                var signals = RandomSignals(random, ref air);
                mixer.Step(signals, random.NextDouble() < 0.01 ? (float)random.NextDouble() : Step);

                foreach (float weight in mixer.Weights) Assert.InRange(weight, 0f, 1f);
                Assert.True(mixer.WeightOf(FaceChannel.LookLeft) == 0f || mixer.WeightOf(FaceChannel.LookRight) == 0f);
                Assert.True(mixer.WeightOf(FaceChannel.LookUp) == 0f || mixer.WeightOf(FaceChannel.LookDown) == 0f);
                Assert.True(mixer.ExpressionSum() <= mixer.Tuning.expressionBudget + Tolerance, $"frame {frame}: sum {mixer.ExpressionSum()}");
            }
        }

        [Fact]
        public void BlinkIsTheSameWhateverTheEyesLookAt() {
            var still = new FaceExpressionMixer(new FaceExpressionTuning(), 21u);
            var busy = new FaceExpressionMixer(new FaceExpressionTuning(), 21u);
            var random = new Random(4);
            int blinking = 0;
            for (int frame = 0; frame < 3000; frame++) {
                still.Step(new FaceSignalFrame(), Step);
                busy.Step(new FaceSignalFrame {
                    TurnRate = (float)(random.NextDouble() * 1400 - 700), LookPitch = (float)(random.NextDouble() * 180 - 90),
                    MoveRight = (float)(random.NextDouble() * 2 - 1), MoveForward = (float)(random.NextDouble() * 2 - 1),
                }, Step);

                Assert.Equal(still.WeightOf(FaceChannel.Blink), busy.WeightOf(FaceChannel.Blink));
                if (busy.WeightOf(FaceChannel.Blink) > 0.87f) blinking++;
            }

            Assert.True(blinking > 0, "never shut in 50 s");
        }

        [Fact]
        public void BackingUpStaysInsideTheBudgetThroughALanding() {
            var mixer = Quiet();
            float air = 0f;
            for (float time = 0f; time < 1.6f; time += Step) {
                air += Step;
                mixer.Step(new FaceSignalFrame { Airborne = true, AirTime = air, MoveForward = -1f }, Step);
            }

            mixer.Step(new FaceSignalFrame { Landed = true, LandAirTime = air, MoveForward = -1f }, Step);
            float squash = 0f;
            for (float time = 0f; time < 1.5f; time += Step) {
                mixer.Step(new FaceSignalFrame { MoveForward = -1f, Jumped = time > 0.3f && time < 0.3f + Step }, Step);
                squash = Math.Max(squash, mixer.WeightOf(FaceChannel.Squash));
                Assert.True(mixer.ExpressionSum() <= 1f + Tolerance, $"t {time}: sum {mixer.ExpressionSum()}");
            }

            Assert.True(squash >= mixer.Tuning.landSquashMax * 0.95f, $"squash {squash}");
            Assert.Equal(mixer.Tuning.backWorried, mixer.WeightOf(FaceChannel.Worried), 2);
        }

        [Fact]
        public void SheetGlanceCombinesWithPitch() {
            var tuning = new FaceExpressionTuning();
            float sheet = Hold(new FaceSignalFrame { GlanceDown = 1f }, 1f, FaceChannel.LookDown);
            float sheetLookingDown = Hold(new FaceSignalFrame { GlanceDown = 1f, LookPitch = 60f }, 1f, FaceChannel.LookDown);
            float sheetLookingUp = Hold(new FaceSignalFrame { GlanceDown = 1f, LookPitch = -60f }, 1f, FaceChannel.LookDown);
            float upAlone = Hold(new FaceSignalFrame { LookPitch = -60f }, 1f, FaceChannel.LookUp);

            Assert.Equal(tuning.glanceLookDown, sheet, 2);
            Assert.True(sheetLookingDown > sheet + 0.1f && sheetLookingDown <= 1f, $"sheet {sheet}, looking down {sheetLookingDown}");
            Assert.True(sheetLookingUp > 0.2f && sheetLookingUp < sheet, $"sheet {sheet}, looking up {sheetLookingUp}");
            Assert.True(upAlone > 0.3f);
        }

        [Fact]
        public void BadLookSignalsNeverPoisonTheWeights() {
            var mixer = Quiet();
            mixer.Step(new FaceSignalFrame { TurnRate = float.NaN, LookPitch = float.NaN, MoveRight = float.NaN, MoveForward = float.PositiveInfinity }, Step);
            mixer.Step(new FaceSignalFrame { TurnRate = float.PositiveInfinity, LookPitch = float.NegativeInfinity, MoveRight = 1f }, float.NaN);
            mixer.Step(new FaceSignalFrame { TurnRate = 300f }, Step);

            foreach (float weight in mixer.Weights) Assert.False(float.IsNaN(weight));
            Run(mixer, new FaceSignalFrame(), 2f);
            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void ResetLooksAheadAtOnce() {
            var mixer = Quiet();
            Run(mixer, new FaceSignalFrame { TurnRate = 300f, LookPitch = 50f, MoveForward = -1f }, 1f);
            mixer.Reset();

            Assert.True(mixer.IsAtRest());
            mixer.Step(new FaceSignalFrame(), Step);
            Assert.True(mixer.IsAtRest());
        }

        [Fact]
        public void SpringHoldsOnBadInputAndSnapsToTarget() {
            var spring = new FaceSpring();
            spring.Step(1f, Step, 0.1f, 0.2f);
            float value = spring.Value;

            Assert.Equal(value, spring.Step(float.NaN, Step, 0.1f, 0.2f));
            Assert.Equal(value, spring.Step(0f, float.NaN, 0.1f, 0.2f));
            for (int frame = 0; frame < 120; frame++) spring.Step(0.5f, Step, 0.1f, 0.2f);
            Assert.Equal(0.5f, spring.Value);
            Assert.Equal(0f, spring.Velocity);
            Assert.Equal(-1f, spring.Step(-1f, Step, 0f, 0f));
        }

        private static FaceSignalFrame RandomSignals(Random random, ref float air) {
            var signals = new FaceSignalFrame {
                Airborne = random.NextDouble() < 0.5, Jumped = random.NextDouble() < 0.05, Landed = random.NextDouble() < 0.05,
                LandAirTime = (float)random.NextDouble() * 3f, Exertion = (float)random.NextDouble() * 1.5f, GlanceDown = (float)random.NextDouble(),
                LookRight = (float)(random.NextDouble() * 4 - 2), LookUp = (float)(random.NextDouble() * 4 - 2),
                TurnRate = (float)(random.NextDouble() * 3000 - 1500), LookPitch = (float)(random.NextDouble() * 200 - 100),
                MoveRight = (float)(random.NextDouble() * 4 - 2), MoveForward = (float)(random.NextDouble() * 4 - 2),
            };
            air = signals.Airborne ? air + Step : 0f;
            signals.AirTime = air;
            return signals;
        }

        // Blinks pushed far away so the looks read alone.
        private static FaceExpressionMixer Quiet() {
            var tuning = new FaceExpressionTuning { blinkIntervalMin = 1000f, blinkIntervalMax = 1000f };
            return new FaceExpressionMixer(tuning, 1u);
        }

        private static float Hold(FaceSignalFrame signals, float seconds, FaceChannel watched) {
            var mixer = Quiet();
            Run(mixer, signals, seconds);
            return mixer.WeightOf(watched);
        }

        private static float Worried(FaceSignalFrame signals, float seconds) {
            var mixer = Quiet();
            Run(mixer, signals, seconds);
            return mixer.WeightOf(FaceChannel.Worried);
        }

        private static void Run(FaceExpressionMixer mixer, FaceSignalFrame signals, float seconds) {
            for (float time = 0f; time < seconds; time += Step) mixer.Step(signals, Step);
        }
    }
}
