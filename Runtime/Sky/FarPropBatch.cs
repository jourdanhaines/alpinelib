using System;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>Far copies sharing a submesh and material, as transforms relative to their cell's centre.</summary>
    public sealed class FarPropBatch {
        public FarPropBatch(Mesh mesh, int submesh, Material material, Matrix4x4[] matrices, Bounds bounds) {
            Mesh = mesh;
            Submesh = submesh;
            Material = material;
            Matrices = matrices ?? Array.Empty<Matrix4x4>();
            Bounds = bounds;
        }

        public Mesh Mesh { get; }
        public int Submesh { get; }
        public Material Material { get; }
        public Matrix4x4[] Matrices { get; }

        /// <summary>Cell-local bounds of every copy.</summary>
        public Bounds Bounds { get; }
    }
}
