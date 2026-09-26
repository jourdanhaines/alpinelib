using System;
using System.Globalization;
using System.Numerics;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// Where a kit part sits in the kit frame (x across, y along, z up) and its yaw about +z in degrees,
    /// counter-clockwise seen from above.
    /// </summary>
    public readonly struct KitPose {
        /// <summary>Builds a pose.</summary>
        public KitPose(Vector3 position, float yawDegrees) {
            Position = position;
            YawDegrees = yawDegrees;
        }

        /// <summary>The origin with no yaw.</summary>
        public static KitPose Identity => new KitPose(Vector3.Zero, 0f);

        /// <summary>Position in the kit frame.</summary>
        public Vector3 Position { get; }

        /// <summary>Yaw about +z, degrees, counter-clockwise from above.</summary>
        public float YawDegrees { get; }

        /// <summary>
        /// This pose turned about the kit origin by <paramref name="degrees"/>: the position swings round
        /// the z axis and the yaw adds up. Quarter turns are exact.
        /// </summary>
        public KitPose Rotated(float degrees) {
            return new KitPose(RotateVector(Position, degrees), YawDegrees + degrees);
        }

        /// <summary>This pose moved by <paramref name="offset"/>, yaw unchanged.</summary>
        public KitPose Translated(Vector3 offset) {
            return new KitPose(Position + offset, YawDegrees);
        }

        /// <summary>
        /// Rotates <paramref name="vector"/> about +z by <paramref name="degrees"/>. Multiples of 90° use
        /// exact sines, so grid-aligned layouts produce identical floats on every runtime.
        /// </summary>
        public static Vector3 RotateVector(Vector3 vector, float degrees) {
            SinCos(degrees, out float sine, out float cosine);
            return new Vector3(
                vector.X * cosine - vector.Y * sine,
                vector.X * sine + vector.Y * cosine,
                vector.Z);
        }

        /// <summary>"(x, y, z) yaw°", culture-invariant.</summary>
        public override string ToString() {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.###}, {1:0.###}, {2:0.###}) {3:0.##}°",
                Position.X, Position.Y, Position.Z, YawDegrees);
        }

        private static void SinCos(float degrees, out float sine, out float cosine) {
            double quarterTurns = degrees / 90.0;
            if (quarterTurns == Math.Floor(quarterTurns) && !double.IsInfinity(quarterTurns)) {
                QuarterTurnSinCos((long)quarterTurns, out sine, out cosine);
                return;
            }
            double radians = degrees * (Math.PI / 180.0);
            sine = (float)Math.Sin(radians);
            cosine = (float)Math.Cos(radians);
        }

        private static void QuarterTurnSinCos(long quarterTurns, out float sine, out float cosine) {
            long wrapped = ((quarterTurns % 4) + 4) % 4;
            sine = wrapped == 1 ? 1f : wrapped == 3 ? -1f : 0f;
            cosine = wrapped == 0 ? 1f : wrapped == 2 ? -1f : 0f;
        }
    }
}
