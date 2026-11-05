using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class SphereEnvelopeFitterTests {
        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        public void TheSphereHoldsEveryPoint(double topWeight) {
            FacetMesh blob = BlobMesh.Build(new BlobSpec { Subdivisions = 1, Radii = new Vector3(1.3f, 0.9f, 1.1f), NoiseAmplitude = 0.2f, FlattenBelowY = -0.4f }, 11UL);

            EnvelopeSphere sphere = SphereEnvelopeFitter.Fit(blob.Positions, topWeight);

            Assert.All(blob.Positions, (point) => Assert.Equal(0.0, sphere.DistanceOutside(point)));
        }

        [Fact]
        public void WeightingTheTopLowersTheCrown() {
            FacetMesh blob = BlobMesh.Build(new BlobSpec { Subdivisions = 1, Radii = new Vector3(1.3f, 0.6f, 1.3f), FlattenBelowY = -0.3f }, 5UL);

            EnvelopeSphere smallest = SphereEnvelopeFitter.Fit(blob.Positions, 0.0);
            EnvelopeSphere low = SphereEnvelopeFitter.Fit(blob.Positions, 2.0);

            Assert.True(low.Center.Y + low.Radius <= smallest.Center.Y + smallest.Radius + 1e-4);
        }

        [Fact]
        public void TwoApartCuboidsAreTwoPieces() {
            List<Vector3> look = EnvelopeShapes.Cuboid(Vector3.Zero, Vector3.One, 0);
            look.AddRange(EnvelopeShapes.Cuboid(new Vector3(5f, 0f, 0f), Vector3.One, 30));

            List<List<int>> pieces = TrianglePieces.Split(look);

            Assert.Equal(2, pieces.Count);
            Assert.Equal(12, pieces[0].Count);
            Assert.Equal(Enumerable.Range(12, 12), pieces[1]);
        }
    }
}
