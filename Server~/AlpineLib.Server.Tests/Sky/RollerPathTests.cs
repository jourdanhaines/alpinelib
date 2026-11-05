using System;
using System.Collections.Generic;
using System.Text;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Sky;
using Xunit;

namespace AlpineLib.Server.Tests.Sky {
    // Rolls walk from their spawn along the wind: deterministic, clear of what stops them, centred on the wind within
    // the wander, never parked (moving whenever drawn), faded out within a second when blocked, cut at the loaded
    // world's edge and continuous across cell borders.
    public sealed class RollerPathTests {
        private const double Frame = 1.0 / 60.0;
        private const double StressWind = 20.0;
        private const double JumpLimit = 0.1;
        private const double ClearSlack = 0.02;
        private const double SteepestClimb = 4.0;
        private const double CellSize = 128.0;
        private static readonly RollerPathSettings Settings = RollerPathSettings.Default;

        [Fact]
        public void SameSeedSameRollEverywhere() {
            var prevailing = new WindVector(0.6, 0.8);
            int differing = 0;
            for (int slot = 0; slot < 40; slot++) {
                RollerTestWorld world = RollerTestWorld.Scattered(1000UL + (ulong)slot, 40);
                RollerPath first = Build(world, StableHash.Mix(5UL, (ulong)slot), prevailing, slot);
                RollerPath second = Build(world, StableHash.Mix(5UL, (ulong)slot), prevailing, slot);
                RollerPath other = Build(world, StableHash.Mix(6UL, (ulong)slot), prevailing, slot);
                Assert.Equal(Describe(first), Describe(second));
                differing += Describe(first) != Describe(other) ? 1 : 0;
            }
            Assert.True(differing > 30, $"only {differing} of 40 rolls differ across seeds");
        }

        [Fact]
        public void RollsStayClearOfWhatStopsThem() {
            int built = 0, blocked = 0, steers = 0;
            for (int slot = 0; slot < 300; slot++) {
                RollerTestWorld world = RollerTestWorld.Scattered(2000UL + (ulong)slot, 160);
                RollerPath path = Build(world, StableHash.Mix(9UL, (ulong)slot), Prevailing(slot), slot);
                if (path == null) continue;

                built++;
                blocked += path.End == RollerEnd.Blocked ? 1 : 0;
                steers += path.Steers;
                for (double distance = 0.0; distance <= path.Length; distance += 0.05) {
                    path.GroundPoint(distance, out double x, out double z);
                    double penetration = world.Penetration(x, z, path.Radius);
                    Assert.True(penetration <= ClearSlack, $"slot {slot} at {distance:0.00} m: {penetration:0.000} m into a stop");
                }
                if (path.End != RollerEnd.Blocked) continue;

                path.GroundPoint(path.Length, out double endX, out double endZ);
                path.HeadingAt(path.Length, out double headingX, out double headingZ);
                double ahead = world.Penetration(endX + headingX * Settings.ClipStep * 1.5, endZ + headingZ * Settings.ClipStep * 1.5, path.Radius);
                Assert.True(ahead > -Settings.ClipStep, $"slot {slot}: blocked at {path.Length:0.00} m with nothing just ahead ({ahead:0.000})");
            }
            Assert.True(built > 200 && steers > 200, $"{built} of 300 spawns rolled, {steers} steers, {blocked} blocked");
        }

