using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Where a wind-rolled prop is and how it is turned at one point of its <see cref="RollerPath"/> cycle: the centre
    /// riding the ground profile plus its radius and hop, rolled by distance / radius about the axis right of its
    /// heading (which follows the wobble), and scaled in at the start and out at the end of its rest.
    /// </summary>
    /// <remarks>
    /// The rotation is a unit quaternion (x, y, z, w) in the plane's own axes (y up, right-hand rule on the components),
    /// so a top point of the ball moves down the path as it rolls; copy the components as they are.
    /// </remarks>
    public readonly struct RollerPose {
        private RollerPose(double x, double y, double z, double rotationX, double rotationY, double rotationZ, double rotationW,
                double scale, double distance, double rollAngle, bool isResting) {
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

        /// <summary>The pose of <paramref name="path"/> after the wind drifted <paramref name="displacement"/> metres since the session began.</summary>
        public static RollerPose Of(RollerPath path, WindVector displacement) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return At(path, path.CycleOf(path.AlongOf(displacement)));
        }

        /// <summary>The pose of <paramref name="path"/> at cycle position <paramref name="cycle"/> metres (see <see cref="RollerPath.CycleOf"/>).</summary>
        public static RollerPose At(RollerPath path, double cycle) {
            if (path == null) throw new ArgumentNullException(nameof(path));

            double length = path.Length;
            bool isResting = cycle > length;
            double distance = Math.Max(0.0, Math.Min(length, cycle));
            double lineOffset = path.StartOffset + distance;
            path.GroundPoint(distance, out double x, out double z);
            double y = path.GroundHeight(distance) + path.Radius + path.Hop(distance);
            double angle = distance / path.Radius;
            if (isResting) angle += path.RockAngle * Math.Sin(2.0 * Math.PI * (cycle - length) / path.RockPeriod);

            double scale = Math.Min(SmoothStep(cycle / path.ScaleDistance), SmoothStep((path.Cycle - cycle) / path.ScaleDistance));
            Rotation(path, path.WobbleSlope(lineOffset), angle, out double rotationX, out double rotationY, out double rotationZ, out double rotationW);
            return new RollerPose(x, y, z, rotationX, rotationY, rotationZ, rotationW, scale, distance, angle, isResting);
        }

        /// <summary>Unit heading of the roll at <paramref name="path"/>'s line offset with wobble slope <paramref name="slope"/>.</summary>
        public static void Heading(RollerPath path, double slope, out double x, out double z) {
            double headingX = path.DirectionX + path.DirectionZ * slope;
            double headingZ = path.DirectionZ - path.DirectionX * slope;
            double length = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            x = headingX / length;
            z = headingZ / length;
        }

        // Roll about up × heading by `angle`, after the seeded spin about up.
        private static void Rotation(RollerPath path, double slope, double angle, out double x, out double y, out double z, out double w) {
            Heading(path, slope, out double headingX, out double headingZ);
            double half = angle * 0.5;
            double sine = Math.Sin(half);
            double rollX = headingZ * sine;
            double rollZ = -headingX * sine;
            double rollW = Math.Cos(half);
            double spinY = Math.Sin(path.Spin * 0.5);
            double spinW = Math.Cos(path.Spin * 0.5);

            // Hamilton product roll ⊗ spin, with roll = (rollX, 0, rollZ, rollW) and spin = (0, spinY, 0, spinW).
            x = rollX * spinW - rollZ * spinY;
            y = rollW * spinY;
            z = rollZ * spinW + rollX * spinY;
            w = rollW * spinW;
        }

        private static double SmoothStep(double share) {
            double clamped = Math.Max(0.0, Math.Min(1.0, share));
            return clamped * clamped * (3.0 - 2.0 * clamped);
        }
    }
}
