using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Where a wind-rolled prop is and how it is turned at one rolled distance of its <see cref="RollerPath"/>: the centre
    /// in contact (its outline touching the support at its roll angle) or on a jump's arc, rolled by distance / roll
    /// radius about the axis right of its heading (which follows the walk) after its seeded tilt and spin, turning on
    /// through the air, and scaled in after its spawn and out before its end. It never rests: the rolled distance only
    /// grows with the wind's drift, and off the roll the prop is not drawn at all.
    /// </summary>
    /// <remarks>
    /// The rotation is a unit quaternion (x, y, z, w) in the plane's own axes (y up, right-hand rule on the components),
    /// so a top point of the ball moves down the path as it rolls; copy the components as they are.
    /// </remarks>
    public readonly struct RollerPose {
        private RollerPose(double x, double y, double z, double rotationX, double rotationY, double rotationZ, double rotationW,
                double scale, double distance, double rollAngle, bool isAirborne, bool isOut) {
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
            IsAirborne = isAirborne;
            IsOut = isOut;
        }

        /// <summary>Centre of the prop in the path's plane.</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
        public double RotationW { get; }

        /// <summary>Visible share of the prop's size: 0 before its spawn and after its end, easing at both.</summary>
        public double Scale { get; }

        /// <summary>Rolled metres from the start, clamped to the roll.</summary>
        public double Distance { get; }

        /// <summary>Roll about the axis, radians: distance / radius.</summary>
        public double RollAngle { get; }

        /// <summary>True while on a jump's arc (off the ground).</summary>
        public bool IsAirborne { get; }

        /// <summary>True between the spawn and the end of the roll (it may still be scaled to nothing at either end).</summary>
        public bool IsOut { get; }

        /// <summary>The pose of <paramref name="path"/> spawned at drift <paramref name="spawnDrift"/> once the wind's drift reaches <paramref name="drift"/>.</summary>
        public static RollerPose Of(RollerPath path, double spawnDrift, double drift) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return At(path, (drift - spawnDrift) * path.GroundSpeedShare);
        }

        /// <summary>The pose of <paramref name="path"/> after rolling <paramref name="rolled"/> metres (negative: not out yet).</summary>
        public static RollerPose At(RollerPath path, double rolled) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            bool isOut = rolled >= 0.0 && rolled <= path.Length;
            double distance = Math.Max(0.0, Math.Min(path.Length, rolled));
            path.GroundPoint(distance, out double x, out double z);
            double angle = path.AngleAt(distance);
            double scale = isOut ? path.FadeAt(distance) : 0.0;
            bool isAirborne = path.TryGetJump(distance, out RollerJump jump);
            double y = isAirborne ? jump.HeightAt(distance) : path.ContactHeight(distance, angle, scale);
            path.HeadingAt(distance, out double headingX, out double headingZ);
            Rotation(path, headingX, headingZ, angle, out double rotationX, out double rotationY, out double rotationZ, out double rotationW);
            return new RollerPose(x, y, z, rotationX, rotationY, rotationZ, rotationW, scale, distance, angle, isAirborne, isOut);
        }

        // Yaw to the heading, roll about the mesh's x by `angle`, after the seeded spin about up and tilt about x:
        // yaw ⊗ roll ⊗ spin ⊗ tilt. The roll axis is up × heading, and the outline below the centre depends only on the
        // roll angle (the heading's yaw turns it about up), so RollerProfile can tabulate it once per prop.
        private static void Rotation(RollerPath path, double headingX, double headingZ, double angle, out double x, out double y, out double z, out double w) {
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
    }
}