        [Fact]
        public void LifeJumpsAndFadesStayInBounds() {
            for (int slot = 0; slot < 200; slot++) {
                RollerTestWorld world = RollerTestWorld.Open();
                ulong seed = StableHash.Mix(11UL, (ulong)slot);
                double life = Settings.MinLife + slot % 5 * 25.0;
                RollerPath path = Build(world, seed, Prevailing(slot), slot, life);
                Assert.NotNull(path);
                Assert.Equal(RollerEnd.Life, path.End);
                Assert.InRange(path.Length, life - Settings.ClipStep - 2.0, life + 1e-9);
                Assert.Equal(Math.Min(path.GroundSpeedShare * Settings.FadeDrift, path.Length * 0.5), path.FadeLength, 12);
                Assert.InRange(path.BumpChance, Settings.MinBumpChance, Settings.MaxBumpChance);
                CheckJumps(path, $"slot {slot}");
                CheckPoses(path);
                Assert.Equal(0.0, RollerPose.At(path, -0.01).Scale);
                Assert.Equal(0.0, RollerPose.At(path, path.Length + 0.01).Scale);
                Assert.False(RollerPose.At(path, path.Length + 0.01).IsOut);
            }
        }

        [Fact]
        public void HeadingsCentreOnTheWindWithinTheWander() {
            double sum = 0.0, squares = 0.0, widest = 0.0;
            int samples = 0, changes = 0;
            for (int slot = 0; slot < 300; slot++) {
                WindVector prevailing = Prevailing(slot);
                var walk = new RollerWalk(StableHash.Mix(21UL, (ulong)slot), prevailing, Settings, Settings.MaxLife);
                Assert.InRange(walk.WanderPeriod, Settings.MinWanderPeriod, Settings.MaxWanderPeriod);
                changes += walk.CourseChanges;
                for (double distance = 0.0; distance <= Settings.MaxLife; distance += 1.0) {
                    walk.DirectionAt(distance, out double x, out double z);
                    double offset = Math.Atan2(x * prevailing.Z - z * prevailing.X, x * prevailing.X + z * prevailing.Z) * 180.0 / Math.PI;
                    Assert.True(Math.Abs(offset) <= Settings.WanderDegrees + 1e-6, $"slot {slot} at {distance} m: {offset:0.00} deg off the wind");
                    Assert.Equal(walk.OffsetAt(distance) * 180.0 / Math.PI, offset, 6);
                    sum += offset;
                    squares += offset * offset;
                    widest = Math.Max(widest, Math.Abs(offset));
                    samples++;
                }
            }
            double mean = sum / samples;
            double spread = Math.Sqrt(squares / samples - mean * mean);
            Assert.True(Math.Abs(mean) < 3.0, $"headings centre {mean:0.00} deg off the wind");
            Assert.InRange(spread, 8.0, Settings.WanderDegrees);
            Assert.True(widest > 30.0, $"the wander never reaches past {widest:0.0} deg");
            Assert.True(changes > 150, $"only {changes} course changes over 300 walks");
        }

        [Fact]
        public void NeverParkedWhileDrawn() {
            var wind = new WindField(77UL, WindSettings.Default);
            WindVector prevailing = wind.Prevailing;
            int checkedRolls = 0;
            for (int slot = 0; slot < 16; slot++) {
                RollerTestWorld world = RollerTestWorld.Scattered(3000UL + (ulong)slot, 120);
                RollerPath path = Build(world, StableHash.Mix(23UL, (ulong)slot), prevailing, slot);
                if (path == null) continue;

                checkedRolls++;
                double spawnTime = 30.0 + slot * 37.0;
                double spawnDrift = DriftAt(wind, spawnTime);
                double still = 0.0;
                RollerPose previous = RollerPose.Of(path, spawnDrift, spawnDrift);
                for (double time = spawnTime + Frame; ; time += Frame) {
                    RollerPose next = RollerPose.Of(path, spawnDrift, DriftAt(wind, time));
                    if (!next.IsOut) break;

                    CheckStep(path, previous, next, StressWind * path.GroundSpeedShare * Frame, slot, true);
                    double dx = next.X - previous.X, dz = next.Z - previous.Z;
                    double speed = Math.Sqrt(dx * dx + dz * dz) / Frame;
                    still = next.Scale > 0.0 && speed < 1e-3 ? still + Frame : 0.0;
                    Assert.True(still < Frame * 1.5, $"slot {slot}: parked at {next.Distance:0.00} m while drawn at scale {next.Scale:0.000}");
                    previous = next;
                }
            }
            Assert.True(checkedRolls > 10);
        }

