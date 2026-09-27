using System.Collections;
using System.Threading;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// Builds the content of one streamed cell for <see cref="CellStreamer"/> in two halves: pure data on a
    /// worker thread, then scene objects on the main thread in budgeted slices.
    /// </summary>
    /// <remarks>
    /// <see cref="Release"/> is called exactly once for every cell whose <see cref="Instantiate"/> began,
    /// including one abandoned mid-build because it left interest; a cell dropped before that point never
    /// reaches the builder again (a prepared result that is <see cref="System.IDisposable"/> is disposed).
    /// </remarks>
    public interface ICellBuilder {
        /// <summary>
        /// Computes everything the cell needs without touching Unity objects. Runs on a worker thread
        /// (the calling thread under <see cref="CellStreamer.BuildAllNow"/>); should poll
        /// <paramref name="cancellation"/> in long loops.
        /// </summary>
        object Prepare(CellCoord cell, CancellationToken cancellation);

        /// <summary>
        /// Creates the cell's objects under <see cref="StreamedCell.Root"/> (placed at the cell centre) on the
        /// main thread. Each <c>yield</c> is a checkpoint: the streamer resumes in the same frame while
        /// <paramref name="budget"/> has time left, otherwise next frame. A yielded
        /// <see cref="IEnumerator"/> runs as a nested step. May return null when there is nothing to build.
        /// </summary>
        IEnumerator Instantiate(StreamedCell cell, object prepared, CellBuildBudget budget);

        /// <summary>Tears down whatever <see cref="Instantiate"/> created or registered; the root is destroyed afterwards.</summary>
        void Release(StreamedCell cell);
    }
}
