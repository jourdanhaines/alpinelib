using System;
using System.Linq;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Landmarks;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Landmarks {
    public sealed class LandmarkFieldGoldenTests {
        [Fact]
        public void FingerprintsMatchTheGoldenTable() {
            for (int index = 0; index < LandmarkFieldGolden.Seeds.Length; index++) {
                ulong seed = LandmarkFieldGolden.Seeds[index];
                Assert.Equal(LandmarkFieldGolden.Fingerprints[index], LandmarkFieldGolden.Fingerprint(seed));
            }
        }

        [Fact]
        public void ClusterFingerprintsMatchTheGoldenTable() {
            var actual = new ulong[LandmarkFieldGolden.Seeds.Length];
            for (int index = 0; index < actual.Length; index++) {
                actual[index] = LandmarkFieldGolden.ClusterFingerprint(LandmarkFieldGolden.Seeds[index]);
            }
            Assert.True(LandmarkFieldGolden.ClusterFingerprints.SequenceEqual(actual), string.Join(", ", actual.Select((hash) => $"0x{hash:X16}")));
        }

        [Fact]
        public void SitesMatchTheGoldenTableBitForBit() {
            LandmarkField field = LandmarkFieldGolden.CreateField(LandmarkFieldGolden.SiteSeed);
            foreach (LandmarkSite expected in LandmarkFieldGolden.Sites) {
                Assert.True(field.TryGetSite(expected.Tier, expected.Block, out LandmarkSite actual));
                Assert.Equal(expected, actual);
                Assert.Equal(Bits(expected.OffsetX), Bits(actual.OffsetX));
                Assert.Equal(Bits(expected.OffsetZ), Bits(actual.OffsetZ));
                for (int index = 0; index < LandmarkDraws.Count; index++) {
                    Assert.Equal(Bits(expected.UDraws[index]), Bits(actual.UDraws[index]));
                }
            }
        }

        [Fact]
        public void AbsentBlocksStayEmpty() {
            LandmarkField field = LandmarkFieldGolden.CreateField(LandmarkFieldGolden.SiteSeed);
            foreach ((int tier, CellCoord block) in LandmarkFieldGolden.AbsentBlocks) {
                Assert.False(field.TryGetSite(tier, block, out _));
            }
        }

        private static long Bits(double value) {
            return BitConverter.DoubleToInt64Bits(value);
        }
    }
}
