using System;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Origin;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The engine-free floating origin pieces on their own: the origin value and its delta, the two-epoch
    /// history both ends resolve stamps through, and the rebase policy.
    /// </summary>
    public sealed class SessionOriginTests {
        private const double CellSize = 128.0;

        [Fact]
        public void DeltaMovesPositionsOppositeToTheOrigin() {
            SessionOrigin next = SessionOrigin.Initial.Next(2, -3, CellSize);

            Vector3 delta = SessionOrigin.Delta(SessionOrigin.Initial, next);

            Assert.Equal(new Vector3(-256f, 0f, 384f), delta);
        }

        [Fact]
        public void DeltaBetweenFarCellsIsExact() {
            var from = new SessionOrigin(7, 1_000_000_000, -1_000_000_000, CellSize);
            SessionOrigin to = from.Next(1_000_000_001, -1_000_000_000, CellSize);

            Assert.Equal(new Vector3(-128f, 0f, 0f), SessionOrigin.Delta(from, to));
        }

        [Fact]
        public void EpochsCompareAcrossTheWrap() {
            Assert.True(SessionOrigin.IsEpochAfter(0, ushort.MaxValue));
            Assert.False(SessionOrigin.IsEpochAfter(ushort.MaxValue, 0));
            Assert.True(SessionOrigin.IsEpochAfter(5, 4));
            Assert.False(SessionOrigin.IsEpochAfter(4, 4));

            var last = new SessionOrigin(ushort.MaxValue, 0, 0, CellSize);
            Assert.Equal(0, last.Next(1, 0, CellSize).Epoch);
        }

        [Fact]
        public void TheCellSizeCannotChangeOnceKnown() {
            SessionOrigin first = SessionOrigin.Initial.Next(1, 0, CellSize);

            Assert.Throws<ArgumentException>(() => first.Next(2, 0, 64.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SessionOrigin.Initial.Next(1, 0, 0.0));
        }

        [Fact]
        public void HistoryResolvesTheCurrentAndPreviousEpochOnly() {
            var history = new OriginHistory();
            Assert.True(history.TryResolveDelta(0, out Vector3 none));
            Assert.Equal(Vector3.Zero, none);
            Assert.False(history.TryResolveDelta(1, out _));

            history.Advance(history.Current.Next(1, 0, CellSize));
            history.Advance(history.Current.Next(3, 0, CellSize));

            Assert.True(history.TryResolveDelta(2, out Vector3 current));
            Assert.Equal(Vector3.Zero, current);
            Assert.True(history.TryResolveDelta(1, out Vector3 previous));
            Assert.Equal(new Vector3(-256f, 0f, 0f), previous);
            Assert.False(history.TryResolveDelta(0, out _));
        }

        [Fact]
        public void HistoryLeavesCarrierRelativeStatesAloneWhateverTheirStamp() {
            var history = new OriginHistory();
            history.Advance(history.Current.Next(1, 0, CellSize));
            history.Advance(history.Current.Next(2, 0, CellSize));
            var riding = new PawnState(new Vector3(0.5f, 1f, 2f), 0f, Vector3.Zero, 0, 3);

            Assert.True(history.TryBringToCurrent(0, in riding, out PawnState current));
            Assert.Equal(riding.Position, current.Position);
        }

        [Fact]
        public void AuthorityRefusesAThresholdInsideTwoCells() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OriginAuthority(200.0, CellSize));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OriginAuthority(1000.0, 0.0));
            _ = new OriginAuthority(256.0, CellSize);
        }

        [Fact]
        public void AuthorityWaitsUntilTheAnchorIsPastTheThreshold() {
            var authority = new OriginAuthority(5000.0, CellSize);

            Assert.False(authority.TryPlan(SessionOrigin.Initial, new Vector3(4999f, 0f, 0f), out _));
            Assert.False(authority.TryPlan(SessionOrigin.Initial, new Vector3(3000f, 0f, 4000f), out _));
            Assert.False(authority.TryPlan(SessionOrigin.Initial, new Vector3(float.NaN, 0f, 0f), out _));
            Assert.True(authority.TryPlan(SessionOrigin.Initial, new Vector3(3000f, 0f, 4001f), out _));
        }

        [Fact]
        public void AuthoritySnapsToTheAnchorsCell() {
            var authority = new OriginAuthority(5000.0, CellSize);
            var current = new SessionOrigin(4, 3, 4, CellSize);

            Assert.True(authority.TryPlan(current, new Vector3(5000.5f, 12f, -10f), out SessionOrigin next));

            Assert.Equal(5, next.Epoch);
            Assert.Equal(3 + 39, next.CellX);
            Assert.Equal(4 - 1, next.CellZ);
            Assert.Equal(CellSize, next.CellSize);
        }

        [Fact]
        public void AFreshOriginNeverAsksForAnotherStraightAway() {
            var authority = new OriginAuthority(256.0, CellSize);
            var anchor = new Vector3(-300f, 0f, 255f);

            Assert.True(authority.TryPlan(SessionOrigin.Initial, anchor, out SessionOrigin next));
            Vector3 rebased = anchor + SessionOrigin.Delta(SessionOrigin.Initial, next);

            Assert.InRange(rebased.X, 0f, (float)CellSize);
            Assert.InRange(rebased.Z, 0f, (float)CellSize);
            Assert.False(authority.TryPlan(next, rebased, out _));
            Assert.False(authority.TryPlan(next, rebased + new Vector3(100f, 0f, 0f), out _));
        }
    }
}