        [Fact]
        public void ABlockedRollFadesOutWithinASecond() {
            var settings = new RollerPathSettings { FadeDrift = RollerSpawner.SlowestDriftSpeed(WindSettings.Default) };
            var wind = new WindField(81UL, WindSettings.Default);
            WindVector prevailing = wind.Prevailing;
            double worst = 0.0;
            int blocked = 0;
            for (int slot = 0; slot < 24; slot++) {
                ulong seed = StableHash.Mix(25UL, (ulong)slot);
                RollerTestWorld world = Wall(prevailing, 20.0 + slot);
                double size = RollerPath.SizeOf(seed, settings);
                RollerPath path = RollerPath.Build(seed, 0.0, 0.0, 0.45 * size, size, settings.MaxLife, prevailing, settings, world, null);
                if (path == null || path.End != RollerEnd.Blocked) continue;

                blocked++;
                double spawnTime = 100.0 + slot * 53.0;
                double spawnDrift = DriftAt(wind, spawnTime);
                double fadeStart = double.NaN, fadeEnd = double.NaN;
                for (double time = spawnTime; time < spawnTime + 400.0; time += Frame) {
                    RollerPose pose = RollerPose.Of(path, spawnDrift, DriftAt(wind, time));
                    if (pose.Distance > path.Length * 0.5 && pose.Scale < 1.0 && double.IsNaN(fadeStart)) fadeStart = time;
                    if (pose.IsOut || pose.Distance < path.Length) continue;

                    fadeEnd = time;
                    break;
                }
                Assert.False(double.IsNaN(fadeEnd), $"slot {slot}: never reached its block");
                worst = Math.Max(worst, fadeEnd - fadeStart);
            }
            Assert.True(blocked > 15, $"only {blocked} rolls blocked");
            Assert.True(worst > 0.0 && worst <= 1.0 + 2.0 * Frame, $"a blocked fade takes {worst:0.000} s");
        }

        [Fact]
        public void TheLoadedEdgeCutsTheRollAndAWiderWorldCarriesItOn() {
            var prevailing = new WindVector(1.0, 0.0);
            for (int slot = 0; slot < 30; slot++) {
                ulong seed = StableHash.Mix(27UL, (ulong)slot);
                var narrow = new RollerTestWorld { EdgeX = 40.0 };
                RollerPath cut = Build(narrow, seed, prevailing, slot, Settings.MaxLife, 0.0, 0.0);
                RollerPath full = Build(RollerTestWorld.Open(), seed, prevailing, slot, Settings.MaxLife, 0.0, 0.0);
                Assert.Equal(RollerEnd.Edge, cut.End);
                Assert.True(full.Length > cut.Length + 10.0);
                for (double distance = 0.0; distance < cut.Length - 1.0; distance += 0.5) {
                    cut.GroundPoint(distance, out double cutX, out double cutZ);
                    full.GroundPoint(distance, out double fullX, out double fullZ);
                    Assert.True(cutX <= 40.0 + 1e-9);
                    Assert.Equal(fullX, cutX, 12);
                    Assert.Equal(fullZ, cutZ, 12);
                }
            }
        }

