using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class FacetUvTests {
        [Fact]
        public void BandTIsInUnitRangeAndFallsWithHeight() {
            FacetMesh mesh = LoftMesh.Build(Column(), true, true);
            FacetUv.BandByHeight(mesh, (t) => new Vector2((float)t, 0f), 0.0, 6.0);

            for (int face = 0; face < mesh.FaceCount; face++) {
                for (int other = 0; other < mesh.FaceCount; other++) {
                    AssertMonotonic(mesh, face, other);
                }
                float t = mesh.Uvs[face * 3].X;
                Assert.InRange(t, 0f, 1f);
                Assert.Equal(mesh.Uvs[face * 3], mesh.Uvs[face * 3 + 2]);
            }
        }

        [Fact]
        public void LowEndIsOneHighEndIsZeroClamped() {
            Assert.Equal(1.0, FacetUv.BandT(0.0, 0.0, 4.0));
            Assert.Equal(0.0, FacetUv.BandT(4.0, 0.0, 4.0));
            Assert.Equal(0.75, FacetUv.BandT(1.0, 0.0, 4.0));
            Assert.Equal(1.0, FacetUv.BandT(-10.0, 0.0, 4.0));
            Assert.Equal(0.0, FacetUv.BandT(10.0, 0.0, 4.0));
        }

        [Fact]
        public void BandRangeLeavesOtherFacesAlone() {
            FacetMesh mesh = LoftMesh.Build(Column(), false, false);
            FacetUv.BandByHeight(mesh, (t) => Vector2.One, 0.0, 6.0, 2, 3);

            Assert.Equal(Vector2.Zero, mesh.Uvs[0]);
            Assert.Equal(Vector2.One, mesh.Uvs[2 * 3]);
            Assert.Equal(Vector2.Zero, mesh.Uvs[5 * 3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => FacetUv.BandByHeight(mesh, (t) => Vector2.One, 0.0, 6.0, 0, mesh.FaceCount + 1));
            Assert.Throws<ArgumentException>(() => FacetUv.BandByHeight(mesh, (t) => Vector2.One, 6.0, 6.0));
        }

        [Fact]
        public void PaintFacesHonoursThePredicate() {
            FacetMesh mesh = LoftMesh.Build(Column(), true, true);
            Vector2 cap = new Vector2(0.9f, 0.1f);

            int painted = FacetUv.PaintFaces(mesh, (face) => mesh.FaceNormal(face).Y > 0.99f, cap);

            Assert.Equal(6, painted);
            Assert.Equal(mesh.FaceCount, FacetUv.PaintFaces(mesh, null, Vector2.Zero));
        }

        private static void AssertMonotonic(FacetMesh mesh, int face, int other) {
            float height = mesh.FaceCentroid(face).Y;
            float otherHeight = mesh.FaceCentroid(other).Y;
            if (height >= otherHeight) {
                return;
            }
            Assert.True(mesh.Uvs[face * 3].X >= mesh.Uvs[other * 3].X);
        }

        private static LoftRing[] Column() {
            Vector2[] polygon = LoftMesh.JitterPolygon(4UL, 6, 2f, 2f, 0.3f, true);
            return new[] { new LoftRing(0f, polygon), new LoftRing(3f, polygon), new LoftRing(6f, polygon) };
        }
    }
}
