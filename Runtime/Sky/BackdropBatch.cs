using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>One camera's backdrop instances: a matrix list per silhouette mesh, and where they were built from.</summary>
    public sealed class BackdropBatch {
        private readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[BackdropSettings.MeshCount];

        public BackdropBatch() {
            for (int index = 0; index < _matrices.Length; index++) _matrices[index] = new List<Matrix4x4>();
        }

        /// <summary>True once matrices were built.</summary>
        public bool IsBuilt { get; set; }

        /// <summary>Camera world position the matrices were built at.</summary>
        public Vector3 CameraWorld { get; set; }

        /// <summary>Camera absolute xz the matrices were built at.</summary>
        public double AbsoluteX { get; set; }
        public double AbsoluteZ { get; set; }

        /// <summary>The layout revision the matrices were built from.</summary>
        public int Revision { get; set; }

        /// <summary>Frame of the last draw (stale batches are dropped).</summary>
        public int LastUsedFrame { get; set; }

        /// <summary>Instances across every mesh.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Box holding every instance.</summary>
        public Bounds Bounds { get; set; }

        /// <summary>Instances of mesh <paramref name="meshIndex"/>.</summary>
        public List<Matrix4x4> MatricesOf(int meshIndex) {
            return _matrices[meshIndex];
        }

        public void Clear() {
            foreach (List<Matrix4x4> list in _matrices) list.Clear();

            InstanceCount = 0;
        }

        public void Add(int meshIndex, Matrix4x4 matrix) {
            _matrices[meshIndex].Add(matrix);
            InstanceCount++;
        }
    }
}
