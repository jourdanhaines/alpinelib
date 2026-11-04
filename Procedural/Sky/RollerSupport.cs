using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// What a ball of a given radius centred over one plane point rests on: its centre height less the radius, the unit
    /// normal at the contact (from the contact point to the centre) and whether the contact is a crest or obstacle the
    /// prop jumps. A caller's surface answers it per point; <see cref="BallOnHeights"/> answers it for a height field.
    /// Where two surfaces meet (an embankment's toe on the ground) <see cref="With"/> keeps the lower one beside the
    /// higher, so a prop's outline clears both, not only the one its ball rests on.
    /// </summary>
    public readonly struct RollerSupport {
        private const int Directions = 8;
        private static readonly double[] Rings = { 0.4, 0.8 };

        public RollerSupport(double height, double normalX, double normalY, double normalZ, RollerObstacle obstacle)
                : this(height, normalX, normalY, normalZ, obstacle, double.NegativeInfinity, 0.0, 1.0, 0.0) {
        }

        private RollerSupport(double height, double normalX, double normalY, double normalZ, RollerObstacle obstacle, double besideHeight, double besideX,
                double besideY, double besideZ, bool isRough = false) {
            double length = Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
            bool isValid = length > 1e-9 && normalY > 0.0;
            Height = height;
            NormalX = isValid ? normalX / length : 0.0;
            NormalY = isValid ? normalY / length : 1.0;
            NormalZ = isValid ? normalZ / length : 0.0;
            Obstacle = obstacle;
            BesideHeight = besideHeight;
            BesideX = besideX;
            BesideY = besideY;
            BesideZ = besideZ;
            IsRough = isRough;
        }

        /// <summary>Centre height of the resting ball less its radius, metres.</summary>
        public double Height { get; }

        /// <summary>Unit contact normal, y up; straight up on level ground.</summary>
        public double NormalX { get; }
        public double NormalY { get; }
        public double NormalZ { get; }

        public RollerObstacle Obstacle { get; }

        /// <summary>The second surface's ball height (negative infinity: none) and unit normal.</summary>
        public double BesideHeight { get; }
        public double BesideX { get; }
        public double BesideY { get; }
        public double BesideZ { get; }

        public bool HasBeside => !double.IsNegativeInfinity(BesideHeight);

        /// <summary>
        /// True where the surface is not smooth at the prop's scale (gaps between sleepers): the path then drops the posed
        /// outline onto the caller's point heights there instead of trusting the contact normal alone.
        /// </summary>
        public bool IsRough { get; }

        /// <summary>This support marked rough.</summary>
        public RollerSupport AsRough() {
            return new RollerSupport(Height, NormalX, NormalY, NormalZ, Obstacle, BesideHeight, BesideX, BesideY, BesideZ, true);
        }

        /// <summary>Level support at <paramref name="height"/>.</summary>
        public static RollerSupport Level(double height) {
            return new RollerSupport(height, 0.0, 1.0, 0.0, RollerObstacle.None);
        }

        /// <summary>The higher of the two supports (this one on a tie), the other kept beside it when it is a surface at all.</summary>
        public RollerSupport With(RollerSupport other) {
            RollerSupport high = other.Height > Height ? other : this;
            RollerSupport low = other.Height > Height ? this : other;
            if (double.IsNegativeInfinity(low.Height) || double.IsNaN(low.Height)) return high;

            return new RollerSupport(high.Height, high.NormalX, high.NormalY, high.NormalZ, high.Obstacle, low.Height, low.NormalX, low.NormalY, low.NormalZ, high.IsRough);
        }

        /// <summary>The higher of the two supports (this one on a tie).</summary>
        public RollerSupport Higher(RollerSupport other) {
            return other.Height > Height ? other : this;
        }

        /// <summary>
        /// A ball of <paramref name="radius"/> over (<paramref name="x"/>, <paramref name="z"/>) on the height field
        /// <paramref name="height"/>: the higher of the rest on the field's tangent plane at the centre (its slope across the
        /// disc; exact on a plane) and the highest rest over a contact disc (the centre and two rings of eight points out to
        /// 0.8 r, for bumps), with the plane's normal.
        /// </summary>
        public static RollerSupport BallOnHeights(Func<double, double, double> height, double x, double z, double radius) {
            if (height == null) throw new ArgumentNullException(nameof(height));

            double centre = height(x, z);
            double step = 0.5 * radius;
            double slopeX = (height(x + step, z) - height(x - step, z)) / (2.0 * step);
            double slopeZ = (height(x, z + step) - height(x, z - step)) / (2.0 * step);
            double best = centre + radius * (Math.Sqrt(1.0 + slopeX * slopeX + slopeZ * slopeZ) - 1.0);
            foreach (double ring in Rings) {
                double offset = ring * radius;
                double lift = Math.Sqrt(radius * radius - offset * offset) - radius;
                for (int direction = 0; direction < Directions; direction++) {
                    double angle = 2.0 * Math.PI * direction / Directions;
                    best = Math.Max(best, height(x + offset * Math.Cos(angle), z + offset * Math.Sin(angle)) + lift);
                }
            }

            return new RollerSupport(best, -slopeX, 1.0, -slopeZ, RollerObstacle.None);
        }
    }
}
