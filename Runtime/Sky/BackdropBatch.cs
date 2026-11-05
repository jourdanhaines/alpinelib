using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// One camera's backdrop instances: per silhouette mesh, every matrix of the ring and the share of them drawn (those
    /// not wholly hidden in fog), and where they were built from.
    /// </summary>
    public sealed class BackdropBatch {
        private readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[BackdropSettings.MeshCount];
        private readonly List<Matrix4x4>[] _drawn = new List<Matrix4x4>[BackdropSettings.MeshCount];

        public BackdropBatch() {
            for (int index = 0; index < _matrices.Length; index++) {
                _matrices[index] = new List<Matrix4x4>();
                _drawn[index] = new List<Matrix4x4>();
            }
        }

        /// <summary>True once feature matrices were built.</summary>
        public bool IsBuilt { get; set; }

        /// <summary>The layout revision the feature matrices were built from.</summary>
        public int Revision { get; set; }

        /// <summary>Read of the loaded cells the features were picked against.</summary>
        public int KeepOutRevision { get; set; }

        /// <summary>The fog the drawn share was culled with.</summary>
        public BackdropFog Fog { get; set; }

        /// <summary>Keep-out cell of the camera the features were picked for (it follows the camera with hysteresis).</summary>
        public long CellX { get; set; }
        public long CellZ { get; set; }

        /// <summary>Camera absolute minus world xz the features were placed with (changes on an origin shift).</summary>
        public double OffsetX { get; set; }
        public double OffsetZ { get; set; }

        /// <summary>Camera absolute xz the features were picked at.</summary>
        public double FeaturesX { get; set; }
        public double FeaturesZ { get; set; }

        /// <summary>True once the ground was placed.</summary>
        public bool IsGroundPlaced { get; set; }

        /// <summary>The layout revision the ground was placed for.</summary>
        public int GroundRevision { get; set; }

        /// <summary>Camera world position the ground was placed at.</summary>
        public Vector3 CameraWorld { get; set; }

        /// <summary>Camera absolute xz the ground was placed at.</summary>
        public double AbsoluteX { get; set; }
        public double AbsoluteZ { get; set; }

        /// <summary>Frame of the last draw (stale batches are dropped).</summary>
        public int LastUsedFrame { get; set; }

        /// <summary>Instances across every mesh, drawn or fogged away.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Instances drawn across every mesh.</summary>
        public int DrawnCount { get; private set; }

        /// <summary>Meshes with at least one drawn instance: the instanced draws a frame issues.</summary>
        public int DrawnMeshCount { get; private set; }

        /// <summary>Box holding every instance.</summary>
        public Bounds Bounds { get; set; }

        /// <summary>Where the ground is drawn (the annulus about the camera, or the lattice about its anchor; below the ground).</summary>
        public Matrix4x4 GroundMatrix { get; set; }

        /// <summary>Box holding the ground annulus.</summary>
        public Bounds GroundBounds { get; set; }

        /// <summary>Instances of mesh <paramref name="meshIndex"/> in the ring, drawn or not.</summary>
        public List<Matrix4x4> MatricesOf(int meshIndex) {
            return _matrices[meshIndex];
        }

        /// <summary>Instances of mesh <paramref name="meshIndex"/> drawn (not wholly hidden in fog).</summary>
        public List<Matrix4x4> DrawnOf(int meshIndex) {
            return _drawn[meshIndex];
        }

        public void Clear() {
            foreach (List<Matrix4x4> list in _matrices) list.Clear();
            foreach (List<Matrix4x4> list in _drawn) list.Clear();

            InstanceCount = 0;
            DrawnCount = 0;
            DrawnMeshCount = 0;
        }

        /// <summary>Adds an instance of mesh <paramref name="meshIndex"/>; <paramref name="isDrawn"/> false keeps it in the ring undrawn.</summary>
        public void Add(int meshIndex, Matrix4x4 matrix, bool isDrawn = true) {
            _matrices[meshIndex].Add(matrix);
            InstanceCount++;
            if (!isDrawn) return;

            if (_drawn[meshIndex].Count == 0) DrawnMeshCount++;
            _drawn[meshIndex].Add(matrix);
            DrawnCount++;
        }
    }
}
