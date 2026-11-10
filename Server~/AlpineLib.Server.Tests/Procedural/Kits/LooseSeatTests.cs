using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Kits {
    public sealed class LooseSeatTests {
        // The values the consuming game passes (its landmark fallen rocks and cleft debris).
        private static readonly LooseSeatSettings Settings = new LooseSeatSettings(7, new[] { -0.6, -0.2, 0.2, 0.6 }, 0.15);
        // The game's surface reads a piece within 5 cm of its edge.
        private const double SeamReach = 0.05;

        [Fact]
        public void RecordedRocksSeatExactlyAsTheGameDid() {
            Assert.NotEmpty(LooseSeatGolden.Cases);
            foreach (SeatCase seatCase in LooseSeatGolden.Cases) {
                var rotation = new DegreeRotation(seatCase.Yaw, seatCase.Pitch, seatCase.Roll);
                Func<double, double, double> surface = (x, z) => Surface(seatCase, x, z);

                double seated = LooseSeat.Seated(rotation, seatCase.X, seatCase.CentreY, seatCase.Z, seatCase.HalfX, seatCase.HalfY, seatCase.HalfZ, surface, Settings);
                double buried = LooseSeat.Buried(rotation, seatCase.X, seated, seatCase.Z, seatCase.HalfX, seatCase.HalfY, seatCase.HalfZ, surface);

                Assert.Equal(BitConverter.DoubleToInt64Bits(seatCase.Seated), BitConverter.DoubleToInt64Bits(seated));
                Assert.Equal(BitConverter.DoubleToInt64Bits(seatCase.Buried), BitConverter.DoubleToInt64Bits(buried));
            }
        }

        [Fact]
        public void RecordedRocksNeitherFloatNorSinkPastTheirSamples() {
            foreach (SeatCase seatCase in LooseSeatGolden.Cases) {
                var rotation = new DegreeRotation(seatCase.Yaw, seatCase.Pitch, seatCase.Roll);
                AssertSeated(rotation, seatCase.X, seatCase.CentreY, seatCase.Z, seatCase.HalfX, seatCase.HalfY, seatCase.HalfZ, (x, z) => Surface(seatCase, x, z));
            }
        }

        [Fact]
        public void AnUprightBoxOnFlatGroundSinksUntilItsOuterLowPointsAreWithinTheSlack() {
            var upright = new DegreeRotation(37, 0, 0);

            double seated = LooseSeat.Seated(upright, 3.0, 10.0, -2.0, 1.0, 0.5, 2.0, (x, z) => 1.25, Settings);

            // The lattice's outer points (0.6, 0.6) stand 0.5·(1 − √0.28) over the box bottom: more than the slack.
            double expected = 1.25 + 0.5 * Math.Sqrt(1.0 - 0.72) + 0.15;
            Assert.Equal(expected, seated, 12);
            Assert.Equal((1.25 - (expected - 0.5)) / 1.0, LooseSeat.Buried(upright, 3.0, seated, -2.0, 1.0, 0.5, 2.0, (x, z) => 1.25), 12);
        }

        [Fact]
        public void ARockAlreadyLowStaysWhereItIs() {
            var tilted = new DegreeRotation(10, 20, -15);

            Assert.Equal(-3.0, LooseSeat.Seated(tilted, 0.0, -3.0, 0.0, 1.0, 1.0, 1.0, (x, z) => 0.0, Settings));
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(45, 15, 0)]
        [InlineData(200, -25, 30)]
        [InlineData(90, 40, -40)]
        public void TiltedRocksOnASlopeTouchWithoutFloating(int yaw, int pitch, int roll) {
            var rotation = new DegreeRotation(yaw, pitch, roll);
            Func<double, double, double> slope = (x, z) => 0.6 * x - 0.2 * z + 4.0;

            AssertSeated(rotation, 2.0, 50.0, -1.0, 1.2, 0.8, 0.9, slope);
        }

        [Fact]
        public void BuriedIsTheShareOfTheRoundedHeightUnderTheSurface() {
            var upright = new DegreeRotation(0, 0, 0);

            Assert.Equal(0.25, LooseSeat.Buried(upright, 0.0, 1.0, 0.0, 1.0, 2.0, 1.0, (x, z) => 0.0), 12);
            Assert.Equal(1.0, LooseSeat.Buried(upright, 0.0, -2.0, 0.0, 1.0, 2.0, 1.0, (x, z) => 0.0), 12);
        }

        [Fact]
        public void BadSettingsAndSurfacesThrow() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LooseSeatSettings(1, new[] { 0.0 }, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LooseSeatSettings(3, new[] { -0.8, 0.8 }, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LooseSeatSettings(3, new[] { 0.5 }, -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LooseSeatSettings(3, new[] { double.NaN }, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LooseSeatSettings(3, new[] { 0.5 }, double.NaN));
            Assert.Throws<ArgumentNullException>(() => LooseSeat.Seated(new DegreeRotation(0, 0, 0), 0, 0, 0, 1, 1, 1, null, Settings));
            Assert.Throws<ArgumentException>(() => LooseSeat.Seated(new DegreeRotation(0, 0, 0), 0, 0, 0, 1, 1, 1, (x, z) => 0.0, default));
        }

        [Fact]
        public void SettingsKeepTheirOwnCopyOfTheLattice() {
            var lattice = new[] { -0.6, 0.6 };
            var settings = new LooseSeatSettings(3, lattice, 0.1);
            double before = LooseSeat.Seated(new DegreeRotation(0, 0, 0), 0, 5, 0, 1, 1, 1, (x, z) => 0.0, settings);

            lattice[0] = 0.0;

            Assert.Equal(-0.6, settings.LowLattice[0]);
            Assert.Equal(before, LooseSeat.Seated(new DegreeRotation(0, 0, 0), 0, 5, 0, 1, 1, 1, (x, z) => 0.0, settings));
            Assert.False(settings.LowLattice is double[]);
        }

        // Seated: every bottom grid point at or under the surface, every low point within the slack over it, and at least one
        // sample touching (else it would have sunk no further than it started).
        private static void AssertSeated(DegreeRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, Func<double, double, double> surfaceAt) {
            double seated = LooseSeat.Seated(rotation, x, centreY, z, halfX, halfY, halfZ, surfaceAt, Settings);
            Assert.True(seated <= centreY);
            double highest = double.NegativeInfinity;
            for (int row = 0; row < Settings.BottomGrid; row++) {
                for (int column = 0; column < Settings.BottomGrid; column++) {
                    double shareX = row * 2.0 / (Settings.BottomGrid - 1) - 1.0, shareZ = column * 2.0 / (Settings.BottomGrid - 1) - 1.0;
                    double over = Over(rotation, x, seated, z, shareX * halfX, -halfY, shareZ * halfZ, surfaceAt);
                    Assert.True(over <= 1e-9, $"bottom point {over} over the surface");
                    highest = Math.Max(highest, over);
                }
            }
            foreach (double shareX in Settings.LowLattice) {
                foreach (double shareZ in Settings.LowLattice) {
                    double over = Over(rotation, x, seated, z, shareX * halfX, -halfY * Math.Sqrt(1.0 - shareX * shareX - shareZ * shareZ), shareZ * halfZ, surfaceAt);
                    Assert.True(over <= Settings.LowPointSlack + 1e-9, $"low point {over} over the surface");
                    highest = Math.Max(highest, over - Settings.LowPointSlack);
                }
            }
            if (seated < centreY) Assert.True(highest >= -1e-9, $"seated {-highest} under its first contact");
        }

        private static double Over(DegreeRotation rotation, double x, double y, double z, double localX, double localY, double localZ, Func<double, double, double> surfaceAt) {
            rotation.Apply(localX, localY, localZ, out double turnedX, out double turnedY, out double turnedZ);
            return y + turnedY - surfaceAt(x + turnedX, z + turnedZ);
        }

        // The game's walkable surface: the highest talus piece over (x, z) or within 5 cm, a plinth top, else the ground.
        private static double Surface(SeatCase seatCase, double x, double z) {
            double surface = 0.0;
            foreach (SlopedPrismPiece slope in seatCase.Slopes) {
                if (ConvexPlan.Contains(slope.Plan, x, z, SeamReach)) surface = Math.Max(surface, slope.HeightAt(x, z));
            }
            if (seatCase.Plinths == null) return surface;

            foreach ((List<PlanPoint> plan, double top) in seatCase.Plinths) {
                if (ConvexPlan.Contains(plan, x, z, SeamReach)) surface = Math.Max(surface, top);
            }
            return surface;
        }
    }
}
