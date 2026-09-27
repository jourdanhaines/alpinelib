using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Streaming;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class CellInterestSetTests {
        private readonly List<CellCoord> _added = new List<CellCoord>();
        private readonly List<CellCoord> _removed = new List<CellCoord>();

        [Fact]
        public void OneObserverLoadsItsSquareInRowMajorOrder() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));

            interest.Update(_added, _removed);

            Assert.Equal(CellSpace.Square(new CellCoord(0, 0), 2).Cells().ToArray(), _added);
            Assert.Empty(_removed);
            Assert.Equal(25, interest.Loaded.Count);
        }

        [Fact]
        public void AnUnchangedObserverReportsNothing() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(4, 4));
            interest.Update(_added, _removed);

            interest.Update(_added, _removed);

            Assert.Empty(_added);
            Assert.Empty(_removed);
        }

        [Fact]
        public void OverlappingObserversLoadTheUnionOnce() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.SetObserver(2, new CellCoord(3, 0));

            interest.Update(_added, _removed);

            Assert.Equal(5 * 8, _added.Count);
            Assert.Equal(_added.Count, _added.Distinct().Count());
        }

        [Fact]
        public void SteppingOneCellLoadsTheLeadingRowButKeepsTheTrailingOne() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.Update(_added, _removed);

            interest.SetObserver(1, new CellCoord(1, 0));
            interest.Update(_added, _removed);

            Assert.Equal(Enumerable.Range(-2, 5).Select(y => new CellCoord(3, y)).ToArray(), _added);
            Assert.Empty(_removed);
            Assert.True(interest.IsLoaded(new CellCoord(-2, 0)));
        }

        [Fact]
        public void PacingAcrossAnEdgeNeverUnloads() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.Update(_added, _removed);

            for (int step = 0; step < 6; step++) {
                interest.SetObserver(1, new CellCoord(step % 2, 0));
                interest.Update(_added, _removed);

                Assert.Empty(_removed);
            }
        }

        [Fact]
        public void ACellUnloadsOnlyPastTheUnloadRadius() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.Update(_added, _removed);

            interest.SetObserver(1, new CellCoord(2, 0));
            interest.Update(_added, _removed);

            Assert.Equal(Enumerable.Range(-2, 5).Select(y => new CellCoord(-2, y)).ToArray(), _removed);
            Assert.True(interest.IsLoaded(new CellCoord(-1, 0)));
            Assert.Equal(5 * 6, interest.Loaded.Count);
        }

        [Fact]
        public void ATeleportDropsEverythingLeftBehind() {
            CellInterestSet interest = new CellInterestSet(2);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.Update(_added, _removed);
            CellCoord[] before = CopyLoaded(interest);

            interest.SetObserver(1, new CellCoord(100, -100));
            interest.Update(_added, _removed);

            Assert.Equal(before, _removed);
            Assert.Equal(CellSpace.Square(new CellCoord(100, -100), 2).Cells().ToArray(), _added);
        }

        [Fact]
        public void RemovingAnObserverReleasesOnlyCellsNobodyElseHolds() {
            CellInterestSet interest = new CellInterestSet(1);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.SetObserver(2, new CellCoord(10, 0));
            interest.Update(_added, _removed);

            Assert.True(interest.RemoveObserver(2));
            interest.Update(_added, _removed);

            Assert.Equal(CellSpace.Square(new CellCoord(10, 0), 1).Cells().ToArray(), _removed);
            Assert.Equal(9, interest.Loaded.Count);
            Assert.False(interest.RemoveObserver(2));
        }

        [Fact]
        public void DiffsDoNotDependOnObserverRegistrationOrder() {
            CellCoord[] positions = { new CellCoord(5, 5), new CellCoord(-3, 2), new CellCoord(6, 7), new CellCoord(0, -9) };
            CellInterestSet forward = new CellInterestSet(2);
            CellInterestSet backward = new CellInterestSet(2);

            for (int index = 0; index < positions.Length; index++) {
                forward.SetObserver((ulong)index, positions[index]);
                backward.SetObserver((ulong)(positions.Length - 1 - index), positions[positions.Length - 1 - index]);
            }

            List<CellCoord> forwardAdded = new List<CellCoord>();
            List<CellCoord> backwardAdded = new List<CellCoord>();
            forward.Update(forwardAdded, _removed);
            backward.Update(backwardAdded, _removed);

            Assert.Equal(forwardAdded, backwardAdded);
            Assert.Equal(forwardAdded.OrderBy(cell => cell.Y).ThenBy(cell => cell.X), forwardAdded);
        }

        [Fact]
        public void ClearReportsEveryLiveCellAndForgetsObservers() {
            CellInterestSet interest = new CellInterestSet(1);
            interest.SetObserver(7, new CellCoord(2, 2));
            interest.Update(_added, _removed);

            interest.Clear(_removed);

            Assert.Equal(CellSpace.Square(new CellCoord(2, 2), 1).Cells().ToArray(), _removed);
            Assert.Empty(interest.Loaded);
            Assert.Equal(0, interest.ObserverCount);
        }

        [Fact]
        public void AZeroMarginUnloadsAsSoonAsACellLeavesTheSquare() {
            CellInterestSet interest = new CellInterestSet(1, 0);
            interest.SetObserver(1, new CellCoord(0, 0));
            interest.Update(_added, _removed);

            interest.SetObserver(1, new CellCoord(1, 0));
            interest.Update(_added, _removed);

            Assert.Equal(3, _removed.Count);
            Assert.All(_removed, cell => Assert.Equal(-1, cell.X));
        }

        [Fact]
        public void NegativeRadiiAreRefused() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellInterestSet(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellInterestSet(1, -1));
        }

        private static CellCoord[] CopyLoaded(CellInterestSet interest) {
            List<CellCoord> cells = new List<CellCoord>();
            interest.CopyLoaded(cells);
            return cells.ToArray();
        }
    }
}
