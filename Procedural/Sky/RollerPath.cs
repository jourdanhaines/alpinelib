using System;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One wind-rolled prop's track: a near-straight line along the prevailing wind through its scattered slot, wobbling
    /// sideways, hopping, clipped where it would leave the caller's clear area, with the ground's height sampled along it.
    /// Pure data built once per prop (worker-safe); <see cref="RollerPose"/> places the prop on it from the wind's drift.
    /// </summary>
    /// <remarks>
    /// Coordinates are the caller's plane (x, z), metres, with the wind's x East and z North. A roll cycles forever:
    /// <see cref="Length"/> metres of rolling, then <see cref="RestGap"/> cycle metres settled at the end, then it scales
    /// in again at the start. The cycle position is the wind's drift along the path × the ground speed share plus a
    /// seeded phase, reduced modulo the cycle in double (<see cref="CycleOf"/>), so every client agrees without sync.
    /// </remarks>
    public sealed class RollerPath {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const double SlopeStep = 0.25;
        private const double HopFloor = 0.65;
        private const int WobbleOctaves = 2;

        private readonly ulong _wobbleSeed;
        private readonly ulong _hopSeed;
        private readonly FbmSettings _wobbleNoise;
        private readonly double _wobbleAmplitude;
        private double[] _heights;

        private RollerPath(ulong seed, double slotX, double slotZ, double directionX, double directionZ, double radius, RollerPathSettings settings) {
            Seed = seed;
            SlotX = slotX;
            SlotZ = slotZ;
            DirectionX = directionX;
            DirectionZ = directionZ;
            Radius = radius;
            GroundSpeedShare = settings.GroundSpeedShare;
            ScaleDistance = settings.ScaleDistance;
            RockAngle = settings.RockAngle;
            RockPeriod = settings.RockPeriod;
            ProfileStep = settings.ProfileStep;
            _wobbleAmplitude = settings.WobbleAmplitude;
            _wobbleSeed = StableHash.Mix(seed, 0x574F42UL);
            _hopSeed = StableHash.Mix(seed, 0x484F50UL);
            _wobbleNoise = new FbmSettings(WobbleOctaves, 1.0 / settings.WobblePeriod);
            _heights = Array.Empty<double>();
        }

        public ulong Seed { get; }

        /// <summary>The scattered slot the line runs through.</summary>
        public double SlotX { get; }
        public double SlotZ { get; }

        /// <summary>Unit direction of the roll (downwind).</summary>
        public double DirectionX { get; }
        public double DirectionZ { get; }

        /// <summary>Ball radius, metres: the centre rides this high over the ground.</summary>
        public double Radius { get; }

        /// <summary>Where the roll starts along the line from the slot, metres (≤ 0: upwind of it).</summary>
        public double StartOffset { get; private set; }

        /// <summary>Rolled metres from the start to the rest point.</summary>
        public double Length { get; private set; }

        /// <summary>Cycle metres spent settled at the end before reappearing at the start.</summary>
        public double RestGap { get; private set; }

        /// <summary>Seeded cycle offset, metres in [0, <see cref="Cycle"/>).</summary>
        public double Phase { get; private set; }

        /// <summary>Whole hops over <see cref="Length"/>; the roll ends on a landing.</summary>
        public int HopCount { get; private set; }

        /// <summary>Highest hop, metres; each hop is 65–100 % of it.</summary>
        public double HopHeight { get; private set; }

        /// <summary>Seeded yaw of the prop about up before it rolls, radians.</summary>
        public double Spin { get; private set; }

        public double GroundSpeedShare { get; }
        public double ScaleDistance { get; }
        public double RockAngle { get; }
        public double RockPeriod { get; }
        public double ProfileStep { get; }

        /// <summary>Metres of one hop.</summary>
        public double HopSpacing => Length / HopCount;

        /// <summary>Most the roll strays sideways from its line, metres.</summary>
        public double WobbleAmplitude => _wobbleAmplitude;

        /// <summary>Length of one roll-and-rest cycle, metres.</summary>
        public double Cycle => Length + RestGap;

        /// <summary>
        /// The roll of the prop seeded <paramref name="seed"/> at slot (<paramref name="slotX"/>, <paramref name="slotZ"/>) of
        /// <paramref name="radius"/>, heading along <paramref name="prevailing"/> (unit) bent by up to the settings' jitter,
        /// clipped where <paramref name="isClear"/> says the centre may not go and riding <paramref name="groundHeight"/>;
        /// null when the slot is blocked or the clear roll is shorter than <see cref="RollerPathSettings.MinClippedLength"/>.
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, double> groundHeight) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (isClear == null) throw new ArgumentNullException(nameof(isClear));
            if (groundHeight == null) throw new ArgumentNullException(nameof(groundHeight));
            if (!(radius > 0.0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Must be positive.");
            if (!(prevailing.Length > 0.0)) throw new ArgumentException("The prevailing direction must not be zero.", nameof(prevailing));
            settings.Validate();

            var random = new SplitMix64(seed);
            double bend = WindField.SeriesTangent(settings.DirectionJitterDegrees * DegreesToRadians) * (random.NextDouble() * 2.0 - 1.0);
            double baseX = prevailing.X / prevailing.Length;
            double baseZ = prevailing.Z / prevailing.Length;
            double x = baseX + baseZ * bend;
            double z = baseZ - baseX * bend;
            double norm = Math.Sqrt(x * x + z * z);
            var shape = new RollerPath(seed, slotX, slotZ, x / norm, z / norm, radius, settings);

            double target = Lerp(settings.MinLength, settings.MaxLength, random.NextDouble());
            double spacing = Lerp(settings.MinHopSpacing, settings.MaxHopSpacing, random.NextDouble());
            double hopHeight = Lerp(settings.MinHopHeight, settings.MaxHopHeight, random.NextDouble());
            double restGap = Lerp(settings.MinRestGap, settings.MaxRestGap, random.NextDouble());
            double phaseShare = random.NextDouble();
            double spin = random.NextDouble() * 2.0 * Math.PI;
            if (!shape.IsClearAt(0.0, isClear)) return null;

            double clipStep = settings.ClipStep;
            double start = shape.WalkClear(0.0, -clipStep, -settings.UpwindReach, isClear);
            double end = shape.WalkClear(start, clipStep, start + target, isClear);
            double length = end - start;
            if (length < settings.MinClippedLength) return null;

            shape.StartOffset = start;
            shape.Length = length;
            shape.HopCount = Math.Max(1, (int)Math.Round(length / spacing));
            shape.HopHeight = hopHeight;
            shape.RestGap = restGap;
            shape.Phase = phaseShare * (length + restGap);
            shape.Spin = spin;
            shape._heights = shape.SampleHeights(start, length, groundHeight);
            return shape;
        }

        /// <summary>The wind's drift (metres, x East z North) along the roll's direction.</summary>
        public double AlongOf(WindVector displacement) {
            return displacement.X * DirectionX + displacement.Z * DirectionZ;
        }

        /// <summary>Cycle position for drift <paramref name="along"/> metres down the path: (along × share + phase) mod cycle, in double.</summary>
        public double CycleOf(double along) {
            double cycle = Cycle;
            double value = along * GroundSpeedShare + Phase;
            double reduced = value - Math.Floor(value / cycle) * cycle;
            return reduced >= cycle || reduced < 0.0 ? 0.0 : reduced;
        }

        /// <summary>Sideways offset of the line at <paramref name="lineOffset"/> metres from the slot.</summary>
        public double Wobble(double lineOffset) {
            return _wobbleAmplitude * Fbm.Sample1(_wobbleSeed, lineOffset, _wobbleNoise);
        }

        /// <summary>Slope of the wobble (sideways metres per metre) at <paramref name="lineOffset"/>.</summary>
        public double WobbleSlope(double lineOffset) {
            return (Wobble(lineOffset + SlopeStep) - Wobble(lineOffset - SlopeStep)) / (2.0 * SlopeStep);
        }

        /// <summary>Ground position of the centre after rolling <paramref name="distance"/> metres from the start (no hop).</summary>
        public void GroundPoint(double distance, out double x, out double z) {
            PointOnLine(StartOffset + distance, out x, out z);
        }

        /// <summary>Ground height under the centre at <paramref name="distance"/>, linear between samples.</summary>
        public double GroundHeight(double distance) {
            if (_heights.Length == 0) return 0.0;

            double clamped = Math.Max(0.0, Math.Min(Length, distance));
            int last = _heights.Length - 1;
            int index = Math.Min(last - 1, (int)Math.Floor(clamped / ProfileStep));
            if (index < 0) return _heights[0];

            double from = index * ProfileStep;
            double to = Math.Min(Length, (index + 1) * ProfileStep);
            double share = to > from ? (clamped - from) / (to - from) : 0.0;
            return _heights[index] + (_heights[index + 1] - _heights[index]) * Math.Max(0.0, Math.Min(1.0, share));
        }

        /// <summary>Hop lift over the ground at <paramref name="distance"/>: a parabola per hop, 0 at each landing.</summary>
        public double Hop(double distance) {
            if (distance <= 0.0 || distance >= Length) return 0.0;

            double spacing = HopSpacing;
            long hop = (long)Math.Floor(distance / spacing);
            double share = distance / spacing - hop;
            double height = HopHeight * (HopFloor + (1.0 - HopFloor) * LatticeHash.Unit(_hopSeed, hop));
            return height * 4.0 * share * (1.0 - share);
        }

        private void PointOnLine(double lineOffset, out double x, out double z) {
            double side = Wobble(lineOffset);
            x = SlotX + DirectionX * lineOffset + DirectionZ * side;
            z = SlotZ + DirectionZ * lineOffset - DirectionX * side;
        }

        private bool IsClearAt(double lineOffset, Func<double, double, bool> isClear) {
            PointOnLine(lineOffset, out double x, out double z);
            return isClear(x, z);
        }

        // The farthest offset from `from` toward `limit` in `step` strides whose every sample is clear.
        private double WalkClear(double from, double step, double limit, Func<double, double, bool> isClear) {
            double reached = from;
            int strides = (int)Math.Floor(Math.Abs(limit - from) / Math.Abs(step) + 1e-9);
            for (int stride = 1; stride <= strides; stride++) {
                double next = from + stride * step;
                if (!IsClearAt(next, isClear)) return reached;

                reached = next;
            }
            return reached != limit && IsClearAt(limit, isClear) ? limit : reached;
        }

        private double[] SampleHeights(double start, double length, Func<double, double, double> groundHeight) {
            int intervals = Math.Max(1, (int)Math.Ceiling(length / ProfileStep - 1e-9));
            var heights = new double[intervals + 1];
            for (int index = 0; index <= intervals; index++) {
                PointOnLine(start + Math.Min(length, index * ProfileStep), out double x, out double z);
                heights[index] = groundHeight(x, z);
            }
            return heights;
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
