using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The validator's carrier rule and the hole it deliberately leaves: a tick that changes frame is
    /// accepted unmeasured, and every tick that does not is measured exactly as before.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth guarding. Accepting a frame change is easy to write and easy to
    /// over-apply, and a validator that stopped clamping inside a carrier's frame would hand any client
    /// a train deck to speed-hack on with no ceiling at all.
    /// </remarks>
    public sealed class MovementValidatorCarrierTests {
        private const ushort PawnPrefab = 0;
        private const float TickInterval = 1f / 30f;
        private const ushort TrainCarrierId = 1;

        [Fact]
        public void BoardingACarrierIsAcceptedRatherThanReadAsATeleport() {
            MovementValidator validator = BuildValidator();
            PawnState beside = At(new Vector3(0f, 0f, 0f), WireLocomotion.Walk, PawnState.WorldCarrierId);
            PawnState aboard = At(new Vector3(0.4f, 0f, 1.2f), WireLocomotion.Walk, TrainCarrierId);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in beside, in aboard, TickInterval);

            Assert.Equal(MovementVerdictKind.Accepted, verdict.Kind);
            Assert.False(verdict.RequiresCorrection);
            Assert.Equal(TrainCarrierId, verdict.ResolvedState.CarrierId);
            Assert.Equal(aboard.Position.Z, verdict.ResolvedState.Position.Z, 5);
        }

        [Fact]
        public void SteppingOffACarrierIsAcceptedTheSameWay() {
            MovementValidator validator = BuildValidator();
            PawnState aboard = At(new Vector3(0.4f, 0f, 1.2f), WireLocomotion.Walk, TrainCarrierId);
            PawnState ashore = At(new Vector3(480f, 0f, -12f), WireLocomotion.Walk, PawnState.WorldCarrierId);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in aboard, in ashore, TickInterval);

            Assert.Equal(MovementVerdictKind.Accepted, verdict.Kind);
            Assert.Equal(PawnState.WorldCarrierId, verdict.ResolvedState.CarrierId);
        }

        [Fact]
        public void AModestOverrunOnADeckIsStillClampedAndStaysOnItsCarrier() {
            MovementValidator validator = BuildValidator();
            float allowed = 2f * 1.5f * TickInterval + MovementValidator.PositionSlackMetres;
            PawnState previous = At(Vector3.Zero, WireLocomotion.Walk, TrainCarrierId);
            PawnState next = At(new Vector3(0f, 0f, allowed * 2f), WireLocomotion.Walk, TrainCarrierId);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval);

            Assert.Equal(MovementVerdictKind.Clamped, verdict.Kind);
            Assert.Equal(allowed, verdict.ResolvedState.Position.Z, 4);
            Assert.Equal(TrainCarrierId, verdict.ResolvedState.CarrierId);
        }

        [Fact]
        public void ATeleportAcrossOneDeckIsStillThrownAway() {
            MovementValidator validator = BuildValidator();
            PawnState previous = At(new Vector3(0f, 0f, 1f), WireLocomotion.Walk, TrainCarrierId);
            PawnState next = At(new Vector3(0f, 0f, 400f), WireLocomotion.Walk, TrainCarrierId);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval);

            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
            Assert.Equal(previous.Position.Z, verdict.ResolvedState.Position.Z, 5);
            Assert.Equal(TrainCarrierId, verdict.ResolvedState.CarrierId);
        }

        [Fact]
        public void MovingBetweenTwoCarriersIsAFrameChangeLikeAnyOther() {
            MovementValidator validator = BuildValidator();
            PawnState onTheFirst = At(new Vector3(0f, 0f, 1f), WireLocomotion.Walk, TrainCarrierId);
            PawnState onTheSecond = At(new Vector3(0f, 0f, 1.1f), WireLocomotion.Walk, 2);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in onTheFirst, in onTheSecond, TickInterval);

            Assert.Equal(MovementVerdictKind.Accepted, verdict.Kind);
            Assert.Equal(2, verdict.ResolvedState.CarrierId);
        }

        private static MovementValidator BuildValidator() {
            var config = new NetConfig {
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] {
                    new MovementProfile {
                        DisplayName = "pawn",
                        WalkSlowSpeed = 0.8f,
                        WalkSpeed = 2f,
                        JogSpeed = 3.5f,
                        SprintSpeed = 5.5f,
                        CrouchSpeed = 1.2f,
                        CrouchFastSpeed = 2.2f
                    }
                }
            };

            return new MovementValidator(config);
        }

        private static PawnState At(Vector3 position, WireLocomotion gait, ushort carrierId) {
            return new PawnState(position, 0f, Vector3.Zero, PawnState.PackFlags(gait, false, true), carrierId);
        }
    }
}
