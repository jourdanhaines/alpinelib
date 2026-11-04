using System;
using System.Collections.Generic;
using System.Text;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class RollerPathTests {
        private const double Frame = 1.0 / 60.0;
        private const double StressWind = 20.0;
        private const double JumpLimit = 0.1;
        private const double ClearSlack = 0.02;
        private const double SteepestClimb = 4.0;
        private static readonly RollerPathSettings Settings = RollerPathSettings.Default;

        [Fact]
        public void SameSeedSameRollEverywhere() {
            var prevailing = new WindVector(0.6, 0.8);
            int differing = 0;
            for (int slot = 0; slot < 40; slot++) {
                RollerPathScene scene = RollerPathScene.Random(1000UL + (ulong)slot);
                RollerPath first = scene.Build(StableHash.Mix(5UL, (ulong)slot), prevailing);
                RollerPath second = scene.Build(StableHash.Mix(5UL, (ulong)slot), prevailing);
                RollerPath other = scene.Build(StableHash.Mix(6UL, (ulong)slot), prevailing);
                Assert.Equal(Describe(first), Describe(second));
                differing += Describe(first) != Describe(other) ? 1 : 0;
            }
            Assert.True(differing > 30, $"only {differing} of 40 rolls differ across seeds");
        }

        [Fact]
        public void RollsStayInsideTheCellAndClearOfFootprints() {
            int built = 0;
            for (int slot = 0; slot < 400; slot++) {
                RollerPathScene scene = RollerPathScene.Random(2000UL + (ulong)slot);
                RollerPath path = scene.Build(StableHash.Mix(9UL, (ulong)slot), Prevailing(slot));
                if (path == null) continue;

                built++;
                for (double distance = 0.0; distance <= path.Length; distance += 0.05) {
                    path.GroundPoint(distance, out double x, out double z);
                    double penetration = scene.Penetration(x, z, path.Radius);
                    Assert.True(penetration <= ClearSlack, $"slot {slot} at {distance:0.00} m: {penetration:0.000} m into a footprint or past the cell");
                }
            }
            Assert.True(built > 200, $"only {built} of 400 slots got a roll");
        }

        [Fact]
        public void LengthsJumpsAndWobbleStayInBounds() {
            for (int slot = 0; slot < 300; slot++) {
                RollerPathScene scene = RollerPathScene.Open(0.3 + slot % 4 * 0.1);
                RollerPath path = scene.Build(StableHash.Mix(11UL, (ulong)slot), Prevailing(slot), (slot % 7 - 3) * 0.05);
                Assert.NotNull(path);
                Assert.InRange(path.Length, Settings.MinClippedLength, Settings.MaxLength + 1e-9);
                Assert.InRange(path.RestGap, Settings.MinRestGap, Settings.MaxRestGap);
                Assert.InRange(path.StartOffset, -Settings.UpwindReach - 1e-9, 0.0);
                Assert.InRange(path.BumpChance, Settings.MinBumpChance, Settings.MaxBumpChance);
                CheckJumps(path, $"slot {slot}");
                CheckPoses(path, scene);
            }
        }

        [Fact]
        public void LoopsWithoutVisibleJumpsAtTwentyMetresPerSecond() {
            double fastest = 0.0;
            int stalls = 0;
            for (int slot = 0; slot < 60; slot++) {
                RollerPathScene scene = RollerPathScene.Open(0.45);
                WindVector prevailing = Prevailing(slot);
                RollerPath path = scene.Build(StableHash.Mix(13UL, (ulong)slot), prevailing);
                fastest = Math.Max(fastest, path.GroundSpeedShare);
                stalls += path.HasStall ? 1 : 0;
                double travel = StressWind * path.GroundSpeedShare * Frame;
                int frames = (int)Math.Ceiling(path.Cycle * 1.2 / travel);
                int wraps = 0;
                RollerPose previous = RollerPose.Of(path, WindAt(prevailing, 0.0));
                for (int frame = 1; frame <= frames; frame++) {
                    RollerPose next = RollerPose.Of(path, WindAt(prevailing, frame * Frame));
                    if (next.Distance < previous.Distance - 1.0) {
                        wraps++;
                        // A wrap pops at most half the jump limit of ball radius: at the fastest share a frame covers ~1/6 of the scale-out.
                        double pop = Math.Max(previous.Scale, next.Scale) * path.Radius;
                        Assert.True(pop < JumpLimit * 0.5, $"slot {slot}: wrapped at visible scale {previous.Scale:0.000} -> {next.Scale:0.000} ({pop:0.000} m)");
                    } else {
                        CheckStep(path, previous, next, travel, slot);
                        CheckSubSteps(path, prevailing, frame, travel, slot);
                    }
                    previous = next;
                }
                Assert.True(wraps >= 1, $"slot {slot} never looped");
            }
            Assert.True(fastest > Settings.MaxGroundSpeedShare - 0.15, $"the fastest of 60 rolls only reaches share {fastest:0.000}");
            Assert.True(stalls >= 1, "no roll of 60 stalls, so the stall was never looped through");
        }

        [Fact]
        public void TheContactPointDoesNotSlip() {
            var flat = new RollerPathSettings { WobbleAmplitude = 0.0, MinBumpChance = 0.0, MaxBumpChance = 0.0, StallChance = 0.0 };
            for (int slot = 0; slot < 20; slot++) {
                ulong seed = StableHash.Mix(17UL, (ulong)slot);
                double size = slot % 2 == 0 ? flat.MaxSize : RollerPath.SizeOf(seed, flat);
                RollerPath path = RollerPath.Build(seed, 0.0, -30.0, 0.45 * size, size, Prevailing(slot), flat, (x, z) => true, (x, z) => 0.0);
                Assert.Equal(0.45 * size, path.Radius, 12);
                const double step = 0.01;
                RollerPose before = RollerPose.At(path, 10.0);
                RollerPose after = RollerPose.At(path, 10.0 + step);
                double[] bottom = InverseRotate(before, 0.0, -path.Radius, 0.0);
                double[] top = InverseRotate(before, 0.0, path.Radius, 0.0);
                double bottomMoved = Moved(before, after, bottom);
                double[] topAfter = Rotate(after, top[0], top[1], top[2]);
                double topForward = (after.X + topAfter[0] - before.X) * path.DirectionX + (after.Z + topAfter[2] - before.Z) * path.DirectionZ;
                Assert.True(bottomMoved < 0.1 * step, $"slot {slot}: contact point slid {bottomMoved:0.00000} m over {step} m");
                Assert.InRange(topForward, 1.9 * step, 2.1 * step);
            }
        }

        [Fact]
        public void CycleReducesHugeDriftInDouble() {
            RollerPathScene scene = RollerPathScene.Open(0.4);
            RollerPath path = scene.Build(21UL, new WindVector(1.0, 0.0));
            double along = 4.0e9;
            double cycle = path.CycleOf(along);
            double later = path.CycleOf(along + 0.1);
            Assert.InRange(cycle, 0.0, path.Cycle);
            double step = later - cycle;
            if (step < 0.0) step += path.Cycle;
            double expected = 0.1 * path.GroundSpeedShare;
            Assert.InRange(step, expected - 1e-6, expected + 1e-6);
        }

        [Fact]
        public void BlockedOrHemmedSlotsGetNoRoll() {
            var prevailing = new WindVector(1.0, 0.0);
            Assert.Null(RollerPath.Build(1UL, 0.0, 0.0, 0.4, prevailing, Settings, (x, z) => false, (x, z) => 0.0));
            Assert.Null(RollerPath.Build(1UL, 0.0, 0.0, 0.4, prevailing, Settings, (x, z) => x * x + z * z < 9.0, (x, z) => 0.0));
        }

        [Fact]
        public void TheLineRunsThroughTheSlot() {
            for (int slot = 0; slot < 50; slot++) {
                RollerPathScene scene = RollerPathScene.Open(0.4);
                RollerPath path = scene.Build(StableHash.Mix(23UL, (ulong)slot), Prevailing(slot));
                Assert.Equal(0.0, path.Wobble(0.0));
                path.GroundPoint(-path.StartOffset, out double x, out double z);
                Assert.Equal(path.SlotX, x, 9);
                Assert.Equal(path.SlotZ, z, 9);
            }
        }

        [Fact]
        public void EveryPoseStaysInsideTheBounds() {
            for (int slot = 0; slot < 100; slot++) {
                RollerPathScene scene = RollerPathScene.Random(3000UL + (ulong)slot);
                RollerPath path = scene.Build(StableHash.Mix(29UL, (ulong)slot), Prevailing(slot));
                if (path == null) continue;

                path.Bounds(0.0, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ);
                for (double cycle = 0.0; cycle < path.Cycle; cycle += 0.1) {
                    RollerPose pose = RollerPose.At(path, cycle);
                    Assert.True(pose.X >= minX - 1e-9 && pose.X <= maxX + 1e-9 && pose.Z >= minZ - 1e-9 && pose.Z <= maxZ + 1e-9, $"slot {slot}: ({pose.X:0.00}, {pose.Z:0.00}) outside x {minX:0.00}..{maxX:0.00} z {minZ:0.00}..{maxZ:0.00}");
                    Assert.InRange(pose.Y, minY - 1e-9, maxY + 1e-9);
                }
            }
        }

        [Fact]
        public void JitterAndWanderStayUnderTheOffWindLimit() {
            Assert.Equal(10.0, RollerPathSettings.JitterWithin(10.0, 35.0));
            Assert.Equal(RollerPathSettings.MaxOffWindDegrees - 75.0, RollerPathSettings.JitterWithin(10.0, 75.0));
            Assert.Equal(0.0, RollerPathSettings.JitterWithin(10.0, 85.0));
            Assert.Equal(0.0, RollerPathSettings.JitterWithin(-4.0, 10.0));
            for (double wander = 0.0; wander <= 90.0; wander += 5.0) {
                Assert.True(RollerPathSettings.JitterWithin(80.0, wander) + wander <= Math.Max(wander, RollerPathSettings.MaxOffWindDegrees) + 1e-9);
            }
        }

        [Fact]
        public void ScaleDistanceFitsTheRestAndTheShortestRoll() {
            var settings = new RollerPathSettings { MinRestGap = 6.0, MaxRestGap = 6.0, MinClippedLength = 8.0, ScaleDistance = 3.0 };
            settings.Validate();
            Assert.Equal(3.0, settings.MaxScaleDistance);
            settings.ScaleDistance = 3.01;
            Assert.Throws<ArgumentException>(() => settings.Validate());
            settings = new RollerPathSettings { MinClippedLength = 2.0, ScaleDistance = 2.5 };
            Assert.Equal(2.0, settings.MaxScaleDistance);
            Assert.Throws<ArgumentException>(() => settings.Validate());
        }

        [Fact]
        public void SizeSpeedAndShapeVaryWithinBounds() {
            var prevailing = new WindVector(0.6, 0.8);
            double smallShare = 0.0, bigShare = 0.0, minWobble = double.MaxValue, maxWobble = 0.0;
            int small = 0, big = 0, belowMiddle = 0, stalls = 0, built = 0;
            for (int slot = 0; slot < 400; slot++) {
                ulong seed = StableHash.Mix(31UL, (ulong)slot);
                double size = RollerPath.SizeOf(seed, Settings);
                Assert.InRange(size, Settings.MinSize, Settings.MaxSize);
                Assert.Equal(size, RollerPath.SizeOf(seed, Settings));
                belowMiddle += size < (Settings.MinSize + Settings.MaxSize) * 0.5 ? 1 : 0;
                RollerPath path = RollerPathScene.Open(0.45).Build(seed, prevailing);
                built++;
                Assert.Equal(size, path.Size);
                Assert.Equal(0.45 * size, path.Radius, 12);
                Assert.InRange(path.GroundSpeedShare, Settings.MinGroundSpeedShare, Settings.MaxGroundSpeedShare);
                Assert.InRange(path.WobbleAmplitude, Settings.WobbleAmplitude * Settings.MinWobbleShare - 1e-9, Settings.WobbleAmplitude + 1e-9);
                minWobble = Math.Min(minWobble, path.WobbleAmplitude);
                maxWobble = Math.Max(maxWobble, path.WobbleAmplitude);
                double[] squash = { path.SquashX, path.SquashY, path.SquashZ };
                foreach (double axis in squash) Assert.InRange(axis, 1.0 - Settings.MaxSquash - 1e-9, 1.0 + 1e-12);
                Assert.Equal(1.0, Math.Max(squash[0], Math.Max(squash[1], squash[2])), 12);
                Assert.InRange(Math.Abs(path.Tilt), 0.0, Settings.MaxTiltDegrees * Math.PI / 180.0 + 1e-12);
                if (path.HasStall) {
                    stalls++;
                    Assert.True(path.Heft >= Settings.StallHeft, $"slot {slot}: a light roll (heft {path.Heft:0.00}) stalls");
                    Assert.InRange(path.StallGap, Settings.MinStall, Settings.MaxStall);
                    Assert.InRange(path.StallAt, path.Length * 0.25 - 1e-9, path.Length);
                    Assert.False(path.TryGetJump(path.StallAt, out _), $"slot {slot}: stalls mid-jump");
                }
                if (path.Heft < 0.25) {
                    small++;
                    smallShare += path.GroundSpeedShare;
                } else if (path.Heft > 0.75) {
                    big++;
                    bigShare += path.GroundSpeedShare;
                }
            }
            Assert.True(belowMiddle > built * 0.6, $"only {belowMiddle} of {built} sizes are below the middle of the range (small ones should be common)");
            Assert.True(small > 0 && big > 0 && smallShare / small > bigShare / big + 0.15, $"small rolls ({small}) are not clearly faster than big ones ({big})");
            Assert.True(stalls > 0, "no heavy roll stalls");
            Assert.True(maxWobble - minWobble > Settings.WobbleAmplitude * 0.4, $"wobble only varies {minWobble:0.00}..{maxWobble:0.00} m");
        }

        [Fact]
        public void AStallHoldsTheRollThenCarriesOn() {
            RollerPath path = null;
            for (ulong slot = 0; path == null || !path.HasStall; slot++) path = RollerPathScene.Open(0.45).Build(StableHash.Mix(37UL, slot), new WindVector(1.0, 0.0));
            RollerPose before = RollerPose.At(path, path.StallAt - 1e-6);
            RollerPose during = RollerPose.At(path, path.StallAt + path.StallGap * 0.5);
            RollerPose after = RollerPose.At(path, path.StallAt + path.StallGap + 1.0);
            Assert.True(during.IsStalled && !during.IsResting);
            Assert.Equal(path.StallAt, during.Distance, 9);
            Assert.Equal(before.X, during.X, 4);
            Assert.Equal(before.Z, during.Z, 4);
            Assert.Equal(path.StallAt + 1.0, after.Distance, 9);
            Assert.True(RollerPose.At(path, path.Length + path.StallGap + 0.5).IsResting);
        }

        [Fact]
        public void PrevailingForMatchesTheField() {
            for (ulong seed = 1; seed < 20; seed++) {
                Assert.Equal(new WindField(seed, WindSettings.Default).Prevailing, WindField.PrevailingFor(seed));
            }
        }

        private static void CheckPoses(RollerPath path, RollerPathScene scene) {
            for (double cycle = 0.0; cycle < path.Cycle; cycle += 0.1) {
                RollerPose pose = RollerPose.At(path, cycle);
                double contact = path.ContactHeight(pose.Distance, pose.RollAngle, pose.Scale);
                if (pose.IsAirborne) Assert.True(pose.Y >= contact - 0.01, $"a jump dips {contact - pose.Y:0.000} m under the contact at {pose.Distance:0.00} m");
                else Assert.Equal(contact, pose.Y, 9);
                double side = path.Wobble(path.StartOffset + pose.Distance);
                Assert.InRange(Math.Abs(side), 0.0, 2.0 * Settings.WobbleAmplitude + 1e-9);
                Assert.InRange(pose.Scale, 0.0, 1.0);
                double norm = pose.RotationX * pose.RotationX + pose.RotationY * pose.RotationY + pose.RotationZ * pose.RotationZ + pose.RotationW * pose.RotationW;
                Assert.InRange(norm, 1.0 - 1e-9, 1.0 + 1e-9);
                if (pose.IsResting || pose.IsStalled) Assert.False(pose.IsAirborne);
            }
        }

        // Jumps run in order inside the roll, never overlap, keep a little contact between them, each leaves and lands
        // on its contact height, and a bounce follows the landing of the leap before it.
        private static void CheckJumps(RollerPath path, string label) {
            double previousLanding = double.NegativeInfinity;
            foreach (RollerJump jump in path.Jumps) {
                Assert.True(jump.Takeoff >= 0.0 && jump.Landing <= path.Length + 1e-9, $"{label}: jump {jump.Takeoff:0.00}..{jump.Landing:0.00} outside 0..{path.Length:0.00}");
                Assert.True(jump.Length > 0.0 && jump.Height >= 0.0, $"{label}: empty jump");
                if (jump.Kind == RollerJumpKind.Bounce) Assert.Equal(previousLanding, jump.Takeoff, 9);
                else Assert.True(jump.Takeoff >= previousLanding + 0.1, $"{label}: jump at {jump.Takeoff:0.00} leaves {jump.Takeoff - previousLanding:0.000} m of contact");
                Assert.Equal(path.ContactHeight(jump.Takeoff, path.AngleAt(jump.Takeoff)), jump.TakeoffY, 9);
                Assert.Equal(path.ContactHeight(jump.Landing, path.AngleAt(jump.Landing)), jump.LandingY, 9);
                Assert.Equal(jump.LandingY, jump.HeightAt(jump.Landing), 9);
                previousLanding = jump.Landing;
            }
        }

        // Twenty sub-steps inside a frame: no sub-step moves the centre more than a steep jump's climb allows, so a
        // frame's move is never a teleport hidden inside the frame's travel.
        private static void CheckSubSteps(RollerPath path, WindVector prevailing, int frame, double travel, int slot) {
            const int steps = 20;
            RollerPose previous = RollerPose.Of(path, WindAt(prevailing, (frame - 1) * Frame));
            for (int step = 1; step <= steps; step++) {
                RollerPose next = RollerPose.Of(path, WindAt(prevailing, (frame - 1 + step / (double)steps) * Frame));
                if (next.Distance < previous.Distance - 1.0) return;

                double dy = Math.Abs(next.Y - previous.Y);
                Assert.True(dy <= travel / steps * SteepestClimb + JumpLimit * 0.2, $"slot {slot}: centre jumped {dy:0.000} m up or down in 1/{steps} of a frame");
                previous = next;
            }
        }

        // Between two frames the centre moves the roll's travel plus at most the jump limit across (and no more than a
        // steep jump climbs up or down), the visible size and the roll angle change smoothly.
        private static void CheckStep(RollerPath path, RollerPose previous, RollerPose next, double travel, int slot) {
            double dx = next.X - previous.X;
            double dz = next.Z - previous.Z;
            double moved = Math.Sqrt(dx * dx + dz * dz);
            Assert.True(moved <= travel + JumpLimit, $"slot {slot}: centre jumped {moved:0.000} m across in a frame (travel {travel:0.000})");
            Assert.True(Math.Abs(next.Y - previous.Y) <= travel * SteepestClimb + JumpLimit, $"slot {slot}: centre jumped {next.Y - previous.Y:0.000} m up or down in a frame");
            Assert.True(Math.Abs(next.Scale - previous.Scale) * path.Radius <= JumpLimit, $"slot {slot}: size jumped");
            double turned = Math.Abs(next.RollAngle - previous.RollAngle);
            double rockLimit = path.RockAngle * 2.0 * Math.PI * travel / path.RockPeriod;
            Assert.True(turned <= travel / path.RollRadius + rockLimit + 1e-9, $"slot {slot}: roll angle jumped {turned:0.000} rad");
        }

        private static WindVector WindAt(WindVector direction, double seconds) {
            return direction * (StressWind * seconds + 12345.0);
        }

        private static WindVector Prevailing(int slot) {
            double angle = slot * 0.7;
            return new WindVector(Math.Sin(angle), Math.Cos(angle));
        }

        private static double Moved(RollerPose before, RollerPose after, double[] body) {
            double[] start = Rotate(before, body[0], body[1], body[2]);
            double[] end = Rotate(after, body[0], body[1], body[2]);
            double dx = after.X + end[0] - before.X - start[0];
            double dz = after.Z + end[2] - before.Z - start[2];
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static double[] InverseRotate(RollerPose pose, double x, double y, double z) {
            return Rotate(-pose.RotationX, -pose.RotationY, -pose.RotationZ, pose.RotationW, x, y, z);
        }

        private static double[] Rotate(RollerPose pose, double x, double y, double z) {
            return Rotate(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW, x, y, z);
        }

        // v' = v + 2w(q × v) + 2 q × (q × v)
        private static double[] Rotate(double qx, double qy, double qz, double qw, double x, double y, double z) {
            double tx = 2.0 * (qy * z - qz * y);
            double ty = 2.0 * (qz * x - qx * z);
            double tz = 2.0 * (qx * y - qy * x);
            return new[] {
                x + qw * tx + (qy * tz - qz * ty),
                y + qw * ty + (qz * tx - qx * tz),
                z + qw * tz + (qx * ty - qy * tx),
            };
        }

        private static string Describe(RollerPath path) {
            if (path == null) return "none";

            var text = new StringBuilder();
            text.Append(BitConverter.DoubleToInt64Bits(path.Length)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.Phase)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.Size)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.GroundSpeedShare)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.StallAt)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.Tilt)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.SquashX)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.RollRadius)).Append(' ');
            foreach (RollerJump jump in path.Jumps) text.Append(jump.Kind).Append(BitConverter.DoubleToInt64Bits(jump.Takeoff)).Append(',').Append(BitConverter.DoubleToInt64Bits(jump.Height)).Append(' ');
            for (double cycle = 0.0; cycle < path.Cycle; cycle += 3.7) {
                RollerPose pose = RollerPose.At(path, cycle);
                text.Append(BitConverter.DoubleToInt64Bits(pose.X)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.Y)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(pose.Z)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.RotationW)).Append(' ');
            }
            return text.ToString();
        }
    }
}
