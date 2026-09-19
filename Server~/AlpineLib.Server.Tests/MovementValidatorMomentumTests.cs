using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The carried-momentum allowance: an airborne pawn that left a carrier is measured against the
    /// speed it left at, and nothing else widens.
    /// </summary>
    public sealed class MovementValidatorMomentumTests {
        private const ushort PawnPrefab = 0;
        private const ushort TrainCarrierId = 1;
        private const float TickInterval = 1f / 30f;
        private const float ConsistSpeed = 30f;

        /// <summary>One thirty-metre-a-second tick in world frame, at the All Aboard walk gait.</summary>
        private static readonly Vector3 Before = new Vector3(500f, 1.2f, 0f);
        private static readonly Vector3 After = new Vector3(501f, 1.0f, 0f);

        [Fact]
        public void AnAirborneWorldTickAtConsistSpeedIsAcceptedWithCarriedMomentum() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState previous = Airborne(Before);
            PawnState next = Airborne(After);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval, true, ConsistSpeed);

            Assert.Equal(MovementVerdictKind.Accepted, verdict.Kind);
        }

        [Fact]
        public void TheSameTickWithoutMomentumIsRefused() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState previous = Airborne(Before);
            PawnState next = Airborne(After);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval, true, 0f);

            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
        }

        [Fact]
        public void MomentumNeverWidensAGroundedTick() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState previous = Grounded(Before);
            PawnState next = Grounded(After);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval, true, ConsistSpeed);

            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
        }

        [Fact]
        public void TheGaitStillWinsWhenItIsFasterThanTheCarriedSpeed() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState previous = Airborne(Before).WithFlags(WireLocomotion.Sprint, false, false);
            PawnState next = Airborne(new Vector3(500.3f, 1.0f, 0f)).WithFlags(WireLocomotion.Sprint, false, false);

            MovementVerdict slow = validator.Validate(PawnPrefab, in previous, in next, TickInterval, true, 1f);
            MovementVerdict none = validator.Validate(PawnPrefab, in previous, in next, TickInterval, true, 0f);

            Assert.Equal(MovementVerdictKind.Accepted, slow.Kind);
            Assert.Equal(none.AllowedSpeed, slow.AllowedSpeed, 5);
        }

        [Fact]
        public void LeavingACarrierIntoTheAirLatchesThePlanarSpeedCapped() {
            PawnState onDeck = Grounded(new Vector3(0.5f, 0f, 2f)).WithCarrier(TrainCarrierId);
            PawnState flying = Airborne(Before, new Vector3(ConsistSpeed, -1f, 0f));
            PawnState tooFast = Airborne(Before, new Vector3(200f, 0f, 0f));

            Assert.Equal(ConsistSpeed, MovementValidator.ResolveCarriedSpeed(in onDeck, in flying), 5);
            Assert.Equal(MovementValidator.MaxCarriedSpeed, MovementValidator.ResolveCarriedSpeed(in onDeck, in tooFast), 5);
        }

        [Fact]
        public void OnlyACarrierToWorldChangeIntoTheAirOpensMomentum() {
            PawnState onDeck = Grounded(new Vector3(0.5f, 0f, 2f)).WithCarrier(TrainCarrierId);
            PawnState onGround = Grounded(Before, new Vector3(ConsistSpeed, 0f, 0f));
            PawnState beside = Grounded(Before, new Vector3(ConsistSpeed, 0f, 0f));
            PawnState boarding = Airborne(new Vector3(0.5f, 0.5f, 2f), new Vector3(ConsistSpeed, 0f, 0f)).WithCarrier(TrainCarrierId);
            PawnState otherCar = Airborne(new Vector3(0.5f, 0.5f, 2f), new Vector3(ConsistSpeed, 0f, 0f)).WithCarrier(2);

            Assert.Equal(0f, MovementValidator.ResolveCarriedSpeed(in onDeck, in onGround));
            Assert.Equal(0f, MovementValidator.ResolveCarriedSpeed(in beside, in boarding));
            Assert.Equal(0f, MovementValidator.ResolveCarriedSpeed(in onDeck, in otherCar));
        }

        private static PawnState Airborne(Vector3 position) {
            return Airborne(position, Vector3.Zero);
        }

        private static PawnState Airborne(Vector3 position, Vector3 velocity) {
            return new PawnState(position, 0f, velocity, PawnState.PackFlags(WireLocomotion.Walk, false, false));
        }

        private static PawnState Grounded(Vector3 position) {
            return Grounded(position, Vector3.Zero);
        }

        private static PawnState Grounded(Vector3 position, Vector3 velocity) {
            return new PawnState(position, 0f, velocity, PawnState.PackFlags(WireLocomotion.Walk, false, true));
        }

        private static MovementValidator BuildAllAboardValidator() {
            var config = new NetConfig {
                ServerTickRate = 30,
                ClientSendRate = 30,
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] {
                    new MovementProfile {
                        DisplayName = "Player",
                        WalkSlowSpeed = 4f,
                        WalkSpeed = 4f,
                        JogSpeed = 5.4f,
                        SprintSpeed = 7f,
                        CrouchSpeed = 1.6f,
                        CrouchFastSpeed = 2.8f
                    }
                }
            };

            return new MovementValidator(config);
        }
    }
}
