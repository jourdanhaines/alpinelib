using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The shape of a faceted blob: a displaced icosphere for rocks, cloud puffs and smoke.
    /// </summary>
    /// <remarks>
    /// Displacement fractions are of the local radius, so one spec scales with <see cref="Radii"/>.
    /// Order of operations per vertex: noise + jitter on the radius, scale by radii, squash the lower
    /// half, then clamp to <see cref="FlattenBelowY"/>.
    /// </remarks>
    public struct BlobSpec {
        /// <summary>Icosphere level 0–2: 20, 80 or 320 faces.</summary>
        public int Subdivisions;

        /// <summary>Half-extents along x, y and z before displacement.</summary>
        public Vector3 Radii;

        /// <summary>Smooth 3D-noise displacement, as a fraction of the radius (0.2 = ±20 %).</summary>
        public float NoiseAmplitude;

        /// <summary>Noise frequency in cycles across the unit sphere (≈ 1–3 for lumpy blobs).</summary>
        public float NoiseFrequency;

        /// <summary>Independent per-vertex radial jitter, as a fraction of the radius.</summary>
        public float FacetJitter;

        /// <summary>When set, every vertex below this blob-local height is lifted onto it (flat base).</summary>
        public float? FlattenBelowY;

        /// <summary>0–1: how far the lower half is pressed toward y = 0 (0 = round, 0.6 = cloud belly).</summary>
        public float Squash;

        /// <summary>A lumpy level-1 unit blob: noise 0.15 at frequency 1.5, jitter 0.05, no flatten or squash.</summary>
        public static BlobSpec Default => new BlobSpec {
            Subdivisions = 1,
            Radii = Vector3.One,
            NoiseAmplitude = 0.15f,
            NoiseFrequency = 1.5f,
            FacetJitter = 0.05f,
            FlattenBelowY = null,
            Squash = 0f,
        };
    }
}
