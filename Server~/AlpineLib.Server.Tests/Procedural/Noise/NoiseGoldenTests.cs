using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    /// <summary>
    /// Holds CoreCLR to <see cref="NoiseGoldenTable"/>; the Unity editor gate holds Mono to the same table.
    /// </summary>
    public sealed class NoiseGoldenTests {
        public static IEnumerable<object[]> Rows() {
            foreach (NoiseGoldenRow row in (NoiseGoldenRow[])Enum.GetValues(typeof(NoiseGoldenRow))) {
                yield return new object[] { row };
            }
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public void RowMatchesRecord(NoiseGoldenRow row) {
            for (int point = 0; point < NoiseGoldenTable.PointCount; point++) {
                Assert.Equal(NoiseGoldenTable.ExpectedValue(row, point), NoiseGoldenTable.Compute(row, point));
            }
        }

        [Fact]
        public void LatticeHash3MatchesRecord() {
            for (int point = 0; point < NoiseGoldenTable.PointCount; point++) {
                Assert.Equal(NoiseGoldenTable.ExpectedLatticeHash3(point), NoiseGoldenTable.ComputeLatticeHash3(point));
            }
        }

        [Fact]
        public void TableReportsNoMismatches() {
            Assert.Empty(NoiseGoldenTable.FindMismatches());
        }
    }
}