        [Fact]
        public void RollsCrossCellBordersWithoutAJump() {
            var cells = new Dictionary<(long X, long Z), IRollerCell>();
            for (long cellX = -6; cellX <= 6; cellX++) {
                for (long cellZ = -6; cellZ <= 6; cellZ++) cells[(cellX, cellZ)] = new RollerHeightCell(cellX, cellZ, CellSize);
            }
            var world = new RollerCellWorld(CellSize, cells);
            int borders = 0;
            for (int slot = 0; slot < 30; slot++) {
                ulong seed = StableHash.Mix(29UL, (ulong)slot);
                WindVector prevailing = Prevailing(slot % 4);
                RollerPath crossing = Build(world, seed, prevailing, slot, Settings.MaxLife, 40.0, 30.0);
                RollerPath single = Build(RollerTestWorld.Open(), seed, prevailing, slot, Settings.MaxLife, 40.0, 30.0);
                Assert.Equal(single.Length, crossing.Length);
                double travel = StressWind * crossing.GroundSpeedShare * Frame;
                RollerPose previous = RollerPose.At(crossing, 0.0);
                for (double rolled = travel; rolled < crossing.Length; rolled += travel) {
                    RollerPose next = RollerPose.At(crossing, rolled);
                    RollerPose alone = RollerPose.At(single, rolled);
                    Assert.True(Math.Abs(next.Y - alone.Y) < 1e-6 && Math.Abs(next.X - alone.X) < 1e-9, $"slot {slot}: the cell world rolls {next.Y - alone.Y:0.0000} m off one height field");
                    bool isBorder = CellOf(previous.X) != CellOf(next.X) || CellOf(previous.Z) != CellOf(next.Z);
                    borders += isBorder ? 1 : 0;
                    if (isBorder) CheckStep(crossing, previous, next, travel, slot, false);
                    previous = next;
                }
            }
            Assert.True(borders > 15, $"only {borders} border crossings checked");
        }

        [Fact]
        public void AStopAcrossABorderBlocksBothSides() {
            var cells = new Dictionary<(long X, long Z), IRollerCell>();
            var stopCell = new RollerStopCell(60.0, 0.0, 12.0);
            cells[(0, 0)] = stopCell;
            cells[(1, 0)] = new RollerStopCell(double.NaN, 0.0, 0.0);
            var reaching = new RollerCellWorld(CellSize, cells, 12.0);
            var blind = new RollerCellWorld(CellSize, cells);
            double insideNeighbour = CellSize + 2.0;
            double centreZ = RollerCellWorld.CentreOf(0, CellSize);
            Assert.Equal(RollerBlock.Stop, reaching.BlockAt(insideNeighbour, centreZ, 0.4));
            Assert.Equal(RollerBlock.None, blind.BlockAt(insideNeighbour, centreZ, 0.4));
            Assert.Equal(RollerBlock.None, reaching.BlockAt(insideNeighbour + 10.0, centreZ, 0.4));
            Assert.Equal(RollerBlock.Edge, reaching.BlockAt(-1.0, centreZ, 0.4));
        }

        [Fact]
        public void MovesSmoothlyAtTwentyMetresPerSecond() {
            int checkedRolls = 0;
            for (int slot = 0; slot < 40; slot++) {
                RollerTestWorld world = RollerTestWorld.Scattered(4000UL + (ulong)slot, 80);
                RollerPath path = Build(world, StableHash.Mix(13UL, (ulong)slot), Prevailing(slot), slot);
                if (path == null) continue;

                checkedRolls++;
                double travel = StressWind * path.GroundSpeedShare * Frame;
                RollerPose previous = RollerPose.At(path, 0.0);
                for (double rolled = travel; rolled <= path.Length; rolled += travel) {
                    RollerPose next = RollerPose.At(path, rolled);
                    CheckStep(path, previous, next, travel, slot, false);
                    previous = next;
                }
            }
            Assert.True(checkedRolls > 25);
        }

