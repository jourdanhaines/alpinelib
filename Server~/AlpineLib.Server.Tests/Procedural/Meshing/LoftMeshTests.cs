using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class LoftMeshTests {
        [Theory]
        [InlineData(6, 2, true, true, 6 * 2 + 12)]
        [InlineData(8, 4, true, false, 8 * 6 + 8)]
        [InlineData(5, 3, false, false, 5 * 4)]
        public void TriangleCountIsBandsPlusCaps(int sides, int ringCount, bool capTop, bool capBottom, int faces) {
            Assert.Equal(faces, LoftMesh.Build(Column(sides, ringCount, 0f), capTop, capBottom).FaceCount);
        }

        [Fact]
        public void CappedLoftIsWatertight() {
            Assert.True(FacetTopology.IsWatertight(LoftMesh.Build(Column(9, 5, 0.3f), true, true)));
        }

        [Fact]
        public void OpenEndsLeaveOnlyTheRingEdges() {
            FacetMesh mesh = LoftMesh.Build(Column(7, 3, 0.2f), false, false);

            Assert.False(FacetTopology.IsWatertight(mesh));
            Assert.Equal(14, FacetTopology.OpenEdgeCount(mesh));
        }

        [Fact]
        public void FacesPointOutward() {
            FacetMesh mesh = LoftMesh.Build(Column(8, 2, 0f), true, true);
            Vector3 centre = mesh.Bounds.Centre;

            for (int face = 0; face < mesh.FaceCount; face++) {
                Assert.True(Vector3.Dot(mesh.FaceNormal(face), mesh.FaceCentroid(face) - centre) > 0f, $"face {face}");
            }
        }

        [Fact]
        public void ReversedPolygonsStillFaceOutward() {
            Vector2[] polygon = LoftMesh.JitterPolygon(1UL, 6, 2f, 2f, 0f, false);
            Array.Reverse(polygon);
            FacetMesh mesh = LoftMesh.Build(new[] { new LoftRing(0f, polygon), new LoftRing(3f, polygon) }, true, true);

            Assert.True(FacetTopology.IsWatertight(mesh));
            Vector3 centre = mesh.Bounds.Centre;
            for (int face = 0; face < mesh.FaceCount; face++) {
                Assert.True(Vector3.Dot(mesh.FaceNormal(face), mesh.FaceCentroid(face) - centre) > 0f);
            }
        }

        [Fact]
        public void TwistRotatesTheRing() {
            Vector2[] polygon = { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) };
            FacetMesh mesh = LoftMesh.Build(new[] {
                new LoftRing(0f, polygon), new LoftRing(1f, polygon, (float)(Math.PI / 2.0)),
            }, true, true);

            Assert.True(FacetTopology.IsWatertight(mesh));
            Assert.Contains(mesh.Positions, (position) => position.Y == 1f && Math.Abs(position.X) < 1e-6f && Math.Abs(position.Z - 1f) < 1e-6f);
        }

        [Fact]
        public void RejectsMismatchedRings() {
            Vector2[] square = LoftMesh.JitterPolygon(1UL, 4, 1f, 1f, 0f, false);
            Vector2[] pentagon = LoftMesh.JitterPolygon(1UL, 5, 1f, 1f, 0f, false);

            Assert.Throws<ArgumentException>(() => LoftMesh.Build(new[] { new LoftRing(0f, square) }, true, true));
            Assert.Throws<ArgumentException>(() => LoftMesh.Build(new[] { new LoftRing(0f, square), new LoftRing(1f, pentagon) }, true, true));
            Assert.Throws<ArgumentNullException>(() => LoftMesh.Build(null, true, true));
        }

        [Fact]
        public void InwardOnlyJitterNeverExceedsTheRadii() {
            for (ulong seed = 0; seed < 200; seed++) {
                Vector2[] polygon = LoftMesh.JitterPolygon(seed, 3 + (int)(seed % 10), 2.5f, 1.75f, 1f, true);
                foreach (Vector2 point in polygon) {
                    Assert.True(Math.Abs(point.X) <= 2.5f && Math.Abs(point.Y) <= 1.75f, $"seed {seed} point {point}");
                }
            }
        }

        [Fact]
        public void JitterPolygonKeepsAngleOrderAndSeed() {
            Vector2[] polygon = LoftMesh.JitterPolygon(17UL, 12, 3f, 3f, 1f, false);
            Vector2[] again = LoftMesh.JitterPolygon(17UL, 12, 3f, 3f, 1f, false);
            Vector2[] other = LoftMesh.JitterPolygon(18UL, 12, 3f, 3f, 1f, false);

            Assert.Equal(12, polygon.Length);
            Assert.Equal(polygon, again);
            Assert.NotEqual(polygon, other);
            double previous = double.NegativeInfinity;
            for (int index = 0; index < polygon.Length; index++) {
                double angle = Math.Atan2(polygon[index].Y, polygon[index].X);
                angle = index > polygon.Length / 2 && angle < 0.0 ? angle + 2.0 * Math.PI : angle;
                Assert.True(angle > previous);
                previous = angle;
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => LoftMesh.JitterPolygon(1UL, 2, 1f, 1f, 0f, false));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CapsNeverFoldAtFullJitter(bool inwardOnly) {
            for (ulong seed = 0; seed < 600; seed++) {
                int sides = 4 + (int)(seed % 9);
                Vector2[] polygon = LoftMesh.JitterPolygon(seed, sides, 3f, 2f, 1f, inwardOnly);
                float twist = (seed % 3) * 0.7f;
                FacetMesh mesh = LoftMesh.Build(new[] { new LoftRing(0f, polygon, twist), new LoftRing(2f, polygon, twist) }, true, true);
                AssertCapsFace(mesh, sides * 2, sides, seed);
            }
        }

        [Fact]
        public void OrientationComesFromTheFirstRingWithArea() {
            Vector2[] tip = { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero };
            Vector2[] polygon = LoftMesh.JitterPolygon(3UL, 5, 2f, 2f, 0.2f, true);
            Array.Reverse(polygon);
            FacetMesh mesh = LoftMesh.Build(new[] { new LoftRing(0f, tip), new LoftRing(2f, polygon) }, true, false);

            for (int face = 0; face < mesh.FaceCount; face++) {
                Vector3 normal = mesh.FaceNormal(face);
                Vector3 centroid = mesh.FaceCentroid(face);
                bool outward = face >= 10 ? normal.Y > 0f : normal.X * centroid.X + normal.Z * centroid.Z >= 0f;
                Assert.True(outward, $"face {face}");
            }
            Assert.Throws<ArgumentException>(() => LoftMesh.Build(new[] { new LoftRing(0f, tip), new LoftRing(1f, tip) }, true, true));
        }

        private static void AssertCapsFace(FacetMesh mesh, int firstCap, int sides, ulong seed) {
            for (int face = firstCap; face < firstCap + sides; face++) {
                Assert.True(mesh.FaceNormal(face).Y < 0f, $"seed {seed} bottom face {face}");
            }
            for (int face = firstCap + sides; face < firstCap + 2 * sides; face++) {
                Assert.True(mesh.FaceNormal(face).Y > 0f, $"seed {seed} top face {face}");
            }
        }

        private static LoftRing[] Column(int sides, int ringCount, float jitter) {
            LoftRing[] rings = new LoftRing[ringCount];
            for (int ring = 0; ring < ringCount; ring++) {
                Vector2[] polygon = LoftMesh.JitterPolygon((ulong)ring, sides, 2f - ring * 0.2f, 1.5f, jitter, true);
                rings[ring] = new LoftRing(ring * 1.5f, polygon);
            }
            return rings;
        }
    }
}
