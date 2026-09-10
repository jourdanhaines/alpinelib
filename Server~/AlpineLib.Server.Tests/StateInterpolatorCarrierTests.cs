using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the interpolator must refuse to do once samples can name a frame: blend between two of them,
    /// borrow a world-space platform's motion for one of them, or carry an extrapolation debt across the
    /// moment a pawn boards.
    /// </summary>
    /// <remarks>
    /// Each of these fails silently in the wrong direction — a rider drawn between two origins, a ride
    /// applied twice, a pawn dragged along the deck for the length of the smoothing window — so every
    /// case is pinned against a control that shows the mechanism is really what moved the numbers.
    /// </remarks>
    public sealed class StateInterpolatorCarrierTests {
        private const double TickInterval = 1.0 / 30.0;
        private const int Capacity = 32;
        private const ushort TrainCarrierId = 1;

        /// <summary>Deck height of the fixture platform: its path height plus half its thickness.</summary>
        private const float DeckHeight = 0.5f;

        /// <summary>Z the fixture platform runs along.</summary>
        private const float PlatformPathZ = 4f;

        /// <summary>The sample every stall test extrapolates from: moving fast enough to build a real debt.</summary>
        private static readonly PawnState Stalled =
            Grounded(new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 0f), PawnState.WorldCarrierId);

        [Fact]
        public void SamplesEitherSideOfABoardingAreNotBlendedTogether() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            PawnState onTheGround = Grounded(new Vector3(0f, 0f, 0f), Vector3.Zero, PawnState.WorldCarrierId);
            PawnState onTheDeck = Grounded(new Vector3(2f, 0f, 0f), Vector3.Zero, TrainCarrierId);

            interpolator.Push(10u, in onTheGround);
            interpolator.Push(12u, in onTheDeck);

            Assert.True(interpolator.Sample(11.0 * TickInterval, out PawnState sampled));

            Assert.Equal(TrainCarrierId, sampled.CarrierId);
            Assert.Equal(onTheDeck.Position.X, sampled.Position.X, 5);
        }

        [Fact]
        public void ACarrierRelativeSampleBorrowsNoMoverCarry() {
            CollisionWorld world = BuildPlatformWorld();
            StateInterpolator inTheWorld = BuildRiderInterpolator(world, PawnState.WorldCarrierId);
            StateInterpolator onTheCarrier = BuildRiderInterpolator(world, TrainCarrierId);

            double aheadSeconds = 41.0 * TickInterval + 0.05;

            Assert.True(inTheWorld.Sample(aheadSeconds, out PawnState carried));
            Assert.True(onTheCarrier.Sample(aheadSeconds, out PawnState uncarried));

            // The rider reports zero velocity either way, so anything the world-frame pawn gained came
            // from the platform's own delta — exactly the helping the carrier-relative pawn must not get.
            Assert.True(carried.Position.X != 0f, "The control did not pick up any mover carry; the test proves nothing.");
            Assert.Equal(0f, uncarried.Position.X, 5);
        }

        [Fact]
        public void ExtrapolationDebtIsDroppedWhenTheOutputChangesFrame() {
            StateInterpolator crossing = BuildStalledInterpolator();
            StateInterpolator staying = BuildStalledInterpolator();
            var reference = new StateInterpolator(TickInterval, Capacity);

            PawnState resumeOnCarrier = Grounded(new Vector3(0.2f, 0f, 0f), Vector3.Zero, TrainCarrierId);
            PawnState resumeInWorld = Grounded(new Vector3(0.2f, 0f, 0f), Vector3.Zero, PawnState.WorldCarrierId);

            crossing.Push(13u, in resumeOnCarrier);
            staying.Push(13u, in resumeInWorld);
            reference.Push(10u, in Stalled);
            reference.Push(13u, in resumeInWorld);

            double resumeSeconds = 12.0 * TickInterval;
            Assert.True(crossing.Sample(resumeSeconds, out PawnState crossed));
            Assert.True(staying.Sample(resumeSeconds, out PawnState stayed));
            Assert.True(reference.Sample(resumeSeconds, out PawnState clean));

            Assert.Equal(TrainCarrierId, crossed.CarrierId);
            Assert.Equal(resumeOnCarrier.Position.X, crossed.Position.X, 5);
            Assert.True(
                MathF.Abs(stayed.Position.X - clean.Position.X) > 0.1f,
                "The control carried no extrapolation debt, so dropping one proves nothing.");
        }

        /// <summary>
        /// An interpolator that has just come off an extrapolation episode, so its next interpolated
        /// frame owes a recovery offset.
        /// </summary>
        private static StateInterpolator BuildStalledInterpolator() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            interpolator.Push(10u, in Stalled);
            interpolator.Sample(11.5 * TickInterval, out PawnState _);

            return interpolator;
        }

        /// <summary>
        /// One grounded, motionless rider standing on the fixture platform's deck, in the frame asked
        /// for, with a carry world attached so the mover probe has something to find.
        /// </summary>
        private static StateInterpolator BuildRiderInterpolator(CollisionWorld world, ushort carrierId) {
            var interpolator = new StateInterpolator(TickInterval, Capacity) { CarryWorld = world };
            PawnState rider = Grounded(new Vector3(0f, DeckHeight, PlatformPathZ), Vector3.Zero, carrierId);
            interpolator.Push(41u, in rider);

            return interpolator;
        }

        private static PawnState Grounded(Vector3 position, Vector3 velocity, ushort carrierId) {
            return new PawnState(
                position,
                0f,
                velocity,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }

        /// <summary>
        /// A floor plus one ping-pong platform travelling on X, the smallest world in which a mover
        /// carry probe can answer anything at all.
        /// </summary>
        private static CollisionWorld BuildPlatformWorld() {
            CollisionShape deck = CollisionShape.MakeBox(
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(1.5f, 0.1f, 1.5f));

            var waypoints = new[] {
                new Vector3(-2f, 0.4f, PlatformPathZ),
                new Vector3(2f, 0.4f, PlatformPathZ)
            };

            var path = new MoverPath(waypoints, 1f, MoverLoopMode.PingPong, 0u);
            var movers = new[] { new MoverDefinition(1, 1, in deck, path) };
            var geometry = new SceneGeometry(
                "CarrierTestPlatform",
                0u,
                new[] { CollisionShape.MakePlane(0f) },
                movers);

            return new CollisionWorld(geometry, (float)TickInterval);
        }
    }
}
