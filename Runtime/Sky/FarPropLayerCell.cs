using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;

namespace AlpineLib.Sky {
    /// <summary>A <see cref="FarPropLayer"/> cell: its prepare in flight, then its instanced batches.</summary>
    internal sealed class FarPropLayerCell {
        public FarPropLayerCell(CellCoord coord) {
            Coord = coord;
        }

        public CellCoord Coord { get; }
        public Task<FarPropCell> PrepareTask { get; set; }
        public CancellationTokenSource Cancellation { get; set; }

        /// <summary>The drawn batches once prepared (empty when the prepare failed).</summary>
        public List<InstancedBatch> Batches { get; } = new List<InstancedBatch>();

        public bool IsReady { get; set; }
        public int InstanceCount { get; set; }

        /// <summary>The shown-since time the batches' fades were set up for (NaN: none).</summary>
        public double FadeSetFor { get; set; } = double.NaN;
    }
}