        [Fact]
        public void TheContactPointDoesNotSlip() {
            var flat = new RollerPathSettings { MinBumpChance = 0.0, MaxBumpChance = 0.0, WanderDegrees = 0.0, CourseDegrees = 0.0 };
            for (int slot = 0; slot < 20; slot++) {
                ulong seed = StableHash.Mix(17UL, (ulong)slot);
                double size = slot % 2 == 0 ? flat.MaxSize : RollerPath.SizeOf(seed, flat);
                var world = new RollerTestWorld { Surface = (x, z) => 0.0 };
                RollerPath path = RollerPath.Build(seed, 0.0, -30.0, 0.45 * size, size, flat.MaxLife, Prevailing(slot), flat, world, null);
                Assert.Equal(0.45 * size, path.Radius, 12);
                const double step = 0.01;
                RollerPose before = RollerPose.At(path, 10.0);
                RollerPose after = RollerPose.At(path, 10.0 + step);
                path.HeadingAt(10.0, out double headingX, out double headingZ);
                double[] bottom = InverseRotate(before, 0.0, -path.Radius, 0.0);
                double[] top = InverseRotate(before, 0.0, path.Radius, 0.0);
                double bottomMoved = Moved(before, after, bottom);
                double[] topAfter = Rotate(after, top[0], top[1], top[2]);
                double topForward = (after.X + topAfter[0] - before.X) * headingX + (after.Z + topAfter[2] - before.Z) * headingZ;
                Assert.True(bottomMoved < 0.1 * step, $"slot {slot}: contact point slid {bottomMoved:0.00000} m over {step} m");
                Assert.InRange(topForward, 1.9 * step, 2.1 * step);
            }
        }

        [Fact]
        public void SizeSpeedAndShapeVaryWithinBounds() {
            double smallest = double.MaxValue, largest = 0.0, slowest = double.MaxValue, fastest = 0.0;
            for (int slot = 0; slot < 200; slot++) {
                ulong seed = StableHash.Mix(19UL, (ulong)slot);
                RollerPath path = Build(RollerTestWorld.Open(), seed, Prevailing(slot), slot, Settings.MinLife);
                Assert.InRange(path.Size, Settings.MinSize, Settings.MaxSize);
                Assert.InRange(path.GroundSpeedShare, Settings.MinGroundSpeedShare, Settings.MaxGroundSpeedShare);
                Assert.Equal(RollerPath.GroundSpeedShareOf(seed, path.Size, Settings), path.GroundSpeedShare);
                Assert.InRange(Math.Min(path.SquashX, Math.Min(path.SquashY, path.SquashZ)), 1.0 - Settings.MaxSquash - 1e-9, 1.0);
                Assert.Equal(1.0, Math.Max(path.SquashX, Math.Max(path.SquashY, path.SquashZ)), 12);
                Assert.InRange(Math.Abs(path.Tilt), 0.0, Settings.MaxTiltDegrees * Math.PI / 180.0 + 1e-9);
                smallest = Math.Min(smallest, path.Size);
                largest = Math.Max(largest, path.Size);
                slowest = Math.Min(slowest, path.GroundSpeedShare);
                fastest = Math.Max(fastest, path.GroundSpeedShare);
            }
            Assert.True(largest - smallest > 0.5 && fastest - slowest > 0.25, $"sizes {smallest:0.00}..{largest:0.00}, shares {slowest:0.00}..{fastest:0.00}");
        }

        [Fact]
        public void PrevailingForMatchesTheField() {
            for (ulong seed = 1; seed < 20; seed++) {
                Assert.Equal(new WindField(seed, WindSettings.Default).Prevailing, WindField.PrevailingFor(seed));
            }
        }

        private static RollerPath Build(IRollerWorld world, ulong seed, WindVector prevailing, int slot, double life = -1.0, double startX = double.NaN, double startZ = double.NaN) {
            double size = RollerPath.SizeOf(seed, Settings);
            if (double.IsNaN(startX)) {
                startX = -prevailing.X * 60.0 + (slot % 9 - 4) * 3.1;
                startZ = -prevailing.Z * 60.0 + (slot % 7 - 3) * 2.3;
            }
            return RollerPath.Build(seed, startX, startZ, 0.45 * size, size, life > 0.0 ? life : Settings.MaxLife, prevailing, Settings, world, null);
        }

