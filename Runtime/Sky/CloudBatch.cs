using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// One camera's cloud instances: a matrix list per draw slot (each cloud mesh fine, then each coarse), the field
    /// window of each class's lattice they came from, what they were built at and which clouds draw coarse (kept per
    /// cell so a cloud inside the far mesh band keeps the mesh it had).
    /// </summary>
    public sealed class CloudBatch {
        /// <summary>Draw slots: <see cref="CloudFieldSettings.MeshCount"/> fine meshes, then as many coarse ones.</summary>
        public const int SlotCount = CloudFieldSettings.MeshCount * 2;

        private const byte BandUnknown = 0;
        private const byte BandNear = 1;
        private const byte BandFar = 2;

        private readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[SlotCount];
        private readonly List<Vector4>[] _centres = new List<Vector4>[SlotCount];
        private readonly Dictionary<(CloudClass Class, long X, long Z), byte[]> _bands = new Dictionary<(CloudClass Class, long X, long Z), byte[]>();
        private readonly List<(CloudClass Class, long X, long Z)> _dropped = new List<(CloudClass Class, long X, long Z)>();
        private Vector3 _min;
        private Vector3 _max;

        public CloudBatch() {
            for (int index = 0; index < _matrices.Length; index++) {
                _matrices[index] = new List<Matrix4x4>();
                _centres[index] = new List<Vector4>();
            }
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

        /// <summary>Landmarks near the camera the clouds lift over and gather about, queried about (<see cref="ShapesX"/>, <see cref="ShapesZ"/>).</summary>
        public List<LandmarkSkyShape> LandmarkShapes { get; } = new List<LandmarkSkyShape>();
        public double ShapesX { get; set; }
        public double ShapesZ { get; set; }

        /// <summary>True once <see cref="LandmarkShapes"/> was queried; complete once the provider knew every landmark in reach.</summary>
        public bool HasShapes { get; set; }
        public bool AreShapesComplete { get; set; }

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

        /// <summary>Instances of draw slot <paramref name="slot"/> (below <see cref="CloudFieldSettings.MeshCount"/> a fine mesh, else that mesh's coarse one).</summary>
        public List<Matrix4x4> MatricesOf(int slot) {
            return _matrices[slot];
        }

        /// <summary>Each instance of draw slot <paramref name="slot"/> as its world centre (x, y, z) and uniform scale (w), in the order of <see cref="MatricesOf"/>.</summary>
        public List<Vector4> CentresOf(int slot) {
            return _centres[slot];
        }

        /// <summary>The cloud mesh index draw slot <paramref name="slot"/> draws.</summary>
        public static int MeshOfSlot(int slot) {
            return slot % CloudFieldSettings.MeshCount;
        }

        /// <summary>True when draw slot <paramref name="slot"/> draws a coarse far mesh.</summary>
        public static bool IsFarSlot(int slot) {
            return slot >= CloudFieldSettings.MeshCount;
        }

        /// <summary>Instances drawing a coarse far mesh.</summary>
        public int FarCount { get; private set; }

        /// <summary>The far mesh choices of a class's cell (one byte per cloud, grown to <paramref name="count"/>): fetch once per cell, pass to <see cref="ResolveFar"/>.</summary>
        public byte[] BandsOf(CloudClass cloudClass, long cellX, long cellZ, int count) {
            if (_bands.TryGetValue((cloudClass, cellX, cellZ), out byte[] bands) && bands.Length >= count) return bands;

            bands = Grow(bands, count);
            _bands[(cloudClass, cellX, cellZ)] = bands;
            return bands;
        }

        /// <summary>
        /// Whether cloud <paramref name="index"/> of a cell's <paramref name="bands"/> draws coarse now, <paramref name="distance"/>
        /// metres out: decided fresh the first time the batch sees it, then kept until it leaves the band (<see cref="CloudFieldSettings.IsFarMesh(double, bool)"/>).
        /// </summary>
        public static bool ResolveFar(CloudFieldSettings settings, byte[] bands, int index, double distance) {
            byte band = bands[index];
            bool isFar = band == BandUnknown ? settings.IsFarMesh(distance) : settings.IsFarMesh(distance, band == BandFar);
            bands[index] = isFar ? BandFar : BandNear;
            return isFar;
        }

        /// <summary>Forgets the far mesh choices of cells outside the window (their clouds decide afresh if they come back).</summary>
        public void DropBandsOutside() {
            _dropped.Clear();
            foreach ((CloudClass Class, long X, long Z) cell in _bands.Keys) {
                if (!HoldsCell(cell.Class, cell.X, cell.Z)) _dropped.Add(cell);
            }
            foreach ((CloudClass Class, long X, long Z) cell in _dropped) _bands.Remove(cell);
        }

        /// <summary>Cells holding far mesh choices.</summary>
        public int BandCellCount => _bands.Count;

        private static byte[] Grow(byte[] bands, int count) {
            var grown = new byte[count];
            if (bands != null) System.Array.Copy(bands, grown, bands.Length);
            return grown;
        }

        /// <summary>Cells past the window's square a batch still holds (built ahead of the camera, kept a while behind it).</summary>
        public const int LookaheadCells = 1;

        /// <summary>True when <paramref name="cloudClass"/>'s field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) is inside this batch's window square grown by <see cref="LookaheadCells"/>.</summary>
        public bool HoldsCell(CloudClass cloudClass, long cellX, long cellZ) {
            if (!IsBuilt) return false;

            int reach = WindowCells + LookaheadCells;
            return System.Math.Abs(cellX - _cellX[(int)cloudClass]) <= reach && System.Math.Abs(cellZ - _cellZ[(int)cloudClass]) <= reach;
        }

        public void Clear() {
            foreach (List<Matrix4x4> list in _matrices) list.Clear();
            foreach (List<Vector4> list in _centres) list.Clear();

            InstanceCount = 0;
            FarCount = 0;
            _min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        /// <summary>Adds an instance whose matrix is <paramref name="matrix"/>, uniformly scaled by <paramref name="scale"/>.</summary>
        public void Add(int slot, in Matrix4x4 matrix, float scale) {
            _matrices[slot].Add(matrix);
            _centres[slot].Add(new Vector4(matrix.m03, matrix.m13, matrix.m23, scale));
            InstanceCount++;
            if (IsFarSlot(slot)) FarCount++;
            if (matrix.m03 < _min.x) _min.x = matrix.m03;
            if (matrix.m13 < _min.y) _min.y = matrix.m13;
            if (matrix.m23 < _min.z) _min.z = matrix.m23;
            if (matrix.m03 > _max.x) _max.x = matrix.m03;
            if (matrix.m13 > _max.y) _max.y = matrix.m13;
            if (matrix.m23 > _max.z) _max.z = matrix.m23;
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
