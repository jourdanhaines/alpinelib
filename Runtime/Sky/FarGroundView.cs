using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>One camera's <see cref="FarGround"/>: its lattice and where it was last placed.</summary>
    internal sealed class FarGroundView {
        public FarGroundLattice Lattice { get; set; }
        public int LastUsedFrame { get; set; }
        public bool IsPlaced { get; set; }
        public int Revision { get; set; }
        public Vector3 CameraWorld { get; set; }
        public double AbsoluteX { get; set; }
        public double AbsoluteZ { get; set; }
        public Matrix4x4 Matrix { get; set; }
        public Bounds Bounds { get; set; }
    }
}
