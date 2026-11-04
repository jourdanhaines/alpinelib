using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>One camera's rolling prop instances of a frame: a world matrix list per kind and the box holding them.</summary>
    public sealed class RollingPropBatch {
        private readonly List<List<Matrix4x4>> _matrices = new List<List<Matrix4x4>>();
        private Vector3 _min;
        private Vector3 _max;

        /// <summary>Instances across every kind.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Kinds with a matrix list (some may be empty).</summary>
        public int KindCount => _matrices.Count;

        /// <summary>Box holding every instance centre, grown by the largest prop.</summary>
        public Bounds Bounds { get; private set; }

        /// <summary>Instances of kind <paramref name="kind"/>, or an empty list past the known kinds.</summary>
        public List<Matrix4x4> MatricesOf(int kind) {
            EnsureKinds(kind + 1);
            return _matrices[kind];
        }

        public void Clear(int kinds) {
            EnsureKinds(kinds);
            foreach (List<Matrix4x4> list in _matrices) list.Clear();

            InstanceCount = 0;
            _min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Bounds = default;
        }

        public void Add(int kind, Matrix4x4 matrix) {
            MatricesOf(kind).Add(matrix);
            Vector3 position = matrix.GetColumn(3);
            _min = Vector3.Min(_min, position);
            _max = Vector3.Max(_max, position);
            InstanceCount++;
        }

        /// <summary>Closes the bounds, grown by <paramref name="reach"/> metres for the props' own size.</summary>
        public void CloseBounds(float reach) {
            if (InstanceCount == 0) return;

            var grow = new Vector3(reach, reach, reach);
            Bounds = new Bounds((_min + _max) * 0.5f, _max - _min + grow * 2f);
        }

        private void EnsureKinds(int kinds) {
            while (_matrices.Count < kinds) _matrices.Add(new List<Matrix4x4>());
        }
    }
}
