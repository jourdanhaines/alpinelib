using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlpineLib.Procedural.Noise {
    /// <summary>
    /// The determinism contract for the noise module: exact outputs at eight fixed points per function,
    /// checked by the .NET tests and by the Unity editor gate so CoreCLR and Mono are held to the same bits.
    /// </summary>
    /// <remarks>
    /// A mismatch means every world built from noise changed. Re-record only with a deliberate version
    /// bump of whatever consumes the noise.
    /// </remarks>
    public static class NoiseGoldenTable {
        /// <summary>The seed every row samples with.</summary>
        public const ulong Seed = 0x5EEDUL;

        /// <summary>Number of points per row.</summary>
        public const int PointCount = 8;

        private const string RoundTrip = "R";

        private static readonly double[] PointsX = { 0.1, 0.5, 1.3, -7.25, 12.345, -100.01, 1e5 + 0.37, 3.999999 };
        private static readonly double[] PointsY = { 0.2, 0.25, -2.7, 3.5, 67.891, -0.5, -1e5 + 0.61, 4.000001 };
        private static readonly double[] PointsZ = { 0.3, 0.75, 0.1, -1.125, -3.21, 50.5, 0.33, -0.000001 };

        private static readonly FbmSettings GoldenFbm = new FbmSettings(4, 0.37);
        private static readonly FbmSettings GoldenWarp = new FbmSettings(2, 0.11);
        private static readonly NoiseField2 GoldenField = new NoiseField2(Seed, new FbmSettings(3, 1.0 / 18.0), 6.0, new FbmSettings(2, 1.0 / 60.0));

        // Rows in NoiseGoldenRow order. Recorded on CoreCLR, confirmed under the Unity editor (Mono).
        private static readonly double[][] Expected = {
            // GradientSample1
            new[] {
                -0.10600079377577445, -0.24095802336090466, -0.11083308526981747, -0.12657048880225574,
                -0.30007427039420603, 0.017359291480335992, -0.02110258333460481, 4.76358955059375E-07,
            },
            // GradientSample2
            new[] {
                -0.17581318510932997, -0.6055128419473994, 0.3200555145913249, 0.5729309046156758,
                0.2059930162864178, -0.578927988547581, 0.7055084909426003, -1.4142135619481461E-06,
            },
            // GradientSample3
            new[] {
                -0.19516078531971803, 0.07980984890911659, 0.2506346206192899, 0.3967355160097939,
                0.1288680662981779, -0.12040379320446032, 0.17869968808929532, -1.234568003383174E-15,
            },
            // FbmSample1
            new[] {
                -0.27336029909054765, -0.2907507463584673, -0.016384029970630664, 0.3689271242511666,
                0.2786905256098331, -0.08986243822593316, -0.32654979420225855, -0.34858960605254086,
            },
            // FbmSample2
            new[] {
                -0.10806961228574315, -0.14442974426613953, 0.3015623572218297, -0.05728665388934996,
                -0.40200524574080615, -0.06643443160684971, -0.09009332063188702, 0.00833959825208218,
            },
            // DomainWarpX
            new[] {
                1.0352565622549363, 1.2046918255664587, 0.3304368176915875, -6.955649423992395,
                12.622852154074724, -98.22835459565614, 100001.70916773527, 3.8081524704646017,
            },
            // DomainWarpY
            new[] {
                0.5620745059980721, 0.3000687295978359, -4.96376470229344, 3.455217322816457,
                66.43383358366667, -1.6237290440979975, -99999.98858836429, 3.129857756100094,
            },
            // NoiseField2
            new[] {
                -0.25048884170730396, -0.2700698841442295, -0.21639793467430943, -0.11910626440132324,
                0.08578504416714657, -0.3689874431719748, 0.09428934239848089, -0.22086956782221254,
            },
            // LatticeUnit2
            new[] {
                0.6425324757875297, 0.8238476368896439, 0.09967944933870476, 0.4329681504339974,
                0.42484221995558713, 0.730046095084233, 0.724742941136352, 0.6785794309556501,
            },
        };

        private static readonly ulong[] ExpectedHash3 = {
            0xADA619179BD4ECB4UL, 0x9F17210E9B14D271UL, 0x062DC4DFAF18F5EFUL, 0x59475EEFD3E09EA3UL,
            0x48D85E6D09FA6E2EUL, 0xD2AFD50A0B673C53UL, 0x730D17229308A10CUL, 0x5F29229265F31CC8UL,
        };

        /// <summary>The recorded value of <paramref name="row"/> at <paramref name="point"/>.</summary>
        public static double ExpectedValue(NoiseGoldenRow row, int point) {
            return Expected[(int)row][point];
        }

        /// <summary>The recorded 3D lattice hash at <paramref name="point"/>.</summary>
        public static ulong ExpectedLatticeHash3(int point) {
            return ExpectedHash3[point];
        }

        /// <summary>What <paramref name="row"/> evaluates to at <paramref name="point"/> on this runtime.</summary>
        public static double Compute(NoiseGoldenRow row, int point) {
            double x = PointsX[point];
            double y = PointsY[point];
            double z = PointsZ[point];
            switch (row) {
                case NoiseGoldenRow.GradientSample1: return GradientNoise.Sample1(Seed, x);
                case NoiseGoldenRow.GradientSample2: return GradientNoise.Sample2(Seed, x, y);
                case NoiseGoldenRow.GradientSample3: return GradientNoise.Sample3(Seed, x, y, z);
                case NoiseGoldenRow.FbmSample1: return Fbm.Sample1(Seed, x, GoldenFbm);
                case NoiseGoldenRow.FbmSample2: return Fbm.Sample2(Seed, x, y, GoldenFbm);
                case NoiseGoldenRow.DomainWarpX: return Warped(x, y, true);
                case NoiseGoldenRow.DomainWarpY: return Warped(x, y, false);
                case NoiseGoldenRow.NoiseField2: return GoldenField.Sample(x, y);
                case NoiseGoldenRow.LatticeUnit2: return LatticeHash.Unit(Seed, Lattice(x), Lattice(y));
                default: throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown golden row.");
            }
        }

        /// <summary>The 3D lattice hash at <paramref name="point"/> on this runtime.</summary>
        public static ulong ComputeLatticeHash3(int point) {
            return LatticeHash.Hash(Seed, Lattice(PointsX[point]), Lattice(PointsY[point]), Lattice(PointsZ[point]));
        }

        /// <summary>Every value that differs from the record, as readable lines; empty when the runtime agrees.</summary>
        public static List<string> FindMismatches() {
            List<string> mismatches = new List<string>();
            foreach (NoiseGoldenRow row in (NoiseGoldenRow[])Enum.GetValues(typeof(NoiseGoldenRow))) {
                for (int point = 0; point < PointCount; point++) {
                    AddIfDifferent(mismatches, row, point);
                }
            }
            for (int point = 0; point < PointCount; point++) {
                ulong actual = ComputeLatticeHash3(point);
                if (actual != ExpectedHash3[point]) {
                    mismatches.Add($"LatticeHash3[{point}]: expected 0x{ExpectedHash3[point]:X16}, got 0x{actual:X16}");
                }
            }
            return mismatches;
        }

        private static void AddIfDifferent(List<string> mismatches, NoiseGoldenRow row, int point) {
            double expected = ExpectedValue(row, point);
            double actual = Compute(row, point);
            if (BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual)) {
                return;
            }
            string expectedText = expected.ToString(RoundTrip, CultureInfo.InvariantCulture);
            string actualText = actual.ToString(RoundTrip, CultureInfo.InvariantCulture);
            mismatches.Add($"{row}[{point}]: expected {expectedText}, got {actualText}");
        }

        private static double Warped(double x, double y, bool wantX) {
            DomainWarp.Warp2(Seed, ref x, ref y, 6.0, GoldenWarp);
            return wantX ? x : y;
        }

        private static long Lattice(double coordinate) {
            return (long)Math.Floor(coordinate * 10.0);
        }
    }
}
