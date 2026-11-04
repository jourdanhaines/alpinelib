using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The determinism contract for the faceted builders: position hashes of one fixed mesh per builder,
    /// checked by the .NET tests and by the Unity editor gate.
    /// </summary>
    /// <remarks>
    /// The loft uses a literal polygon with no twist, because <see cref="LoftMesh.JitterPolygon"/> and
    /// twist go through <c>Math.Cos/Sin</c>, which is not pinned across runtimes.
    /// </remarks>
    public static class MeshGoldenTable {
        /// <summary>The recorded hash of <see cref="BuildBlob"/>.</summary>
        public const ulong ExpectedBlobHash = 0x010B860A7E3F192DUL;

        /// <summary>The recorded hash of <see cref="BuildCluster"/>.</summary>
        public const ulong ExpectedClusterHash = 0xD7FA40F57361E057UL;

        /// <summary>The recorded hash of <see cref="BuildLoft"/>.</summary>
        public const ulong ExpectedLoftHash = 0xAF9D2ECC5BE940A3UL;

        private const ulong Seed = 0x5EEDUL;

        /// <summary>A level-2 squashed, flattened blob.</summary>
        public static FacetMesh BuildBlob() {
            BlobSpec spec = BlobSpec.Default;
            spec.Subdivisions = 2;
            spec.Radii = new Vector3(3f, 2f, 2.5f);
            spec.NoiseAmplitude = 0.25f;
            spec.FacetJitter = 0.08f;
            spec.Squash = 0.4f;
            spec.FlattenBelowY = -0.9f;
            return BlobMesh.Build(spec, Seed);
        }

        /// <summary>A three-puff cloud with a shared flat base.</summary>
        public static FacetMesh BuildCluster() {
            BlobSpec spec = BlobSpec.Default;
            spec.FlattenBelowY = -1f;
            BlobPuff[] puffs = {
                new BlobPuff(new Vector3(0f, 0f, 0f), new Vector3(4f, 3f, 4f)),
                new BlobPuff(new Vector3(5f, 1f, 0f), new Vector3(3f, 2.5f, 3f)),
                new BlobPuff(new Vector3(-4f, 0.5f, 2f), new Vector3(2.5f, 2f, 2.5f)),
            };
            return BlobCluster.Build(puffs, spec, Seed);
        }

        /// <summary>A capped three-ring hexagonal column from literal polygons.</summary>
        public static FacetMesh BuildLoft() {
            Vector2[] wide = {
                new Vector2(2f, 0f), new Vector2(1f, 1.75f), new Vector2(-1.1f, 1.6f),
                new Vector2(-2.1f, 0.1f), new Vector2(-0.9f, -1.7f), new Vector2(1.2f, -1.8f),
            };
            Vector2[] narrow = {
                new Vector2(1.25f, 0.1f), new Vector2(0.5f, 1f), new Vector2(-0.75f, 0.9f),
                new Vector2(-1.3f, 0f), new Vector2(-0.6f, -1.1f), new Vector2(0.7f, -1f),
            };
            LoftRing[] rings = { new LoftRing(0f, wide), new LoftRing(2.5f, wide), new LoftRing(4.75f, narrow) };
            return LoftMesh.Build(rings, true, true);
        }

        /// <summary>Every builder whose hash differs from the record, as readable lines; empty when the runtime agrees.</summary>
        public static List<string> FindMismatches() {
            List<string> mismatches = new List<string>();
            AddIfDifferent(mismatches, "BlobMesh", ExpectedBlobHash, BuildBlob().ComputePositionHash());
            AddIfDifferent(mismatches, "BlobCluster", ExpectedClusterHash, BuildCluster().ComputePositionHash());
            AddIfDifferent(mismatches, "LoftMesh", ExpectedLoftHash, BuildLoft().ComputePositionHash());
            return mismatches;
        }

        private static void AddIfDifferent(List<string> mismatches, string builder, ulong expected, ulong actual) {
            if (expected == actual) {
                return;
            }
            mismatches.Add($"{builder}: expected 0x{expected:X16}, got 0x{actual:X16}");
        }
    }
}
