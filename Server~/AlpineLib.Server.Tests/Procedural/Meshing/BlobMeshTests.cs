using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class BlobMeshTests {
        [Theory]
        [InlineData(0, 20)]
        [InlineData(1, 80)]
        [InlineData(2, 320)]
        public void FaceCountFollowsSubdivisions(int subdivisions, int faces) {
            BlobSpec spec = BlobSpec.Default;
            spec.Subdivisions = subdivisions;

            Assert.Equal(faces, BlobMesh.Build(spec, 1UL).FaceCount);
            Assert.Equal(faces, Icosphere.FaceCount(subdivisions));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void BlobIsWatertight(int subdivisions) {
            BlobSpec spec = BlobSpec.Default;
            spec.Subdivisions = subdivisions;
            spec.NoiseAmplitude = 0.3f;
            spec.FacetJitter = 0.1f;
            spec.FlattenBelowY = -0.3f;
            spec.Squash = 0.5f;

            Assert.True(FacetTopology.IsWatertight(BlobMesh.Build(spec, 42UL)));
        }

        [Fact]
        public void UndisplacedBlobFacesOutwardInsideItsRadii() {
            BlobSpec spec = BlobSpec.Default;
            spec.Subdivisions = 2;
            spec.NoiseAmplitude = 0f;
            spec.FacetJitter = 0f;
            spec.Radii = new Vector3(2f, 1f, 3f);
            FacetMesh mesh = BlobMesh.Build(spec, 3UL);

            for (int face = 0; face < mesh.FaceCount; face++) {
                Assert.True(Vector3.Dot(mesh.FaceNormal(face), mesh.FaceCentroid(face)) > 0f);
            }
            FacetBounds bounds = mesh.Bounds;
            Assert.True(bounds.Max.X <= 2f + 1e-5f && bounds.Max.Y <= 1f + 1e-5f && bounds.Max.Z <= 3f + 1e-5f);
            Assert.True(bounds.Min.X >= -2f - 1e-5f && bounds.Min.Y >= -1f - 1e-5f && bounds.Min.Z >= -3f - 1e-5f);
        }

        [Fact]
        public void SameSeedSameCornersOtherSeedDiffers() {
            BlobSpec spec = BlobSpec.Default;
            FacetMesh first = BlobMesh.Build(spec, 7UL);
            FacetMesh again = BlobMesh.Build(spec, 7UL);
            FacetMesh other = BlobMesh.Build(spec, 8UL);

            Assert.Equal(first.Positions, again.Positions);
            Assert.NotEqual(first.Positions, other.Positions);
        }

        [Fact]
        public void FlattenClampsTheBase() {
            BlobSpec spec = BlobSpec.Default;
            spec.FlattenBelowY = -0.25f;
            FacetMesh mesh = BlobMesh.Build(spec, 5UL);

            Assert.Equal(-0.25f, mesh.Bounds.Min.Y);
        }

        [Fact]
        public void SquashPressesOnlyTheLowerHalf() {
            BlobSpec round = BlobSpec.Default;
            round.NoiseAmplitude = 0f;
            round.FacetJitter = 0f;
            BlobSpec squashed = round;
            squashed.Squash = 0.6f;

            FacetBounds roundBounds = BlobMesh.Build(round, 1UL).Bounds;
            FacetBounds squashedBounds = BlobMesh.Build(squashed, 1UL).Bounds;

            Assert.Equal(roundBounds.Max.Y, squashedBounds.Max.Y);
            Assert.Equal(roundBounds.Min.Y * 0.4f, squashedBounds.Min.Y, 5);
        }

        [Fact]
        public void AppendOffsetsTheBlob() {
            FacetMesh mesh = new FacetMesh();
            BlobMesh.AppendTo(mesh, BlobSpec.Default, 2UL, new Vector3(100f, 0f, 0f));

            Assert.True(mesh.Bounds.Min.X > 98f);
        }

        [Fact]
        public void RejectsBadSpecs() {
            BlobSpec levels = BlobSpec.Default;
            levels.Subdivisions = 3;
            BlobSpec radii = BlobSpec.Default;
            radii.Radii = new Vector3(1f, 0f, 1f);

            Assert.Throws<ArgumentOutOfRangeException>(() => BlobMesh.Build(levels, 1UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlobMesh.Build(radii, 1UL));
        }
    }
}
