using System;
using System.Numerics;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Builds a closed, faceted blob from a <see cref="BlobSpec"/>: an icosphere whose shared vertices are
    /// displaced by seeded noise, then emitted as a flat-shaded <see cref="FacetMesh"/>.
    /// </summary>
    /// <remarks>
    /// Displacement is computed once per shared vertex in double precision, so neighbouring faces meet
    /// exactly and the result is watertight. Same spec and seed give the same corners on every runtime.
    /// Static and thread-safe.
    /// </remarks>
    public static class BlobMesh {
        private const double MinRadiusScale = 0.2;
        private const ulong NoiseSalt = 0x4E4F495345UL;
        private const ulong JitterSalt = 0x4A49545445UL;

        /// <summary>A new blob mesh centred on the origin.</summary>
        public static FacetMesh Build(in BlobSpec spec, ulong seed) {
            FacetMesh mesh = new FacetMesh(Icosphere.FaceCount(spec.Subdivisions));
            AppendTo(mesh, spec, seed, Vector3.Zero);
            return mesh;
        }

        /// <summary>Appends a blob centred on <paramref name="offset"/> to <paramref name="target"/>.</summary>
        public static void AppendTo(FacetMesh target, in BlobSpec spec, ulong seed, Vector3 offset) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            Validate(spec);
            double[] directions = Icosphere.GetDirections(spec.Subdivisions);
            int[] triangles = Icosphere.GetTriangles(spec.Subdivisions);
            int vertexCount = directions.Length / 3;
            Span<Vector3> corners = stackalloc Vector3[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++) {
                corners[vertex] = Displace(spec, seed, directions, vertex) + offset;
            }
            for (int corner = 0; corner < triangles.Length; corner += 3) {
                target.AddFace(corners[triangles[corner]], corners[triangles[corner + 1]], corners[triangles[corner + 2]]);
            }
        }

        private static void Validate(in BlobSpec spec) {
            if (spec.Subdivisions < 0 || spec.Subdivisions > Icosphere.MaxSubdivisions) {
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Subdivisions, "Subdivisions must be in [0, 2].");
            }
            if (!(spec.Radii.X > 0f) || !(spec.Radii.Y > 0f) || !(spec.Radii.Z > 0f)) {
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Radii, "Radii must be positive.");
            }
        }

        private static Vector3 Displace(in BlobSpec spec, ulong seed, double[] directions, int vertex) {
            double directionX = directions[vertex * 3];
            double directionY = directions[vertex * 3 + 1];
            double directionZ = directions[vertex * 3 + 2];
            double frequency = spec.NoiseFrequency;
            double noise = GradientNoise.Sample3(StableHash.Mix(seed, NoiseSalt),
                directionX * frequency, directionY * frequency, directionZ * frequency);
            double jitter = LatticeHash.Unit(StableHash.Mix(seed, JitterSalt), vertex) * 2.0 - 1.0;
            double scale = Math.Max(MinRadiusScale, 1.0 + spec.NoiseAmplitude * noise + spec.FacetJitter * jitter);

            double x = directionX * scale * spec.Radii.X;
            double y = directionY * scale * spec.Radii.Y;
            double z = directionZ * scale * spec.Radii.Z;
            if (y < 0.0) {
                y *= 1.0 - Math.Min(1.0, Math.Max(0.0, spec.Squash));
            }
            if (spec.FlattenBelowY.HasValue && y < spec.FlattenBelowY.Value) {
                y = spec.FlattenBelowY.Value;
            }
            return new Vector3((float)x, (float)y, (float)z);
        }
    }
}
