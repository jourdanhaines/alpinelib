using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>One blob of a <see cref="BlobCluster"/>: where it sits and how big it is.</summary>
    public readonly struct BlobPuff {
        /// <summary>A puff at <paramref name="centre"/> with half-extents <paramref name="radii"/>.</summary>
        public BlobPuff(Vector3 centre, Vector3 radii) {
            Centre = centre;
            Radii = radii;
        }

        /// <summary>The puff centre in cluster space.</summary>
        public Vector3 Centre { get; }

        /// <summary>The puff half-extents; replaces <see cref="BlobSpec.Radii"/> for this puff.</summary>
        public Vector3 Radii { get; }
    }
}
