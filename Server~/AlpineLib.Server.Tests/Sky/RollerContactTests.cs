using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // Rolling props touch the ground: the outline's profile matches the posed cage, the cage's lowest point sits on flat
    // ground and on slopes, most of a roll is in contact, and jumps come only at obstacle and crest runs, seeded bumps
    // and the bounce after a landing.
    public sealed class RollerContactTests {
        private const double CageRadius = 0.45;
        private const double RailTop = 0.15;
        private const double TrackX = 0.0;
        private const double RailHalfGauge = 0.75;
        private const double CrestInner = 1.45;
        private const double CrestOuter = 1.72;
        private const double Life = 60.0;
        private static readonly RollerPathSettings Settings = RollerPathSettings.Default;

        [Fact]
        public void TheProfileIsThePosedCagesReach() {
            for (int slot = 0; slot < 20; slot++) {
                ulong seed = StableHash.Mix(41UL, (ulong)slot);
                double[] positions = RollerCage.Positions(seed, CageRadius);
                RollerShape shape = RollerShape.FromPositions(positions);
                var random = new SplitMix64(seed);
                double size = 0.65 + 0.8 * random.NextDouble();
                double squashX = 1.0 - 0.08 * random.NextDouble(), squashY = 1.0 - 0.08 * random.NextDouble(), squashZ = 1.0;
                double spin = random.NextDouble() * 6.28, tilt = (random.NextDouble() - 0.5) * 0.35;
                RollerProfile profile = RollerProfile.Of(shape, size, squashX, squashY, squashZ, spin, tilt);
                Assert.True(shape.PointCount < positions.Length / 3, $"slot {slot}: the hull kept every point");
                Assert.True(profile.MaxSupport <= shape.Reach * size + 1e-6, $"slot {slot}: reach {profile.MaxSupport:0.000} past the mesh's {shape.Reach * size:0.000}");
                Assert.True(profile.MinExtent > 0.3 * CageRadius * size, $"slot {slot}: reach down only {profile.MinExtent:0.000}");
                Assert.InRange(profile.MeanExtent, profile.MinExtent, profile.MaxExtent);
                for (int bin = 0; bin < RollerProfile.AngleBins; bin++) {
                    double angle = 2.0 * Math.PI * bin / RollerProfile.AngleBins;
                    double expected = BruteExtent(positions, size, squashX, squashY, squashZ, spin, tilt, angle);
                    Assert.True(Math.Abs(profile.Extent(angle) - expected) < 2e-3, $"slot {slot} bin {bin}: profile {profile.Extent(angle):0.0000} vs posed cage {expected:0.0000}");
                }
            }
        }

        [Fact]
        public void MostOfAFlatRollIsInContact() {
            double rolled = 0.0, airborne = 0.0, worst = 1.0;
            for (int slot = 0; slot < 200; slot++) {
                RollerPath path = Build(StableHash.Mix(43UL, (ulong)slot), -40.0, (slot % 20 - 10) * 3.0, new WindVector(1.0, 0.0), (x, z) => RollerSupport.Level(0.0));
                Assert.NotNull(path);
                foreach (RollerJump jump in path.Jumps) {
                    Assert.True(jump.Kind == RollerJumpKind.Bump || jump.Kind == RollerJumpKind.Bounce, $"slot {slot}: a {jump.Kind} jump on flat ground");
                    airborne += jump.Length;
                }
                rolled += path.Length;
                worst = Math.Min(worst, path.ContactShare());
            }
            double share = 1.0 - airborne / rolled;
            Assert.True(share >= 0.85, $"only {share:P1} of {rolled:0} rolled metres in contact");
            Assert.True(worst >= 0.75, $"a roll spends only {worst:P1} in contact");
            Assert.True(airborne > 0.0, "no roll of 200 ever bumps");
        }

        [Fact]
        public void TheCageTouchesFlatAndSlopedGround() {
            double[][] slopes = { new[] { 0.0, 0.0 }, new[] { 0.3, 0.0 }, new[] { 0.0, -0.4 }, new[] { 0.45, 0.35 }, new[] { -0.6, 0.1 } };
            foreach (double[] slope in slopes) {
                double gx = slope[0], gz = slope[1];
                double limit = gx == 0.0 && gz == 0.0 ? 0.002 : 0.005;
                Func<double, double, double> plane = (x, z) => gx * x + gz * z;
                for (int slot = 0; slot < 12; slot++) {
                    ulong seed = StableHash.Mix(47UL, (ulong)slot);
                    double[] positions = RollerCage.Positions(seed, CageRadius);
                    double size = RollerPath.SizeOf(seed, Settings);
                    double radius = CageRadius * size;
                    var world = new RollerTestWorld { Surface = plane };
                    RollerPath path = RollerPath.Build(seed, -30.0, 0.0, radius, size, Life, new WindVector(0.8, 0.6), Settings, world, RollerShape.FromPositions(positions));
                    for (double rolled = 0.1; rolled < path.Length; rolled += 0.37) {
                        RollerPose pose = RollerPose.At(path, rolled);
                        if (pose.IsAirborne) continue;

                        double gap = LowestGap(pose, path, positions, size * pose.Scale, plane);
                        Assert.True(Math.Abs(gap) <= limit, $"slope ({gx}, {gz}) slot {slot} at {pose.Distance:0.00} m: the cage is {gap:0.000} m off the ground");
                    }
                }
            }
        }

        [Fact]
        public void OnRoughSleepersTheCageDropsUntilATwigTouches() {
            Func<double, double, double> sleepers = (x, z) => Math.Abs(x - Math.Round(x / 0.375) * 0.375) < 0.1 ? 0.06 : 0.0;
            double worst = 0.0;
            for (int slot = 0; slot < 12; slot++) {
                ulong seed = StableHash.Mix(61UL, (ulong)slot);
                double[] positions = RollerCage.Positions(seed, CageRadius);
                double size = RollerPath.SizeOf(seed, Settings);
                double radius = CageRadius * size;
                var world = new RollerTestWorld { Surface = sleepers, Support = (x, z, r) => RollerSupport.Level(0.06).AsRough() };
                RollerPath path = RollerPath.Build(seed, -30.0, 0.0, radius, size, Life, new WindVector(1.0, 0.0), Settings, world, RollerShape.FromPositions(positions));
                for (double distance = path.ProfileStep * 40; distance < path.Length; distance += path.ProfileStep * 7) {
                    RollerPose pose = RollerPose.At(path, distance);
                    if (pose.IsAirborne || pose.Scale < 1.0) continue;

                    double gap = LowestGap(pose, path, positions, size, sleepers);
                    worst = Math.Max(worst, Math.Abs(gap));
                    Assert.True(Math.Abs(gap) <= 0.01, $"slot {slot} at {pose.Distance:0.00} m: the cage is {gap:0.000} m off the sleepers");
                }
            }
            Assert.True(worst > 0.0);
        }

        [Fact]
        public void JumpsComeOnlyAtObstaclesCrestsAndBumps() {
            int crossings = 0;
            for (int slot = 0; slot < 120; slot++) {
                ulong seed = StableHash.Mix(53UL, (ulong)slot);
                var prevailing = new WindVector(1.0, 0.25 * Math.Sin(slot));
                RollerPath path = Build(seed, -25.0 - slot % 7, (slot % 11 - 5) * 4.0, prevailing, null);
                Assert.NotNull(path);
                var runs = new List<double>();
                for (double distance = 0.0; distance <= path.Length; distance += path.ProfileStep) {
                    path.GroundPoint(distance, out double x, out double z);
                    if (TrackSupport(x, z, path.Radius).Obstacle != RollerObstacle.None) runs.Add(distance);
                }
                foreach (double distance in runs) {
                    Assert.True(InLeap(path, distance), $"slot {slot}: rolls over the obstacle at {distance:0.00} m");
                }
                foreach (RollerJump jump in path.Jumps) {
                    bool coversRun = runs.Exists((distance) => distance >= jump.Takeoff && distance <= jump.Landing);
                    if (jump.Kind == RollerJumpKind.Obstacle || jump.Kind == RollerJumpKind.Crest) Assert.True(coversRun, $"slot {slot}: a {jump.Kind} leap at {jump.Takeoff:0.00} m over nothing");
                    if (jump.Kind == RollerJumpKind.Bump) Assert.False(coversRun, $"slot {slot}: a bump at {jump.Takeoff:0.00} m over an obstacle");
                }
                crossings += CheckRails(path, slot);
            }
            Assert.True(crossings > 100, $"only {crossings} rail crossings checked");
        }

        [Fact]
        public void LowPropsAreHoppedClear() {
            const double rockHeight = 0.6, rockRadius = 0.5;
            int hopped = 0;
            for (int slot = 0; slot < 40; slot++) {
                ulong seed = StableHash.Mix(67UL, (ulong)slot);
                double rockX = 6.0 + slot % 5, rockZ = 0.0;
                RollerPath path = Build(seed, 0.0, 0.0, new WindVector(1.0, 0.0), (x, z) => Rock(x, z, rockX, rockZ, rockRadius, rockHeight, CageRadius * RollerPath.SizeOf(seed, Settings)));
                Assert.NotNull(path);
                for (double distance = 0.0; distance <= path.Length; distance += path.ProfileStep) {
                    path.GroundPoint(distance, out double x, out double z);
                    double dx = x - rockX, dz = z - rockZ;
                    if (dx * dx + dz * dz > rockRadius * rockRadius) continue;

                    Assert.True(InLeap(path, distance), $"slot {slot}: rolls into the rock at {distance:0.00} m");
                    RollerPose pose = RollerPose.At(path, distance);
                    double top = rockHeight * Math.Sqrt(Math.Max(0.0, 1.0 - (dx * dx + dz * dz) / (rockRadius * rockRadius)));
                    double lowest = pose.Y - path.Profile.Extent(pose.RollAngle);
                    Assert.True(lowest >= top - 0.03, $"slot {slot}: the cage clips the rock at {distance:0.00} m ({lowest:0.000} under {top:0.000})");
                    hopped++;
                }
            }
            Assert.True(hopped > 20, $"only {hopped} samples over a rock");
        }

        [Fact]
        public void TheSameSeedGivesTheSameOutlinedRoll() {
            for (int slot = 0; slot < 10; slot++) {
                ulong seed = StableHash.Mix(59UL, (ulong)slot);
                RollerPath first = Build(seed, -25.0, slot * 2.0, new WindVector(1.0, 0.1), null);
                RollerPath second = Build(seed, -25.0, slot * 2.0, new WindVector(1.0, 0.1), null);
                Assert.Equal(first.Jumps.Count, second.Jumps.Count);
                for (double rolled = 0.0; rolled < first.Length; rolled += 0.9) {
                    RollerPose one = RollerPose.At(first, rolled);
                    RollerPose two = RollerPose.At(second, rolled);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(one.Y), BitConverter.DoubleToInt64Bits(two.Y));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(one.RotationW), BitConverter.DoubleToInt64Bits(two.RotationW));
                }
                Assert.InRange(first.RollRadius, first.Profile.MinExtent, first.Profile.MaxExtent);
            }
        }

        // Every rail line the roll's centre crosses lies inside one leap: taken off before it, landed after it.
        private static int CheckRails(RollerPath path, int slot) {
            int crossings = 0;
            foreach (double rail in new[] { TrackX - RailHalfGauge, TrackX + RailHalfGauge }) {
                path.GroundPoint(0.0, out double previousX, out _);
                for (double distance = path.ProfileStep; distance <= path.Length; distance += path.ProfileStep) {
                    path.GroundPoint(distance, out double x, out _);
                    bool crossed = (previousX - rail) * (x - rail) <= 0.0;
                    previousX = x;
                    if (!crossed) continue;

                    crossings++;
                    Assert.True(InLeap(path, distance), $"slot {slot}: crosses the rail at {rail} on the ground at {distance:0.00} m");
                }
            }
            return crossings;
        }

        private static bool InLeap(RollerPath path, double distance) {
            foreach (RollerJump jump in path.Jumps) {
                if (jump.Kind != RollerJumpKind.Bounce && jump.Kind != RollerJumpKind.Bump && distance >= jump.Takeoff && distance <= jump.Landing) return true;
            }
            return false;
        }

        private static RollerPath Build(ulong seed, double startX, double startZ, WindVector prevailing, Func<double, double, RollerSupport> support) {
            double size = RollerPath.SizeOf(seed, Settings);
            double radius = CageRadius * size;
            support ??= (x, z) => TrackSupport(x, z, radius);
            var world = new RollerTestWorld { Support = (x, z, r) => support(x, z) };
            return RollerPath.Build(seed, startX, startZ, radius, size, Life, prevailing, Settings, world, RollerCage.Shape(seed, CageRadius));
        }

        // Level ground with a track along z at x 0: two rail heads (points at rail top) a ball touching them jumps,
        // and the shoulders where it pops.
        private static RollerSupport TrackSupport(double x, double z, double radius) {
            double lateral = Math.Abs(x - TrackX);
            if (lateral >= CrestInner && lateral <= CrestOuter) return new RollerSupport(0.0, 0.0, 1.0, 0.0, RollerObstacle.Crest);

            RollerSupport best = RollerSupport.Level(0.0);
            foreach (double rail in new[] { TrackX - RailHalfGauge, TrackX + RailHalfGauge }) {
                double offset = x - rail;
                if (Math.Abs(offset) >= radius) continue;

                double up = Math.Sqrt(radius * radius - offset * offset);
                double height = RailTop + up - radius;
                if (height > best.Height) best = new RollerSupport(height, offset / radius, up / radius, 0.0, RollerObstacle.Obstacle);
            }
            return best;
        }

        // A dome of `height` over a disc, inflated by the ball's radius, as an obstacle the prop jumps.
        private static RollerSupport Rock(double x, double z, double rockX, double rockZ, double rockRadius, double height, double radius) {
            double dx = x - rockX, dz = z - rockZ;
            double reach = rockRadius + radius;
            double share = Math.Sqrt(dx * dx + dz * dz) / reach;
            if (share >= 1.0) return RollerSupport.Level(0.0);

            return new RollerSupport(height * Math.Sqrt(1.0 - share * share), 0.0, 1.0, 0.0, RollerObstacle.Obstacle);
        }

        // Least height of any cage point over the ground beneath it, the cage posed as the field draws it.
        private static double LowestGap(RollerPose pose, RollerPath path, double[] positions, double size, Func<double, double, double> ground) {
            double lowest = double.MaxValue;
            for (int index = 0; index < positions.Length; index += 3) {
                double[] world = Rotate(pose, positions[index] * path.SquashX * size, positions[index + 1] * path.SquashY * size, positions[index + 2] * path.SquashZ * size);
                double x = pose.X + world[0], y = pose.Y + world[1], z = pose.Z + world[2];
                lowest = Math.Min(lowest, y - ground(x, z));
            }
            return lowest;
        }

        private static double BruteExtent(double[] positions, double size, double sx, double sy, double sz, double spin, double tilt, double angle) {
            double most = double.MinValue;
            for (int index = 0; index < positions.Length; index += 3) {
                double x = positions[index] * sx * size, y = positions[index + 1] * sy * size, z = positions[index + 2] * sz * size;
                double tippedY = y * Math.Cos(tilt) - z * Math.Sin(tilt);
                double tippedZ = y * Math.Sin(tilt) + z * Math.Cos(tilt);
                double spunZ = -x * Math.Sin(spin) + tippedZ * Math.Cos(spin);
                most = Math.Max(most, -tippedY * Math.Cos(angle) + spunZ * Math.Sin(angle));
            }
            return most;
        }

        // v' = v + 2w(q × v) + 2 q × (q × v)
        private static double[] Rotate(RollerPose pose, double x, double y, double z) {
            double qx = pose.RotationX, qy = pose.RotationY, qz = pose.RotationZ, qw = pose.RotationW;
            double tx = 2.0 * (qy * z - qz * y);
            double ty = 2.0 * (qz * x - qx * z);
            double tz = 2.0 * (qx * y - qy * x);
            return new[] { x + qw * tx + (qy * tz - qz * ty), y + qw * ty + (qz * tx - qx * tz), z + qw * tz + (qx * ty - qy * tx) };
        }
    }
}
