using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How far one sized, squashed and tipped rolling prop reaches from its centre toward the ground as it rolls: its
    /// support (farthest point along a direction) over the roll angle (<see cref="AngleBins"/> bins) and a sideways lean
    /// of the ground's normal (<see cref="TiltBins"/> bins over ±<see cref="MaxLeanDegrees"/>°), in the prop's frame after
    /// its spin and tilt and before its roll. Each bin keeps the outline point farthest along it, and a query takes the
    /// farthest of its four surrounding bins' points along the exact direction, so the reach has no interpolation sag. Straight down at roll angle θ is the support at θ
    /// without lean, so a prop on level ground rests with its centre <see cref="Extent"/>(θ) up and bumps along its
    /// irregular outline. A ball's profile is its radius everywhere. Plain data, built per prop on any thread.
    /// </summary>
    /// <remarks>
    /// Direction (φ, γ) in the prop's frame is (sin γ, −cos γ cos φ, cos γ sin φ): x along the roll axis, y up, z ahead.
    /// </remarks>
    public sealed class RollerProfile {
        public const int AngleBins = 120;
        public const int TiltBins = 17;
        public const double MaxLeanDegrees = 40.0;

        private const double TwoPi = 2.0 * Math.PI;
        private static readonly double MaxLean = MaxLeanDegrees * Math.PI / 180.0;

        private readonly ushort[] _farthest;
        private readonly float[] _points;
        private readonly float[] _all;
        private readonly double _radius;

        private RollerProfile(float[] support, ushort[] farthest, float[] points, float[] all, double radius) {
            _all = all;
            _farthest = farthest;
            _points = points;
            _radius = radius;
            if (support == null) {
                MeanExtent = MinExtent = MaxExtent = MinSupport = MaxSupport = radius;
                return;
            }

            int level = TiltBins / 2;
            double sum = 0.0;
            MinExtent = MinSupport = double.MaxValue;
            MaxExtent = MaxSupport = double.MinValue;
            for (int index = 0; index < support.Length; index++) {
                MinSupport = Math.Min(MinSupport, support[index]);
                MaxSupport = Math.Max(MaxSupport, support[index]);
                if (index / AngleBins != level) continue;

                sum += support[index];
                MinExtent = Math.Min(MinExtent, support[index]);
                MaxExtent = Math.Max(MaxExtent, support[index]);
            }
            MeanExtent = sum / AngleBins;
        }

        /// <summary>True for a plain ball (no outline).</summary>
        public bool IsBall => _farthest == null;

        /// <summary>Mean reach straight down over a turn: the radius a prop rolling without slipping turns at (its outline's perimeter / 2π).</summary>
        public double MeanExtent { get; }

        /// <summary>Least and most reach straight down over a turn, metres.</summary>
        public double MinExtent { get; }
        public double MaxExtent { get; }

        /// <summary>Least and most reach over every tabulated direction, metres.</summary>
        public double MinSupport { get; }
        public double MaxSupport { get; }

        /// <summary>A ball of <paramref name="radius"/>.</summary>
        public static RollerProfile Ball(double radius) {
            if (!(radius > 0.0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Must be positive.");

            return new RollerProfile(null, null, null, null, radius);
        }

        /// <summary>
        /// The profile of <paramref name="shape"/> scaled by <paramref name="size"/> and per mesh axis by the squash, tipped
        /// <paramref name="tilt"/> radians about its x, then spun <paramref name="spin"/> radians about up (the order the
        /// pose applies them).
        /// </summary>
        public static RollerProfile Of(RollerShape shape, double size, double squashX, double squashY, double squashZ, double spin, double tilt) {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (!(size > 0.0)) throw new ArgumentOutOfRangeException(nameof(size), size, "Must be positive.");

            int count = shape.PointCount;
            var xs = new double[count];
            var ys = new double[count];
            var zs = new double[count];
            double tiltCos = Math.Cos(tilt), tiltSin = Math.Sin(tilt), spinCos = Math.Cos(spin), spinSin = Math.Sin(spin);
            for (int index = 0; index < count; index++) {
                shape.Point(index, out double x, out double y, out double z);
                Pose(x, y, z, size, squashX, squashY, squashZ, tiltCos, tiltSin, spinCos, spinSin, out xs[index], out ys[index], out zs[index]);
            }

            if (count > ushort.MaxValue) throw new ArgumentException($"An outline of {count} points is past the profile's {ushort.MaxValue}.", nameof(shape));

            var support = new float[AngleBins * TiltBins];
            var farthest = new ushort[AngleBins * TiltBins];
            for (int tiltBin = 0; tiltBin < TiltBins; tiltBin++) FillRow(support, farthest, tiltBin, xs, ys, zs);

            var points = new float[count * 3];
            for (int index = 0; index < count; index++) {
                points[index * 3] = (float)xs[index];
                points[index * 3 + 1] = (float)ys[index];
                points[index * 3 + 2] = (float)zs[index];
            }
            var all = new float[shape.AllCount * 3];
            for (int index = 0; index < shape.AllCount; index++) {
                shape.AllPoint(index, out double x, out double y, out double z);
                Pose(x, y, z, size, squashX, squashY, squashZ, tiltCos, tiltSin, spinCos, spinSin, out double px, out double py, out double pz);
                all[index * 3] = (float)px;
                all[index * 3 + 1] = (float)py;
                all[index * 3 + 2] = (float)pz;
            }
            return new RollerProfile(support, farthest, points, all, shape.Reach * size);
        }

        /// <summary>Every distinct mesh point, in the prop's frame after its size, squash, tilt and spin (none for a ball).</summary>
        public int PointCount => _all != null ? _all.Length / 3 : 0;

        public void Point(int index, out double x, out double y, out double z) {
            x = _all[index * 3];
            y = _all[index * 3 + 1];
            z = _all[index * 3 + 2];
        }

        // A mesh point sized, squashed, tipped about x and spun about up.
        private static void Pose(double x, double y, double z, double size, double squashX, double squashY, double squashZ, double tiltCos, double tiltSin,
                double spinCos, double spinSin, out double posedX, out double posedY, out double posedZ) {
            x *= squashX * size;
            y *= squashY * size;
            z *= squashZ * size;
            double tippedY = y * tiltCos - z * tiltSin;
            double tippedZ = y * tiltSin + z * tiltCos;
            posedX = x * spinCos + tippedZ * spinSin;
            posedY = tippedY;
            posedZ = -x * spinSin + tippedZ * spinCos;
        }

        /// <summary>Reach straight down at roll angle <paramref name="angle"/>, metres.</summary>
        public double Extent(double angle) {
            return Support(angle, 0.0);
        }

        /// <summary>
        /// Reach along direction (φ = <paramref name="angle"/>, sin γ = <paramref name="sideSine"/>), metres; the lean is
        /// clamped to the table's ±<see cref="MaxLeanDegrees"/>°, the angle wraps.
        /// </summary>
        public double Support(double angle, double sideSine) {
            if (_farthest == null) return _radius;

            double lean = Math.Max(-MaxLean, Math.Min(MaxLean, Math.Asin(Math.Max(-1.0, Math.Min(1.0, sideSine)))));
            double row = (lean + MaxLean) / (2.0 * MaxLean) * (TiltBins - 1);
            int low = Math.Min(TiltBins - 2, (int)Math.Floor(row));
            double column = (angle / TwoPi - Math.Floor(angle / TwoPi)) * AngleBins;
            int left = Math.Min(AngleBins - 1, (int)Math.Floor(column));
            int right = (left + 1) % AngleBins;
            double level = Math.Cos(lean);
            double side = Math.Sin(lean);
            double down = -level * Math.Cos(angle);
            double ahead = level * Math.Sin(angle);
            double most = Along(_farthest[low * AngleBins + left], side, down, ahead);
            most = Math.Max(most, Along(_farthest[low * AngleBins + right], side, down, ahead));
            most = Math.Max(most, Along(_farthest[(low + 1) * AngleBins + left], side, down, ahead));
            return Math.Max(most, Along(_farthest[(low + 1) * AngleBins + right], side, down, ahead));
        }

        private double Along(int point, double side, double down, double ahead) {
            return _points[point * 3] * side + _points[point * 3 + 1] * down + _points[point * 3 + 2] * ahead;
        }

        private static void FillRow(float[] support, ushort[] farthest, int tiltBin, double[] xs, double[] ys, double[] zs) {
            double lean = -MaxLean + 2.0 * MaxLean * tiltBin / (TiltBins - 1);
            double side = Math.Sin(lean);
            double level = Math.Cos(lean);
            for (int angleBin = 0; angleBin < AngleBins; angleBin++) {
                double angle = TwoPi * angleBin / AngleBins;
                double down = -level * Math.Cos(angle);
                double ahead = level * Math.Sin(angle);
                double most = double.NegativeInfinity;
                int best = 0;
                for (int index = 0; index < xs.Length; index++) {
                    double along = xs[index] * side + ys[index] * down + zs[index] * ahead;
                    if (along <= most) continue;

                    most = along;
                    best = index;
                }
                support[tiltBin * AngleBins + angleBin] = (float)most;
                farthest[tiltBin * AngleBins + angleBin] = (ushort)best;
            }
        }

    }
}
