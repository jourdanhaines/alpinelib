using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using AlpineLib.Rendering;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>A <see cref="FarPropLayer"/> cell: its prepare in flight, its payload uploads, then its instanced batches.</summary>
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

        /// <summary>A prepared result whose mesh payloads are still uploading (null: none waiting).</summary>
        public FarPropCell Pending { get; set; }

        /// <summary>Meshes uploaded from the cell's payloads (owned: destroyed when the cell goes).</summary>
        public List<Mesh> PayloadMeshes { get; } = new List<Mesh>();

        /// <summary>Prepares in a row that failed.</summary>
        public int Failures { get; set; }

        /// <summary>Clock time before which a failed prepare is not retried.</summary>
        public double RetryAt { get; set; } = double.NegativeInfinity;
        public int InstanceCount { get; set; }

        /// <summary>The shown-since time the batches' fades were set up for (NaN: none).</summary>
        public double FadeSetFor { get; set; } = double.NaN;
    }
}
