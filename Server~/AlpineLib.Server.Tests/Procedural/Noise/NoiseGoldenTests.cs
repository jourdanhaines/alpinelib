using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    /// <summary>
    /// The determinism contract: exact values at fixed points. Any change here changes every world built
    /// from noise, so a failing golden means a deliberate version bump, never a quiet re-record.
    /// </summary>
    /// <remarks>
    /// The noise core is double add/subtract/multiply, Math.Floor and integer hashing only (no trig, no
    /// fused multiply-add), so Mono, IL2CPP and .NET reproduce these bit for bit.
    /// </remarks>
    public sealed class NoiseGoldenTests {
        private const ulong Seed = 0x5EEDUL;

        private static readonly double[] PointsX = { 0.1, 0.5, 1.3, -7.25, 12.345, -100.01, 1e5 + 0.37, 3.999999 };
        private static readonly double[] PointsY = { 0.2, 0.25, -2.7, 3.5, 67.891, -0.5, -1e5 + 0.61, 4.000001 };
        private static readonly double[] PointsZ = { 0.3, 0.75, 0.1, -1.125, -3.21, 50.5, 0.33, -0.000001 };

        private static readonly FbmSettings GoldenFbm = new FbmSettings(4, 0.37);
        private static readonly FbmSettings GoldenWarp = new FbmSettings(2, 0.11);

        [Fact]
        public void GradientSample1() {
            double[] expected = {
                -0.10600079377577445, -0.24095802336090466, -0.11083308526981747, -0.12657048880225574,
                -0.30007427039420603, 0.017359291480335992, -0.02110258333460481, 4.76358955059375E-07,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], GradientNoise.Sample1(Seed, PointsX[index]));
            }
        }

        [Fact]
        public void GradientSample2() {
            double[] expected = {
                -0.17581318510932997, -0.6055128419473994, 0.3200555145913249, 0.5729309046156758,
                0.2059930162864178, -0.578927988547581, 0.7055084909426003, -1.4142135619481461E-06,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], GradientNoise.Sample2(Seed, PointsX[index], PointsY[index]));
            }
        }

        [Fact]
        public void GradientSample3() {
            double[] expected = {
                -0.1621239308483424, 0.06629962266426162, 0.2082071448673191, 0.32957605318753896,
                0.10705325578014326, -0.10002181642884489, 0.14844937125533353, -1.0255800994045674E-15,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], GradientNoise.Sample3(Seed, PointsX[index], PointsY[index], PointsZ[index]));
            }
        }

        [Fact]
        public void FbmSample1() {
            double[] expected = {
                0.041849221764285564, 0.1063113511650317, -0.04246163263730838, 0.12010719238277308,
                -0.22668043677472685, -0.0040283814349674875, -0.06732708796000861, -0.14137955467148994,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], Fbm.Sample1(Seed, PointsX[index], GoldenFbm));
            }
        }

        [Fact]
        public void FbmSample2() {
            double[] expected = {
                -0.00884517051134459, 0.019630934423754572, 0.06436500125762408, 0.2479108814720888,
                -0.42383768911683767, -0.016568061316175085, -0.0008007265252733209, 0.17337297763711187,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], Fbm.Sample2(Seed, PointsX[index], PointsY[index], GoldenFbm));
            }
        }

        [Fact]
        public void DomainWarp2() {
            double[] expectedX = {
                0.118755564509906, 0.936621674966827, 3.3381518513352795, -5.669404557317284,
                12.028882981050403, -99.60084216248181, 100000.10390344125, 3.3242793813056895,
            };
            double[] expectedY = {
                -0.04194748370283213, -0.3713193369198683, -1.9284518553969745, 5.850344015828313,
                69.0619956382908, -0.22954454987344058, -99999.57937101112, 3.6739783746673655,
            };
            for (int index = 0; index < expectedX.Length; index++) {
                double x = PointsX[index];
                double y = PointsY[index];
                DomainWarp.Warp2(Seed, ref x, ref y, 6.0, GoldenWarp);
                Assert.Equal(expectedX[index], x);
                Assert.Equal(expectedY[index], y);
            }
        }

        [Fact]
        public void NoiseField2Sample() {
            NoiseField2 field = new NoiseField2(Seed, new FbmSettings(3, 1.0 / 18.0), 6.0, new FbmSettings(2, 1.0 / 60.0));
            double[] expected = {
                -0.007030058320603982, -0.012375237804370668, -0.01238286384686762, 0.13121151237962955,
                0.08275455081424168, -0.18014289920038412, 0.19714573425715717, 0.09944865134777081,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], field.Sample(PointsX[index], PointsY[index]));
            }
        }

        [Fact]
        public void LatticeUnit2() {
            double[] expected = {
                0.6425324757875297, 0.8238476368896439, 0.09967944933870476, 0.4329681504339974,
                0.42484221995558713, 0.730046095084233, 0.724742941136352, 0.6785794309556501,
            };
            for (int index = 0; index < expected.Length; index++) {
                Assert.Equal(expected[index], LatticeHash.Unit(Seed, Lattice(PointsX[index]), Lattice(PointsY[index])));
            }
        }

        [Fact]
        public void LatticeHash3() {
            ulong[] expected = {
                0xADA619179BD4ECB4UL, 0x9F17210E9B14D271UL, 0x062DC4DFAF18F5EFUL, 0x59475EEFD3E09EA3UL,
                0x48D85E6D09FA6E2EUL, 0xD2AFD50A0B673C53UL, 0x730D17229308A10CUL, 0x5F29229265F31CC8UL,
            };
            for (int index = 0; index < expected.Length; index++) {
                ulong actual = LatticeHash.Hash(Seed, Lattice(PointsX[index]), Lattice(PointsY[index]), Lattice(PointsZ[index]));
                Assert.Equal(expected[index], actual);
            }
        }

        private static long Lattice(double coordinate) {
            return (long)System.Math.Floor(coordinate * 10.0);
        }
    }
}
