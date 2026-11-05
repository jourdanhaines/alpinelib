using System;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // A blocker box grown from its parts' local boxes in the vehicle's frame keeps the car's own size at any heading,
    // where the world-axis box of a car yawed 45 deg would reach metres past its sides.
    public sealed class RollerBlockerBoxTests {
        private const double HalfLength = 6.0;
        private const double HalfWidth = 1.6;
        private const double HalfHeight = 1.8;

        [Fact]
        public void AYawedCarKeepsItsOwnSize() {
            foreach (double yaw in new[] { 0.0, 0.3, Math.PI / 4.0, 2.0 }) {
                var box = new RollerBlockerBox();
                RollerAffine vehicleToWorld = RollerAffine.YawThenMove(yaw, 120.0, 4.0, -35.0);
                RollerAffine hullToWorld = vehicleToWorld.After(RollerAffine.YawThenMove(0.02, 0.0, 1.0, 0.0));
                box.AddPart(0.0, 0.8, 0.0, HalfWidth, HalfHeight, HalfLength, vehicleToWorld.Inverse().After(hullToWorld));
                Assert.InRange(box.MaxX - box.MinX, 2.0 * HalfWidth, 2.0 * HalfWidth + 0.3);
                Assert.InRange(box.MaxZ - box.MinZ, 2.0 * HalfLength, 2.0 * HalfLength + 0.1);

                double worldHalfX = HalfWidth * Math.Abs(Math.Cos(yaw)) + HalfLength * Math.Abs(Math.Sin(yaw));
                if (yaw == Math.PI / 4.0) Assert.True(worldHalfX - HalfWidth > 3.0, "the world-axis box would not be inflated");
                Assert.False(box.Touches(HalfWidth + 0.6, 1.0, 0.0, 0.4), $"yaw {yaw}: a ball 0.2 m off the side touches");
                Assert.True(box.Touches(HalfWidth + 0.3, 1.0, 0.0, 0.4), $"yaw {yaw}: a ball against the side does not touch");
            }
        }

        [Fact]
        public void PartsGrowTheBoxAndAnEmptyBoxTouchesNothing() {
            var box = new RollerBlockerBox();
            Assert.False(box.Touches(0.0, 0.0, 0.0, 10.0));
            box.AddPart(0.0, 0.0, 0.0, 1.0, 1.0, 1.0, RollerAffine.Identity);
            box.AddPart(0.0, 0.0, 0.0, 0.5, 0.5, 0.5, RollerAffine.YawThenMove(0.0, 0.0, 0.0, 8.0));
            RollerAffine turn = RollerAffine.YawThenMove(0.7, 3.0, 1.0, -2.0);
            turn.Inverse().After(turn).Apply(1.5, -2.0, 4.0, out double x, out double y, out double z);
            Assert.Equal(1.5, x, 9);
            Assert.Equal(-2.0, y, 9);
            Assert.Equal(4.0, z, 9);
            Assert.Equal(8.5, box.MaxZ, 9);
            Assert.Equal(-1.0, box.MinZ, 9);
            box.Clear();
            Assert.True(box.IsEmpty);
        }
    }
}
