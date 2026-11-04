using AlpineLib.Procedural;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    /// <summary>Holds CoreCLR to <see cref="MeshGoldenTable"/>; the Unity editor gate holds Mono to it.</summary>
    public sealed class MeshGoldenTests {
        [Fact]
        public void BuildersMatchRecordedHashes() {
            Assert.Equal(MeshGoldenTable.ExpectedBlobHash, MeshGoldenTable.BuildBlob().ComputePositionHash());
            Assert.Equal(MeshGoldenTable.ExpectedClusterHash, MeshGoldenTable.BuildCluster().ComputePositionHash());
            Assert.Equal(MeshGoldenTable.ExpectedLoftHash, MeshGoldenTable.BuildLoft().ComputePositionHash());
            Assert.Empty(MeshGoldenTable.FindMismatches());
        }

        [Fact]
        public void PositionHashSeesEveryBit() {
            FacetMesh mesh = MeshGoldenTable.BuildLoft();
            ulong before = mesh.ComputePositionHash();
            mesh.Positions[5] += new System.Numerics.Vector3(0f, 1e-6f, 0f);

            Assert.NotEqual(before, mesh.ComputePositionHash());
            Assert.Equal(StableHash.Fnv1a64OffsetBasis, new FacetMesh().ComputePositionHash());
        }

        [Fact]
        public void PuffSeedsAreSaltedAwayFromLatticeHashes() {
            for (int index = 0; index < 32; index++) {
                Assert.NotEqual(LatticeHash.Hash(9UL, index), BlobCluster.PuffSeed(9UL, index));
            }
        }
    }
}
