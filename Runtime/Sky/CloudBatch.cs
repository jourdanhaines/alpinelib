using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>One camera's cloud instances: a matrix list per cloud mesh, the field window of each class's lattice they came from and what they were built at.</summary>
    public sealed class CloudBatch {
        private readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[CloudFieldSettings.MeshCount];
        private Vector3 _min;
        private Vector3 _max;

        public CloudBatch() {
            for (int index = 0; index < _matrices.Length; index++) _matrices[index] = new List<Matrix4x4>();
        }

        /// <summary>True once matrices were built.</summary>
        public bool IsBuilt { get; set; }

        /// <summary>Camera world position, absolute xz, drift, session time and fade the matrices were built at.</summary>
        public Vector3 CameraWorld { get; set; }
        public double AbsoluteX { get; set; }
        public double AbsoluteZ { get; set; }
        public WindVector Displacement { get; set; }
        public double Time { get; set; }
        public float Fade { get; set; }

        /// <summary>The field revision the matrices were built from.</summary>
        public int Revision { get; set; }

        /// <summary>Frame of the last use (stale batches are dropped and stop holding cells).</summary>
        public int LastUsedFrame { get; set; }

        private readonly long[] _cellX = new long[CloudFieldSettings.ClassCount];
        private readonly long[] _cellZ = new long[CloudFieldSettings.ClassCount];

        /// <summary>The window's reach in cells either side of the camera's cell.</summary>
        public int WindowCells { get; set; }

        /// <summary>The field cell of <paramref name="cloudClass"/>'s lattice the camera stood in.</summary>
        public long CellXOf(CloudClass cloudClass) {
            return _cellX[(int)cloudClass];
        }

        public long CellZOf(CloudClass cloudClass) {
            return _cellZ[(int)cloudClass];
        }

        public void SetCell(CloudClass cloudClass, long cellX, long cellZ) {
            _cellX[(int)cloudClass] = cellX;
            _cellZ[(int)cloudClass] = cellZ;
        }

        /// <summary>Instances across every mesh.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Box holding every instance (grown by the largest cloud).</summary>
        public Bounds Bounds { get; private set; }

        /// <summary>Instances of mesh <paramref name="meshIndex"/>.</summary>
        public List<Matrix4x4> MatricesOf(int meshIndex) {
            return _matrices[meshIndex];
        }

        /// <summary>True when <paramref name="cloudClass"/>'s field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) is inside this batch's window.</summary>
        public bool HoldsCell(CloudClass cloudClass, long cellX, long cellZ) {
            if (!IsBuilt) return false;

            return System.Math.Abs(cellX - _cellX[(int)cloudClass]) <= WindowCells && System.Math.Abs(cellZ - _cellZ[(int)cloudClass]) <= WindowCells;
        }

        public void Clear() {
            foreach (List<Matrix4x4> list in _matrices) list.Clear();

            InstanceCount = 0;
            _min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        public void Add(int meshIndex, Matrix4x4 matrix) {
            _matrices[meshIndex].Add(matrix);
            InstanceCount++;
            Vector3 position = matrix.GetColumn(3);
            _min = Vector3.Min(_min, position);
            _max = Vector3.Max(_max, position);
        }

        /// <summary>Closes the bounds around every instance, grown by <paramref name="reach"/> metres (the largest cloud's extent).</summary>
        public void CloseBounds(Vector3 fallbackCentre, float reach) {
            if (InstanceCount == 0) {
                Bounds = new Bounds(fallbackCentre, Vector3.one);
                return;
            }

            var bounds = new Bounds();
            bounds.SetMinMax(_min - Vector3.one * reach, _max + Vector3.one * reach);
            Bounds = bounds;
        }
    }
}