        // A wall of stop discs across the wind, `ahead` metres downwind of the origin and far wider than any steer clears.
        private static RollerTestWorld Wall(WindVector prevailing, double ahead) {
            var world = RollerTestWorld.Open();
            for (double across = -400.0; across <= 400.0; across += 6.0) {
                world.AddStop(prevailing.X * ahead + prevailing.Z * across, prevailing.Z * ahead - prevailing.X * across, 4.0);
            }
            return world;
        }

        private static long CellOf(double value) {
            return (long)Math.Floor(value / CellSize);
        }

        private static double DriftAt(WindField wind, double time) {
            WindVector displacement = wind.Displacement(time);
            return displacement.X * wind.Prevailing.X + displacement.Z * wind.Prevailing.Z;
        }

        private static void CheckPoses(RollerPath path) {
            for (double rolled = 0.0; rolled < path.Length; rolled += 0.1) {
                RollerPose pose = RollerPose.At(path, rolled);
                double contact = path.ContactHeight(pose.Distance, pose.RollAngle, pose.Scale);
                if (pose.IsAirborne) Assert.True(pose.Y >= contact - 0.01, $"a jump dips {contact - pose.Y:0.000} m under the contact at {pose.Distance:0.00} m");
                else Assert.Equal(contact, pose.Y, 9);
                Assert.InRange(pose.Scale, 0.0, 1.0);
                Assert.True(pose.IsOut);
                double norm = pose.RotationX * pose.RotationX + pose.RotationY * pose.RotationY + pose.RotationZ * pose.RotationZ + pose.RotationW * pose.RotationW;
                Assert.InRange(norm, 1.0 - 1e-9, 1.0 + 1e-9);
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

        // Between two frames the centre moves the roll's travel plus at most the jump limit across (and no more than a
        // steep jump climbs up or down) and the roll angle changes smoothly; at a real wind so does the visible size (a
        // gale shortens the fades in time like everything else).
        private static void CheckStep(RollerPath path, RollerPose previous, RollerPose next, double travel, int slot, bool withSize) {
            double dx = next.X - previous.X;
            double dz = next.Z - previous.Z;
            double moved = Math.Sqrt(dx * dx + dz * dz);
            Assert.True(moved <= travel + JumpLimit, $"slot {slot}: centre jumped {moved:0.000} m across in a frame (travel {travel:0.000})");
            Assert.True(Math.Abs(next.Y - previous.Y) <= travel * SteepestClimb + JumpLimit, $"slot {slot}: centre jumped {next.Y - previous.Y:0.000} m up or down in a frame");
            if (withSize) Assert.True(Math.Abs(next.Scale - previous.Scale) * path.Radius <= JumpLimit, $"slot {slot}: size jumped {Math.Abs(next.Scale - previous.Scale) * path.Radius:0.000} m");
            double turned = Math.Abs(next.RollAngle - previous.RollAngle);
            Assert.True(turned <= travel / path.RollRadius + 1e-9, $"slot {slot}: roll angle jumped {turned:0.000} rad");
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
            text.Append(path.End).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.Length)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.Size)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.GroundSpeedShare)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.Tilt)).Append(' ').Append(BitConverter.DoubleToInt64Bits(path.SquashX)).Append(' ')
                .Append(BitConverter.DoubleToInt64Bits(path.RollRadius)).Append(' ');
            foreach (RollerJump jump in path.Jumps) text.Append(jump.Kind).Append(BitConverter.DoubleToInt64Bits(jump.Takeoff)).Append(',').Append(BitConverter.DoubleToInt64Bits(jump.Height)).Append(' ');
            for (double rolled = 0.0; rolled < path.Length; rolled += 3.7) {
                RollerPose pose = RollerPose.At(path, rolled);
                text.Append(BitConverter.DoubleToInt64Bits(pose.X)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.Y)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(pose.Z)).Append(',').Append(BitConverter.DoubleToInt64Bits(pose.RotationW)).Append(' ');
            }
            return text.ToString();
        }
    }
}
