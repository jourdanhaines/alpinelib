using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// One cell's far copies, grouped by what they draw with (a <see cref="IFarPropSource"/> result), plus optional
    /// cell-unique meshes (<see cref="MeshPayloads"/>) the layer uploads and draws once each.
    /// </summary>
    public sealed class FarPropCell {
        /// <summary>
        /// <paramref name="batches"/> of instanced copies, and <paramref name="meshPayloads"/> drawn through
        /// <paramref name="payloadMaterials"/> by slot (null: none). Throws when a payload's slot has no material.
        /// </summary>
        public FarPropCell(IReadOnlyList<FarPropBatch> batches, IReadOnlyList<FarPropMeshPayload> meshPayloads = null, IReadOnlyList<Material> payloadMaterials = null) {
            Batches = batches ?? Array.Empty<FarPropBatch>();
            MeshPayloads = meshPayloads ?? Array.Empty<FarPropMeshPayload>();
            PayloadMaterials = payloadMaterials ?? Array.Empty<Material>();
            foreach (FarPropBatch batch in Batches) InstanceCount += batch.Matrices.Length;
            foreach (FarPropMeshPayload payload in MeshPayloads) RequireSlot(payload);
        }

        public IReadOnlyList<FarPropBatch> Batches { get; }

        /// <summary>Cell-unique meshes built on the worker (empty for plain instanced cells).</summary>
        public IReadOnlyList<FarPropMeshPayload> MeshPayloads { get; }

        /// <summary>The materials <see cref="FarPropMeshPayload.MaterialSlot"/> indexes (referenced, never touched on the worker).</summary>
        public IReadOnlyList<Material> PayloadMaterials { get; }

        /// <summary>Copies over every batch.</summary>
        public int InstanceCount { get; }

        private void RequireSlot(FarPropMeshPayload payload) {
            if (payload == null) throw new ArgumentException("Mesh payloads must not be null.", nameof(MeshPayloads));
            if (payload.MaterialSlot >= PayloadMaterials.Count) throw new ArgumentOutOfRangeException(nameof(MeshPayloads), payload.MaterialSlot, $"Slot outside the {PayloadMaterials.Count} payload materials.");
        }
    }
}
