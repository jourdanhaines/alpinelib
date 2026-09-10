using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A whole ride, sampled frame by frame: a pawn walks the world, boards a carrier, rides it and steps
    /// off, with the stream stalling either side of both transitions.
    /// </summary>
    /// <remarks>
    /// The single-transition tests next door each hold one mechanism still. This one runs the mechanisms
    /// together, because the failure worth catching is cumulative: an extrapolation debt taken on in one
    /// frame and paid back in another leaks a constant offset that no single-step assertion would see,
    /// and it survives every later sample until something clears it.
    /// </remarks>
    public sealed class StateInterpolatorCarrierSequenceTests {
        private const double TickInterval = 1.0 / 30.0;
        private const int Capacity = 32;
        private const ushort TrainCarrierId = 3;
        private const double FrameSeconds = 1.0 / 60.0;

        [Fact]
        public void AWorldToCarrierToWorldRideLeavesNoResidualOffsetBehind() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);

            // Walking the world, then aboard the deck, then back on solid ground far down the line.
            interpolator.Push(10u, Moving(new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f)));
            interpolator.Push(12u, Moving(new Vector3(0.13f, 0f, 0f), new Vector3(2f, 0f, 0f)));

            // Stall: the render clock runs past the newest sample and the pawn is projected forward.
            SampleAcross(interpolator, 12.0, 15.0);

            interpolator.Push(16u, Moving(new Vector3(0.5f, 0f, 1f), Vector3.Zero, TrainCarrierId));
            interpolator.Push(18u, Moving(new Vector3(0.5f, 0f, 1.2f), new Vector3(0f, 0f, 3f), TrainCarrierId));
            SampleAcross(interpolator, 15.0, 18.0);

            // Stall again while aboard, then step off onto the world a long way from where it boarded.
            SampleAcross(interpolator, 18.0, 20.0);
            interpolator.Push(21u, Moving(new Vector3(400f, 0f, 60f), new Vector3(2f, 0f, 0f)));
            interpolator.Push(23u, Moving(new Vector3(400.13f, 0f, 60f), new Vector3(2f, 0f, 0f)));

            PawnState ridden = SampleAcross(interpolator, 20.0, 22.0);

            var clean = new StateInterpolator(TickInterval, Capacity);
            clean.Push(21u, Moving(new Vector3(400f, 0f, 60f), new Vector3(2f, 0f, 0f)));
            clean.Push(23u, Moving(new Vector3(400.13f, 0f, 60f), new Vector3(2f, 0f, 0f)));
            PawnState fresh = SampleAcross(clean, 21.0, 22.0);

            Assert.Equal(PawnState.WorldCarrierId, ridden.CarrierId);
            Assert.Equal(fresh.Position.X, ridden.Position.X, 4);
            Assert.Equal(fresh.Position.Z, ridden.Position.Z, 4);
        }

        [Fact]
        public void ALateWorldSampleSplicedBetweenTwoDeckSamplesIsNotBlendedIntoEither() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            interpolator.Push(10u, Moving(new Vector3(0.5f, 0f, 1f), Vector3.Zero, TrainCarrierId));
            interpolator.Push(14u, Moving(new Vector3(0.5f, 0f, 2f), Vector3.Zero, TrainCarrierId));

            // A keyframe from the tick the pawn was briefly ashore, arriving after both deck snapshots.
            interpolator.Push(12u, Moving(new Vector3(300f, 0f, 40f), Vector3.Zero));

            Assert.Equal(3, interpolator.Count);
            Assert.Equal(14u, interpolator.NewestTick);

            Assert.True(interpolator.Sample(11.0 * TickInterval, out PawnState acrossTheBoarding));
            Assert.Equal(PawnState.WorldCarrierId, acrossTheBoarding.CarrierId);
            Assert.Equal(300f, acrossTheBoarding.Position.X, 3);

            Assert.True(interpolator.Sample(13.0 * TickInterval, out PawnState backAboard));
            Assert.Equal(TrainCarrierId, backAboard.CarrierId);
            Assert.Equal(2f, backAboard.Position.Z, 3);
        }

        [Fact]
        public void AnOlderSampleOnAnotherCarrierNeverOverwritesTheNewestFrame() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            interpolator.Push(20u, Moving(new Vector3(1f, 0f, 1f), Vector3.Zero, TrainCarrierId));
            interpolator.Push(19u, Moving(new Vector3(9f, 0f, 9f), Vector3.Zero));

            Assert.Equal(20u, interpolator.NewestTick);
            Assert.True(interpolator.Sample(40.0 * TickInterval, out PawnState extrapolated));
            Assert.Equal(TrainCarrierId, extrapolated.CarrierId);
        }

        /// <summary>
        /// Renders every frame between two tick times, the way a client does, and hands back the last
        /// pose. Sampling in one leap would skip the frames a recovery offset decays over.
        /// </summary>
        private static PawnState SampleAcross(StateInterpolator interpolator, double fromTicks, double toTicks) {
            double fromSeconds = fromTicks * TickInterval;
            double toSeconds = toTicks * TickInterval;
            PawnState last = default;

            for (double seconds = fromSeconds; seconds <= toSeconds + 1e-9; seconds += FrameSeconds) {
                Assert.True(interpolator.Sample(seconds, out last), "The interpolator had nothing to render.");
            }

            return last;
        }

        private static PawnState Moving(Vector3 position, Vector3 velocity) {
            return Moving(position, velocity, PawnState.WorldCarrierId);
        }

        private static PawnState Moving(Vector3 position, Vector3 velocity, ushort carrierId) {
            return new PawnState(
                position,
                0f,
                velocity,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }
    }
}
