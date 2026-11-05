using System.Threading;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Sky {
    /// <summary>What a <see cref="FarPropLayer"/> draws for a cell beyond the streamed window.</summary>
    public interface IFarPropSource {
        /// <summary>
        /// Worker thread: the cell's far copies, placed relative to the cell centre exactly as the built cell
        /// places them (so swapping the two shows no step). Engine objects may be referenced, never touched.
        /// </summary>
        FarPropCell Prepare(CellCoord cell, CancellationToken cancellation);
    }
}
