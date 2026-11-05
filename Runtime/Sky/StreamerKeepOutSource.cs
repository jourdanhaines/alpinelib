using System.Collections.Generic;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Streaming;

namespace AlpineLib.Sky {
    /// <summary>
    /// Every cell a <see cref="CellStreamer"/> tracks (around all of its observers, unload margin included) as the
    /// backdrop's keep-out. Its cell coordinates are absolute, so they hold across floating-origin shifts.
    /// </summary>
    public sealed class StreamerKeepOutSource : IBackdropKeepOutSource {
        private readonly CellStreamer _streamer;
        private readonly List<StreamedCell> _cells = new List<StreamedCell>();

        public StreamerKeepOutSource(CellStreamer streamer) {
            _streamer = streamer;
        }

        public int KeepOutRevision => _streamer != null ? _streamer.CellsRevision : 0;

        public void CollectKeepOutCells(List<CellCoord> cells) {
            if (_streamer == null) return;

            _streamer.CopyCells(_cells);
            foreach (StreamedCell cell in _cells) cells.Add(cell.Coord);
            _cells.Clear();
        }
    }
}
