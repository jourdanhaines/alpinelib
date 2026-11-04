using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class FacetMeshTests {
        [Fact]
        public void AddFaceKeepsThreeCornersPerFace() {
            FacetMesh mesh = new FacetMesh();
            int first = mesh.AddFace(Vector3.Zero, Vector3.UnitY, Vector3.UnitX, new Vector2(0.25f, 0.5f));
            int second = mesh.AddFace(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);

            Assert.Equal(0, first);
            Assert.Equal(1, second);
            Assert.Equal(2, mesh.FaceCount);
            Assert.Equal(6, mesh.Positions.Count);
            Assert.Equal(6, mesh.Uvs.Count);
            Assert.Equal(new Vector2(0.25f, 0.5f), mesh.Uvs[2]);
        }

        [Fact]
        public void FaceNormalFollowsCrossOfEdges() {
            FacetMesh mesh = new FacetMesh();
            mesh.AddFace(Vector3.Zero, Vector3.UnitY, Vector3.UnitX);
            mesh.AddFace(Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2f);

            Assert.Equal(-Vector3.UnitZ, mesh.FaceNormal(0));
            Assert.Equal(Vector3.Zero, mesh.FaceNormal(1));
        }

        [Fact]
        public void BoundsCoverEveryCorner() {
            FacetMesh mesh = new FacetMesh();
            Assert.True(mesh.Bounds.IsEmpty);

            mesh.AddFace(new Vector3(-1f, 2f, 3f), new Vector3(4f, -5f, 0f), new Vector3(0f, 0f, -6f));
            FacetBounds bounds = mesh.Bounds;

            Assert.False(bounds.IsEmpty);
            Assert.Equal(new Vector3(-1f, -5f, -6f), bounds.Min);
            Assert.Equal(new Vector3(4f, 2f, 3f), bounds.Max);
        }

        [Fact]
        public void AppendTransformsAndKeepsWinding() {
            FacetMesh source = new FacetMesh();
            source.AddFace(Vector3.Zero, Vector3.UnitY, Vector3.UnitX, new Vector2(0.1f, 0.2f));
            FacetMesh target = new FacetMesh();

            target.Append(source, Matrix4x4.CreateTranslation(10f, 0f, 0f));

            Assert.Equal(1, target.FaceCount);
            Assert.Equal(new Vector3(10f, 0f, 0f), target.Positions[0]);
            Assert.Equal(source.FaceNormal(0), target.FaceNormal(0));
            Assert.Equal(new Vector2(0.1f, 0.2f), target.Uvs[0]);
        }

        [Fact]
        public void MirroringAppendFlipsWindingBack() {
            FacetMesh cube = LoftMesh.Build(new[] {
                new LoftRing(0f, Square()), new LoftRing(1f, Square()),
            }, true, true);
            FacetMesh mirrored = new FacetMesh();

            mirrored.Append(cube, Matrix4x4.CreateScale(-1f, 1f, 1f));

            Assert.True(FacetTopology.IsWatertight(mirrored));
            Vector3 centre = mirrored.Bounds.Centre;
            for (int face = 0; face < mirrored.FaceCount; face++) {
                Assert.True(Vector3.Dot(mirrored.FaceNormal(face), mirrored.FaceCentroid(face) - centre) > 0f);
            }
        }

        [Fact]
        public void AppendSelfDoublesOnce() {
            FacetMesh mesh = new FacetMesh();
            mesh.AddFace(Vector3.Zero, Vector3.UnitY, Vector3.UnitX);

            mesh.Append(mesh);

            Assert.Equal(2, mesh.FaceCount);
        }

        [Fact]
        public void ClearEmptiesBothStreams() {
            FacetMesh mesh = new FacetMesh(4);
            mesh.AddFace(Vector3.Zero, Vector3.UnitY, Vector3.UnitX);

            mesh.Clear();

            Assert.Equal(0, mesh.FaceCount);
            Assert.Empty(mesh.Uvs);
            Assert.Throws<ArgumentOutOfRangeException>(() => new FacetMesh(-1));
        }

        private static Vector2[] Square() {
            return new[] { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) };
        }
    }
}
