using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Where a wind-rolled prop is and how it is turned at one point of its <see cref="RollerPath"/> cycle: the centre
    /// in contact (its outline touching the support at its roll angle) or on a jump's arc, rolled by distance / roll
    /// radius about the axis right of its heading (which follows the wobble) after its seeded tilt and spin, turning on
    /// through the air, teetering through a stall, and scaled in at the start and out at the end of its rest.
    /// </summary>
    /// <remarks>
    /// The rotation is a unit quaternion (x, y, z, w) in the plane's own axes (y up, right-hand rule on the components),
    /// so a top point of the ball moves down the path as it rolls; copy the components as they are.
    /// </remarks>
    public readonly struct RollerPose {
        private RollerPose(double x, double y, double z, double rotationX, double rotationY, double rotationZ, double rotationW,
                double scale, double distance, double rollAngle, bool isResting, bool isStalled, bool isAirborne) {
            X = x;
            Y = y;
            Z = z;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
            Scale = scale;
            Distance = distance;
            RollAngle = rollAngle;
            IsResting = isResting;
            IsStalled = isStalled;
            IsAirborne = isAirborne;
        }

        /// <summary>Centre of the prop in the path's plane, y over the path's ground datum.</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
        public double RotationW { get; }

        /// <summary>Visible share of the prop's size: 0 while it reappears at the start or vanishes after its rest.</summary>
        public double Scale { get; }

        /// <summary>Rolled metres from the start, at most the path's length.</summary>
        public double Distance { get; }

        /// <summary>Roll about the axis, radians: distance / radius, plus the rocking while resting.</summary>
        public double RollAngle { get; }

        /// <summary>True while settled at the end of the roll.</summary>
        public bool IsResting { get; }

        /// <summary>True while sitting out the stall mid-roll.</summary>
        public bool IsStalled { get; }

        /// <summary>True while on a jump's arc (off the ground).</summary>
        public bool IsAirborne { get; }

        /// <summary>The pose of <paramref name="path"/> after the wind drifted <paramref name="displacement"/> metres since the session began.</summary>
        public static RollerPose Of(RollerPath path, WindVector displacement) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return At(path, path.CycleOf(path.AlongOf(displacement)));
        }

        /// <summary>The pose of <paramref name="path"/> at cycle position <paramref name="cycle"/> metres (see <see cref="RollerPath.CycleOf"/>).</summary>
        public static RollerPose At(RollerPath path, double cycle) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            double length = path.Length;
            double rolled = path.RolledAt(cycle);
            bool isResting = rolled > length;
            bool isStalled = path.HasStall && cycle > path.StallAt && cycle < path.StallAt + path.StallGap;
            double distance = Math.Max(0.0, Math.Min(length, rolled));
            double lineOffset = path.StartOffset + distance;
            path.GroundPoint(distance, out double x, out double z);
            double angle = path.AngleAt(distance);
            if (isResting) angle += path.RockAngle * Math.Sin(2.0 * Math.PI * (rolled - length) / path.RockPeriod);
            if (isStalled) angle += StallRock(path) * Math.Sin(Math.PI * (cycle - path.StallAt) / path.StallGap);
            double scale = Math.Min(SmoothStep(cycle / path.ScaleDistance), SmoothStep((path.Cycle - cycle) / path.ScaleDistance));
            bool isAirborne = path.TryGetJump(distance, out RollerJump jump);
            double y = isAirborne ? jump.HeightAt(distance) : path.ContactHeight(distance, angle, scale);
            Rotation(path, path.WobbleSlope(lineOffset), angle, out double rotationX, out double rotationY, out double rotationZ, out double rotationW);
            return new RollerPose(x, y, z, rotationX, rotationY, rotationZ, rotationW, scale, distance, angle, isResting, isStalled, isAirborne);
        }

        /// <summary>Unit heading of the roll at <paramref name="path"/>'s line offset with wobble slope <paramref name="slope"/>.</summary>
        public static void Heading(RollerPath path, double slope, out double x, out double z) {
            double headingX = path.DirectionX + path.DirectionZ * slope;
            double headingZ = path.DirectionZ - path.DirectionX * slope;
            double length = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            x = headingX / length;
            z = headingZ / length;
        }

        // One teeter forward and back over a stall, no faster than the rest's rocking.
        private static double StallRock(RollerPath path) {
            return path.RockAngle * Math.Min(1.0, 2.0 * path.StallGap / path.RockPeriod);
        }

        // Yaw to the heading, roll about the mesh's x by `angle`, after the seeded spin about up and tilt about x:
        // yaw ⊗ roll ⊗ spin ⊗ tilt. The roll axis is up × heading, and the outline below the centre depends only on the
        // roll angle (the heading's yaw turns it about up), so RollerProfile can tabulate it once per prop.
        private static void Rotation(RollerPath path, double slope, double angle, out double x, out double y, out double z, out double w) {
            Heading(path, slope, out double headingX, out double headingZ);
            double yaw = Math.Atan2(headingX, headingZ);
            double yawY = Math.Sin(yaw * 0.5);
            double yawW = Math.Cos(yaw * 0.5);
            double rollX = Math.Sin(angle * 0.5);
            double rollW = Math.Cos(angle * 0.5);
            double spinY = Math.Sin(path.Spin * 0.5);
            double spinW = Math.Cos(path.Spin * 0.5);
            double tiltX = Math.Sin(path.Tilt * 0.5);
            double tiltW = Math.Cos(path.Tilt * 0.5);

            // spin ⊗ tilt with spin = (0, spinY, 0, spinW) and tilt = (tiltX, 0, 0, tiltW).
            double turnX = spinW * tiltX;
            double turnY = spinY * tiltW;
            double turnZ = -spinY * tiltX;
            double turnW = spinW * tiltW;
            Multiply(rollX, 0.0, 0.0, rollW, turnX, turnY, turnZ, turnW, out double rolledX, out double rolledY, out double rolledZ, out double rolledW);
            Multiply(0.0, yawY, 0.0, yawW, rolledX, rolledY, rolledZ, rolledW, out x, out y, out z, out w);
        }

        // Hamilton product a ⊗ b.
        private static void Multiply(double ax, double ay, double az, double aw, double bx, double by, double bz, double bw,
                out double x, out double y, out double z, out double w) {
            x = aw * bx + ax * bw + ay * bz - az * by;
            y = aw * by - ax * bz + ay * bw + az * bx;
            z = aw * bz + ax * by - ay * bx + az * bw;
            w = aw * bw - ax * bx - ay * by - az * bz;
        }

        private static double SmoothStep(double share) {
            double clamped = Math.Max(0.0, Math.Min(1.0, share));
            return clamped * clamped * (3.0 - 2.0 * clamped);
        }
    }
}
