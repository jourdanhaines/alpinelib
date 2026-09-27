using System.Numerics;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Client-side buffers moved by a rebase must sample exactly as an unshifted twin fed the same motion
    /// in the old frame, offset by the delta: no jump, no recovery kick.
    /// </summary>
    public sealed class OriginBufferShiftTests {
        private const double TickSeconds = 1.0 / 30.0;
        private const float Speed = 10f;
        private const float Tolerance = 0.002f;

        private static readonly Vector3 Delta = new Vector3(-128f, 0f, 256f);

        [Fact]
        public void InterpolatedOutputIsContinuousAcrossAShift() {
            var reference = new StateInterpolator(TickSeconds, 32);
            var shifted = new StateInterpolator(TickSeconds, 32);

            PushBoth(reference, shifted, 1, 10, Vector3.Zero);
            AssertSameAfterDelta(reference, shifted, 5.5, Vector3.Zero);

            shifted.TranslateWorldFrame(Delta);

            for (int tick = 11; tick <= 20; tick++) {
                PushBoth(reference, shifted, tick, tick, Delta);
                AssertSameAfterDelta(reference, shifted, tick - 4.25, Delta);
            }
        }

        [Fact]
        public void RecoveryFromExtrapolationSurvivesAShift() {
            var reference = new StateInterpolator(TickSeconds, 32);
            var shifted = new StateInterpolator(TickSeconds, 32);

            PushBoth(reference, shifted, 1, 10, Vector3.Zero);
            AssertSameAfterDelta(reference, shifted, 12.0, Vector3.Zero);

            shifted.TranslateWorldFrame(Delta);
            PushBoth(reference, shifted, 11, 16, Delta);

            for (int step = 0; step < 8; step++) {
                AssertSameAfterDelta(reference, shifted, 12.25 + step * 0.25, Delta);
            }
        }

        [Fact]
        public void CarrierRelativeSamplesStayPut() {
            var interpolator = new StateInterpolator(TickSeconds, 32);
            var riding = new PawnState(new Vector3(0.5f, 1f, 2f), 0f, Vector3.Zero, 0, 9);
            interpolator.Push(1u, in riding);
            interpolator.Push(2u, in riding);

            interpolator.TranslateWorldFrame(Delta);

            Assert.True(interpolator.Sample(1.5 * TickSeconds, out PawnState sampled));
            Assert.Equal(riding.Position, sampled.Position);
        }

        [Fact]
        public void PredictionsMoveWithTheFrame() {
            var buffer = new PredictionBuffer();
            var input = new PawnInput(3u, Vector2.Zero, WireLocomotion.Walk, false, false);
            buffer.Record(in input, Walker(1));

            buffer.TranslateWorldFrame(Delta);

            Assert.True(buffer.TryGetPredictedState(3u, out PawnState predicted));
            Assert.Equal(Walker(1).Position + Delta, predicted.Position);
        }

        private static void PushBoth(StateInterpolator reference, StateInterpolator shifted, int firstTick, int lastTick, Vector3 shiftedOffset) {
            for (int tick = firstTick; tick <= lastTick; tick++) {
                PawnState state = Walker(tick);
                reference.Push((uint)tick, in state);
                shifted.Push((uint)tick, state.WithOriginShift(shiftedOffset));
            }
        }

        private static void AssertSameAfterDelta(StateInterpolator reference, StateInterpolator shifted, double renderTick, Vector3 offset) {
            double renderSeconds = renderTick * TickSeconds;
            Assert.True(reference.Sample(renderSeconds, out PawnState expected));
            Assert.True(shifted.Sample(renderSeconds, out PawnState actual));

            Vector3 error = actual.Position - (expected.Position + offset);
            Assert.True(error.Length() < Tolerance, "Shifted output strayed " + error.Length().ToString("0.0000") + " m at tick " + renderTick.ToString("0.00") + ".");
        }

        private static PawnState Walker(int tick) {
            var position = new Vector3(100f + Speed * (float)(tick * TickSeconds), 0f, 40f);
            return new PawnState(position, 0f, new Vector3(Speed, 0f, 0f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
        }
    }
}
