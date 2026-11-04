using System;
using System.Collections.Generic;
using System.Text;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    public sealed class RollerPathTests {
        private const double HalfCell = 64.0;
        private const double Frame = 1.0 / 60.0;
        private const double StressWind = 20.0;
        private const double JumpLimit = 0.1;
        private const double ClearSlack = 0.02;
        private static readonly RollerPathSettings Settings = RollerPathSettings.Default;

        [Fact]
        public void SameSeedSameRollEverywhere() {
            var prevailing = new WindVector(0.6, 0.8);
            int differing = 0;
            for (int slot = 0; slot < 40; slot++) {
                Scene scene = Scene.Random(1000UL + (ulong)slot);
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
                Scene scene = Scene.Random(2000UL + (ulong)slot);
                RollerPath path = scene.Build(StableHash.Mix(9UL, (ulong)slot), Prevailing(slot));
                if (path == null) continue;

                built++;
                for (double distance = 0.0; distance <= path.Length; distance += 0.05) {
                    path.GroundPoint(distance, out double x, out double z);
                    double penetration = scene.Penetration(x, z);
                    Assert.True(penetration <= ClearSlack, $"slot {slot} at {distance:0.00} m: {penetration:0.000} m into a footprint or past the cell");
                }
            }
            Assert.True(built > 200, $"only {built} of 400 slots got a roll");
        }

        [Fact]
        public void LengthsHopsAndWobbleStayInBounds() {
            for (int slot = 0; slot < 300; slot++) {
                Scene scene = Scene.Open(0.3 + slot % 4 * 0.1);
                RollerPath path = scene.Build(StableHash.Mix(11UL, (ulong)slot), Prevailing(slot), (slot % 7 - 3) * 0.05);
                Assert.NotNull(path);
                Assert.InRange(path.Length, Settings.MinClippedLength, Settings.MaxLength + 1e-9);
                Assert.InRange(path.HopHeight, Settings.MinHopHeight, Settings.MaxHopHeight);
                Assert.InRange(path.RestGap, Settings.MinRestGap, Settings.MaxRestGap);
                Assert.InRange(path.StartOffset, -Settings.UpwindReach - 1e-9, 0.0);
                Assert.True(path.HopSpacing <= Settings.MaxHopSpacing * 1.5 + 1e-9, $"hop spacing {path.HopSpacing}");
                Assert.True(path.HopCount == 1 || path.HopSpacing >= Settings.MinHopSpacing * 0.75 - 1e-9, $"hop spacing {path.HopSpacing}");
                CheckPoses(path, scene);
            }
        }

        [Fact]
        public void LoopsWithoutVisibleJumpsAtTwentyMetresPerSecond() {
            for (int slot = 0; slot < 60; slot++) {
                Scene scene = Scene.Open(0.45);
                WindVector prevailing = Prevailing(slot);
                RollerPath path = scene.Build(StableHash.Mix(13UL, (ulong)slot), prevailing);
                double travel = StressWind * Settings.GroundSpeedShare * Frame;
                int frames = (int)Math.Ceiling(path.Cycle * 1.2 / travel);
                int wraps = 0;
                RollerPose previous = RollerPose.Of(path, WindAt(prevailing, 0.0));
                for (int frame = 1; frame <= frames; frame++) {
                    RollerPose next = RollerPose.Of(path, WindAt(prevailing, frame * Frame));
                    if (next.Distance < previous.Distance - 1.0) {
                        wraps++;
                        Assert.True(previous.Scale < 0.05 && next.Scale < 0.05, $"slot {slot}: wrapped at visible scale {previous.Scale:0.000} -> {next.Scale:0.000}");
                    } else {
                        CheckStep(path, previous, next, travel, slot);
                    }
                    previous = next;
                }
                Assert.True(wraps >= 1, $"slot {slot} never looped");
            }
        }

        [Fact]
        public void TheContactPointDoesNotSlip() {
            var flat = new RollerPathSettings { WobbleAmplitude = 0.0, MinHopHeight = 0.0, MaxHopHeight = 0.0 };
            for (int slot = 0; slot < 20; slot++) {
                RollerPath path = RollerPath.Build(StableHash.Mix(17UL, (ulong)slot), 0.0, -30.0, 0.45, Prevailing(slot), flat, (x, z) => true, (x, z) => 0.0);
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
            Scene scene = Scene.Open(0.4);
            RollerPath path = scene.Build(21UL, new WindVector(1.0, 0.0));
            double along = 4.0e9;
            double cycle = path.CycleOf(along);
            double later = path.CycleOf(along + 0.1);
            Assert.InRange(cycle, 0.0, path.Cycle);
            double step = later - cycle;
            if (step < 0.0) step += path.Cycle;
            Assert.InRange(step, 0.06 - 1e-6, 0.06 + 1e-6);
        }

        [Fact]
        public void BlockedOrHemmedSlotsGetNoRoll() {
            var prevailing = new WindVector(1.0, 0.0);
            Assert.Null(RollerPath.Build(1UL, 0.0, 0.0, 0.4, prevailing, Settings, (x, z) => false, (x, z) => 0.0));
            Assert.Null(RollerPath.Build(1UL, 0.0, 0.0, 0.4, prevailing, Settings, (x, z) => x * x + z * z < 9.0, (x, z) => 0.0));
        }

        [Fact]
        public void PrevailingForMatchesTheField() {
            for (ulong seed = 1; seed < 20; seed++) {
                Assert.Equal(new WindField(seed, WindSettings.Default).Prevailing, WindField.PrevailingFor(seed));
            }
        }

        private static void CheckPoses(RollerPath path, Scene scene) {
            for (double cycle = 0.0; cycle < path.Cycle; cycle += 0.1) {
                RollerPose pose = RollerPose.At(path, cycle);
                double lift = pose.Y - path.GroundHeight(pose.Distance) - path.Radius;
                Assert.InRange(lift, -1e-9, Settings.MaxHopHeight + 1e-9);
                double side = path.Wobble(path.StartOffset + pose.Distance);
                Assert.InRange(Math.Abs(side), 0.0, Settings.WobbleAmplitude + 1e-9);
                Assert.InRange(pose.Scale, 0.0, 1.0);
                double norm = pose.RotationX * pose.RotationX + pose.RotationY * pose.RotationY + pose.RotationZ * pose.RotationZ + pose.RotationW * pose.RotationW;
                Assert.InRange(norm, 1.0 - 1e-9, 1.0 + 1e-9);
                if (pose.IsResting) Assert.Equal(0.0, lift, 9);
            }
        }

        // Between two frames the centre moves the roll's travel plus at most the jump limit, the visible size and the
        // roll angle change smoothly, and a resting prop does not move.
        private static void CheckStep(RollerPath path, RollerPose previous, RollerPose next, double travel, int slot) {
            double dx = next.X - previous.X;
            double dy = next.Y - previous.Y;
            double dz = next.Z - previous.Z;
            double moved = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Assert.True(moved <= travel + JumpLimit, $"slot {slot}: centre jumped {moved:0.000} m in a frame (travel {travel:0.000})");
            Assert.True(Math.Abs(next.Scale - previous.Scale) * path.Radius <= JumpLimit, $"slot {slot}: size jumped");
            double turned = Math.Abs(next.RollAngle - previous.RollAngle);
            double rockLimit = path.RockAngle * 2.0 * Math.PI * travel / path.RockPeriod;
            Assert.True(turned <= travel / path.Radius + rockLimit + 1e-9, $"slot {slot}: roll angle jumped {turned:0.000} rad");
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
            text.Append(BitConverter.DoubleToInt64Bits(path.Length)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.Phase)).Append(' ');
            for (double cycle = 0.0; cycle < path.Cycle; cycle += 3.7) {
                RollerPose pose = RollerPose.At(path, cycle);
                text.Append(BitConverter.DoubleToInt64Bits(pose.X)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.Y)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(pose.Z)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.RotationW)).Append(' ');
            }
            return text.ToString();
        }

        // A 128 m cell with blocking discs and a gently rolling ground.
        private sealed class Scene {
            private readonly List<double[]> _discs = new List<double[]>();
            private readonly double _radius;

            private Scene(double radius) {
                _radius = radius;
            }

            public static Scene Open(double radius) {
                return new Scene(radius);
            }

            public static Scene Random(ulong seed) {
                var random = new SplitMix64(seed);
                var scene = new Scene(0.3 + random.NextDouble() * 0.3);
                int count = random.NextInt(10, 60);
                for (int index = 0; index < count; index++) {
                    scene._discs.Add(new[] { random.NextDouble() * 128.0 - 64.0, random.NextDouble() * 128.0 - 64.0, 0.7 + random.NextDouble() * 3.0 });
                }
                return scene;
            }

            public RollerPath Build(ulong seed, WindVector prevailing, double slotShift = 0.0) {
                var random = new SplitMix64(StableHash.Mix(seed, 77UL));
                double slotX = (random.NextDouble() * 2.0 - 1.0) * 50.0 + slotShift;
                double slotZ = (random.NextDouble() * 2.0 - 1.0) * 50.0;
                if (_discs.Count == 0) {
                    slotX = -prevailing.X * 40.0;
                    slotZ = -prevailing.Z * 40.0;
                }
                return RollerPath.Build(seed, slotX, slotZ, _radius, prevailing, Settings, IsClear, Height);
            }

            public double Penetration(double x, double z) {
                double worst = Math.Max(Math.Abs(x), Math.Abs(z)) - (HalfCell - _radius);
                foreach (double[] disc in _discs) {
                    double dx = x - disc[0];
                    double dz = z - disc[1];
                    worst = Math.Max(worst, disc[2] + _radius - Math.Sqrt(dx * dx + dz * dz));
                }
                return worst;
            }

            private bool IsClear(double x, double z) {
                return Penetration(x, z) <= 0.0;
            }

            private static double Height(double x, double z) {
                return 0.15 * Math.Sin(x / 9.0) * Math.Cos(z / 13.0);
            }
        }
    }
}
