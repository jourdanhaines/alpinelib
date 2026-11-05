using System;
using System.Collections.Generic;

namespace AlpineLib.Sky {
    /// <summary>One cell's far copies, grouped by what they draw with (a <see cref="IFarPropSource"/> result).</summary>
    public sealed class FarPropCell {
        public FarPropCell(IReadOnlyList<FarPropBatch> batches) {
            Batches = batches ?? Array.Empty<FarPropBatch>();
            foreach (FarPropBatch batch in Batches) InstanceCount += batch.Matrices.Length;
        }

        public IReadOnlyList<FarPropBatch> Batches { get; }

        /// <summary>Copies over every batch.</summary>
        public int InstanceCount { get; }
    }
}
