using System;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // Rolls steer round the stops ahead instead of breaking up on them: in a desert-dense field of round stops nine in ten
    // walks roll a quarter kilometre, none comes nearer a stop than 0.1 m, and the heading stays within the off-wind
    // limit; a wall across the wind boxes a roll in, which then breaks up while still rolling.
    public sealed class RollerSteeringTests {
        private const double FreeRoll = 250.0;
        private const double LeastClearance = 0.1;
        private const int Walks = 300;

        [Fact]
        public void SteeringClearsADiscField() {
            RollerPathSettings settings = RollerPathSettings.Default;
            int free = 0, steers = 0;
            double least = double.PositiveInfinity, widest = 0.0;
            for (int walk = 0; walk < Walks; walk++) {
                RollerDiscField field = RollerDiscField.Clustered(5000UL + (ulong)walk, 7.0, 700.0);
                RollerPath path = Build(field, StableHash.Mix(31UL, (ulong)walk), walk, settings);
                if (path == null) continue;

                free += path.Length >= FreeRoll || path.End == RollerEnd.Life ? 1 : 0;
                steers += path.Steers;
                least = Math.Min(least, LeastAlong(field, path));
                widest = Math.Max(widest, WidestOffWind(path, Prevailing(walk)));
            }
            Assert.True(free >= Walks * 0.9, $"only {free} of {Walks} walks rolled {FreeRoll} m ({steers} steers)");
            Assert.True(least >= LeastClearance, $"a walk came {least:0.000} m from a stop");
            Assert.True(widest <= RollerPathSettings.MaxOffWindDegrees + 1e-6, $"a heading strayed {widest:0.0} deg off the wind");
            Assert.True(steers > Walks, $"only {steers} steers over {Walks} walks");
        }

        [Fact]
        public void AWallAcrossTheWindBoxesARollInAndItBreaksUpRolling() {
            RollerPathSettings settings = RollerPathSettings.Default;
            int boxed = 0;
            for (int walk = 0; walk < 20; walk++) {
                WindVector wind = Prevailing(walk);
                var world = RollerTestWorld.Open();
                for (double across = -600.0; across <= 600.0; across += 5.0) world.AddStop(wind.X * 40.0 + wind.Z * across, wind.Z * 40.0 - wind.X * across, 3.0);
                ulong seed = StableHash.Mix(37UL, (ulong)walk);
                double size = RollerPath.SizeOf(seed, settings);
                RollerPath path = RollerPath.Build(seed, 0.0, 0.0, 0.45 * size, size, settings.MaxLife, wind, settings, world, null);
                Assert.NotNull(path);
                Assert.Equal(RollerEnd.Blocked, path.End);
                Assert.True(path.Boxed > 0, $"walk {walk}: blocked without being boxed in");
                boxed++;
                double previous = -1.0;
                for (double rolled = path.Length - path.FadeLength; rolled <= path.Length; rolled += path.FadeLength / 10.0) {
                    RollerPose pose = RollerPose.At(path, rolled);
                    Assert.True(pose.Distance > previous, $"walk {walk}: the fade out holds still");
                    previous = pose.Distance;
                }
            }
            Assert.Equal(20, boxed);
        }

        private static RollerPath Build(IRollerWorld world, ulong seed, int walk, RollerPathSettings settings) {
            double size = RollerPath.SizeOf(seed, settings);
            WindVector wind = Prevailing(walk);
            return RollerPath.Build(seed, -wind.X * 300.0, -wind.Z * 300.0, 0.45 * size, size, settings.MaxLife, wind, settings, world, null);
        }

        // Least gap along the roll, short of a blocked roll's last half metre (it ends against a stop).
        private static double LeastAlong(RollerDiscField field, RollerPath path) {
            double least = double.PositiveInfinity;
            double end = path.End == RollerEnd.Blocked ? path.Length - 0.5 : path.Length;
            for (double distance = 0.0; distance <= end; distance += 0.05) {
                path.GroundPoint(distance, out double x, out double z);
                least = Math.Min(least, field.Clearance(x, z, path.Radius));
            }
            return least;
        }

        private static double WidestOffWind(RollerPath path, WindVector wind) {
            double widest = 0.0;
            for (double distance = 0.0; distance <= path.Length; distance += 0.5) {
                path.HeadingAt(distance, out double x, out double z);
                widest = Math.Max(widest, Math.Abs(Math.Atan2(x * wind.Z - z * wind.X, x * wind.X + z * wind.Z)) * 180.0 / Math.PI);
            }
            return widest;
        }

        private static WindVector Prevailing(int walk) {
            double angle = walk * 0.7;
            return new WindVector(Math.Sin(angle), Math.Cos(angle));
        }
    }
}
