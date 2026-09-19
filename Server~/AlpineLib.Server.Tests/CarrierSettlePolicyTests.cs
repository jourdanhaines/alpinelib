using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The reporting side's dwell: free between frames that move together, skipped between frames that
    /// move apart, because a dwell there is paid for in rejections at the consist's speed.
    /// </summary>
    public sealed class CarrierSettlePolicyTests {
        private const ushort DeckCarrierId = 1;
        private const ushort SecondCarrierId = 2;
        private const float Frame = 1f / 60f;
        private const float ConsistSpeed = 30f;

        [Fact]
        public void BoardingAMovingConsistIsReportedOnTheFirstFrame() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(PawnState.WorldCarrierId);

            policy.Advance(DeckCarrierId, ConsistSpeed, Frame);

            Assert.Equal(DeckCarrierId, policy.ReportedCarrierId);
        }

        [Fact]
        public void LeavingAMovingConsistIsReportedOnTheFirstFrame() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(DeckCarrierId);

            policy.Advance(PawnState.WorldCarrierId, ConsistSpeed, Frame);

            Assert.Equal(PawnState.WorldCarrierId, policy.ReportedCarrierId);
        }

        [Fact]
        public void AContactAlternatingBetweenTwoCoupledCarsNeverReports() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(DeckCarrierId);

            int frames = (int)(CarrierSettlePolicy.DwellSeconds * 4f / Frame);
            for (int frame = 0; frame < frames; frame++) {
                ushort live = frame % 2 == 0 ? SecondCarrierId : DeckCarrierId;
                policy.Advance(live, 0f, Frame);
            }

            Assert.Equal(DeckCarrierId, policy.ReportedCarrierId);
        }

        [Fact]
        public void ACoupledCarHeldForTheDwellIsAdopted() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(DeckCarrierId);

            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.5f);
            Assert.Equal(DeckCarrierId, policy.ReportedCarrierId);

            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.5f);
            Assert.Equal(SecondCarrierId, policy.ReportedCarrierId);
        }

        [Fact]
        public void AChangeInTheLiveAnswerRestartsTheDwell() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(DeckCarrierId);

            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.9f);
            policy.Advance(DeckCarrierId, 0f, Frame);
            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.9f);

            Assert.Equal(DeckCarrierId, policy.ReportedCarrierId);
        }

        [Fact]
        public void ResetAdoptsOutrightAndClearsTheDwell() {
            var policy = new CarrierSettlePolicy();
            policy.Reset(DeckCarrierId);
            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.9f);

            policy.Reset(PawnState.WorldCarrierId);
            Assert.Equal(PawnState.WorldCarrierId, policy.ReportedCarrierId);

            policy.Advance(SecondCarrierId, 0f, CarrierSettlePolicy.DwellSeconds * 0.5f);
            Assert.Equal(PawnState.WorldCarrierId, policy.ReportedCarrierId);
        }

        /// <summary>
        /// A whole dwell under the threshold drifts exactly the slack the validator forgives, so a
        /// dwelling source can never produce a correction by dwelling.
        /// </summary>
        [Fact]
        public void TheImmediateThresholdIsTheSlackOverTheDwell() {
            float driftOverADwell = CarrierSettlePolicy.ImmediateRelativeSpeed * CarrierSettlePolicy.DwellSeconds;

            Assert.Equal(MovementValidator.PositionSlackMetres, driftOverADwell, 5);
        }
    }
}
