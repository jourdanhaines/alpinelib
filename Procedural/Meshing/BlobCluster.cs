using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Builds a clump of overlapping blobs (a cloud, a smoke puff, a rock pile) as one faceted mesh.
    /// </summary>
    /// <remarks>
    /// The union is by appending: each puff is a closed blob and overlapping interiors are kept. That is
    /// invisible for opaque flat-shaded clouds and smoke, but the result is not one manifold surface, so
    /// do not use it where a single shell matters (collision, transparency). Puff <c>n</c> uses seed
    /// <see cref="PuffSeed"/>, and <see cref="BlobSpec.FlattenBelowY"/> is read in cluster space, so every
    /// puff shares one flat base.
    /// </remarks>
    public static class BlobCluster {
        private const ulong PuffSalt = 0x50554646UL;

        /// <summary>A new mesh holding one blob per puff.</summary>
        public static FacetMesh Build(IReadOnlyList<BlobPuff> puffs, in BlobSpec spec, ulong seed) {
            if (puffs == null) {
                throw new ArgumentNullException(nameof(puffs));
            }
            FacetMesh mesh = new FacetMesh(puffs.Count * Icosphere.FaceCount(spec.Subdivisions));
            AppendTo(mesh, puffs, spec, seed);
            return mesh;
        }

        /// <summary>Appends one blob per puff to <paramref name="target"/>.</summary>
        public static void AppendTo(FacetMesh target, IReadOnlyList<BlobPuff> puffs, in BlobSpec spec, ulong seed) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            if (puffs == null) {
                throw new ArgumentNullException(nameof(puffs));
            }
            for (int index = 0; index < puffs.Count; index++) {
                BlobPuff puff = puffs[index];
                BlobSpec puffSpec = spec;
                puffSpec.Radii = puff.Radii;
                if (spec.FlattenBelowY.HasValue) {
                    puffSpec.FlattenBelowY = spec.FlattenBelowY.Value - puff.Centre.Y;
                }
                BlobMesh.AppendTo(target, puffSpec, PuffSeed(seed, index), puff.Centre);
            }
        }

        /// <summary>The seed puff <paramref name="index"/> builds with: <c>Mix(Mix(seed, salt), index)</c>.</summary>
        public static ulong PuffSeed(ulong seed, int index) {
            return StableHash.Mix(StableHash.Mix(seed, PuffSalt), (ulong)index);
        }
    }
}
