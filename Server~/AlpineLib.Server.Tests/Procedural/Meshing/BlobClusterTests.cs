using System.Numerics;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    public sealed class BlobClusterTests {
        private static readonly BlobPuff[] Puffs = {
            new BlobPuff(new Vector3(0f, 0f, 0f), new Vector3(4f, 3f, 4f)),
            new BlobPuff(new Vector3(5f, 1f, 0f), new Vector3(3f, 2.5f, 3f)),
            new BlobPuff(new Vector3(-4f, 0.5f, 2f), new Vector3(2.5f, 2f, 2.5f)),
        };

        [Fact]
        public void OneBlobPerPuff() {
            FacetMesh mesh = BlobCluster.Build(Puffs, BlobSpec.Default, 9UL);

            Assert.Equal(3 * 80, mesh.FaceCount);
        }

        [Fact]
        public void EachPuffIsClosedSoTheUnionIsToo() {
            Assert.True(FacetTopology.IsWatertight(BlobCluster.Build(Puffs, BlobSpec.Default, 9UL)));
        }

        [Fact]
        public void FlattenIsOneSharedBaseInClusterSpace() {
            BlobSpec spec = BlobSpec.Default;
            spec.FlattenBelowY = -1f;
            FacetMesh mesh = BlobCluster.Build(Puffs, spec, 9UL);

            Assert.InRange(mesh.Bounds.Min.Y, -1f - 1e-5f, -1f + 1e-5f);
        }

        [Fact]
        public void DeterministicBySeedAndPuffsDiffer() {
            FacetMesh first = BlobCluster.Build(Puffs, BlobSpec.Default, 9UL);
            FacetMesh again = BlobCluster.Build(Puffs, BlobSpec.Default, 9UL);
            FacetMesh other = BlobCluster.Build(Puffs, BlobSpec.Default, 10UL);

            Assert.Equal(first.Positions, again.Positions);
            Assert.NotEqual(first.Positions, other.Positions);
        }
    }
}
