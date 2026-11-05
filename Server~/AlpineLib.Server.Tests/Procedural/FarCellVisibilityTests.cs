using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Streaming;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class FarCellVisibilityTests {
        private static readonly CellCoord Near = new CellCoord(4, 0);
        private static readonly CellCoord Far = new CellCoord(6, 0);

        [Fact]
        public void ReadyCellsInRangeStartShowingAtTheRefresh() {
            FarCellVisibility visibility = Create();
            visibility.MarkReady(Near);
            visibility.MarkReady(Far);

            visibility.Refresh(new[] { Near, Far }, 3.0);

            Assert.True(visibility.TryGetShownSince(Near, out double since));
            Assert.Equal(3.0, since);
            Assert.False(visibility.IsDrawn(Far));
        }

        [Fact]
        public void SuppressingStopsAtOnceAndUnsuppressingShowsFully() {
            FarCellVisibility visibility = Create();
            visibility.MarkReady(Near);
            visibility.Refresh(new[] { Near }, 1.0);

            visibility.Suppress(Near);
            Assert.False(visibility.IsDrawn(Near));
            visibility.Refresh(new[] { Near }, 2.0);
            Assert.False(visibility.IsDrawn(Near));

            visibility.Unsuppress(Near, 5.0);
            Assert.True(visibility.TryGetShownSince(Near, out double since));
            Assert.Equal(5.0 - FarCellVisibility.ShownLongAgoSeconds, since);
        }

        [Fact]
        public void UnsuppressingMidFadeCarriesOnTheTwinsFade() {
            FarCellVisibility visibility = Create();
            visibility.MarkReady(Near);
            visibility.Suppress(Near);

            visibility.Unsuppress(Near, 5.0, 4.6);
            Assert.True(visibility.TryGetShownSince(Near, out double since));
            Assert.Equal(4.6, since);

            visibility.Suppress(Near);
            visibility.Unsuppress(Near, 5.0, 7.0);
            Assert.True(visibility.TryGetShownSince(Near, out since));
            Assert.Equal(5.0 - FarCellVisibility.ShownLongAgoSeconds, since);
        }

        [Fact]
        public void ASuppressedCellStaysHiddenWhenItBecomesReady() {
            FarCellVisibility visibility = Create();
            visibility.Suppress(Near);
            visibility.MarkReady(Near);

            visibility.Refresh(new[] { Near }, 1.0);

            Assert.False(visibility.IsDrawn(Near));
        }

        [Fact]
        public void MovingTheFocusStartsAndStopsCells() {
            FarCellVisibility visibility = Create();
            visibility.MarkReady(Far);
            visibility.Refresh(new[] { Far }, 1.0);
            Assert.False(visibility.IsDrawn(Far));

            visibility.SetFocus(new CellCoord(2, 0));
            visibility.Refresh(new[] { Far }, 2.0);
            Assert.True(visibility.TryGetShownSince(Far, out double since));
            Assert.Equal(2.0, since);

            visibility.SetFocus(new CellCoord(-2, 0));
            visibility.Refresh(new[] { Far }, 3.0);
            Assert.False(visibility.IsDrawn(Far));
        }

        [Fact]
        public void ForgottenCellsNoLongerDraw() {
            FarCellVisibility visibility = Create();
            visibility.MarkReady(Near);
            visibility.Refresh(new[] { Near }, 1.0);

            visibility.Forget(Near);

            Assert.False(visibility.IsDrawn(Near));
            Assert.False(visibility.IsReady(Near));
        }

        private static FarCellVisibility Create() {
            var visibility = new FarCellVisibility(5);
            visibility.SetFocus(new CellCoord(0, 0));
            return visibility;
        }
    }
}
