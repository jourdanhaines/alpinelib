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
    /// <see cref="Length"/> metres of rolling (a heavy prop may sit <see cref="StallGap"/> cycle metres at one landing on
    /// the way), then <see cref="RestGap"/> cycle metres settled at the end, then it scales in again at the start. The
    /// cycle position is the wind's drift along the path × the prop's own ground speed share plus a seeded phase, reduced
    /// modulo the cycle in double (<see cref="CycleOf"/>), so every client agrees without sync. Size, speed, wobble, stall,
    /// squash and tilt are drawn from their own seeded stream (<see cref="SizeOf"/>), so they never move the roll's line.
    /// </remarks>
    public sealed class RollerPath {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const double SlopeStep = 0.25;
        private const double HopFloor = 0.65;
        private const int WobbleOctaves = 2;
        private const ulong SizeStream = 0x53495A45UL;
        private const ulong TraitStream = 0x54524149UL;

        private readonly ulong _wobbleSeed;
        private readonly ulong _hopSeed;
        private readonly FbmSettings _wobbleNoise;
        private readonly double _wobbleAmplitude;
        private readonly double _wobbleOrigin;
        private double[] _heights;

        private RollerPath(ulong seed, double slotX, double slotZ, double directionX, double directionZ, double radius, double wobbleAmplitude,
                double groundSpeedShare, RollerPathSettings settings) {
            Seed = seed;
            SlotX = slotX;
            SlotZ = slotZ;
            DirectionX = directionX;
            DirectionZ = directionZ;
            Radius = radius;
            GroundSpeedShare = groundSpeedShare;
            ScaleDistance = settings.ScaleDistance;
            RockAngle = settings.RockAngle;
            RockPeriod = settings.RockPeriod;
            ProfileStep = settings.ProfileStep;
            _wobbleAmplitude = wobbleAmplitude;
            _wobbleSeed = StableHash.Mix(seed, 0x574F42UL);
            _hopSeed = StableHash.Mix(seed, 0x484F50UL);
            _wobbleNoise = new FbmSettings(WobbleOctaves, 1.0 / settings.WobblePeriod);
            _wobbleOrigin = _wobbleAmplitude * Fbm.Sample1(_wobbleSeed, 0.0, _wobbleNoise);
            _heights = Array.Empty<double>();
        }

        public ulong Seed { get; }

        /// <summary>The scattered slot the line runs through.</summary>
        public double SlotX { get; }
        public double SlotZ { get; }

        /// <summary>Unit direction of the roll (downwind).</summary>
        public double DirectionX { get; }
        public double DirectionZ { get; }

        /// <summary>Ball radius, metres: the centre rides this high over the ground (the caller's radius, already sized).</summary>
        public double Radius { get; }

        /// <summary>The prop's size over its kind's mesh (the mesh scale; <see cref="Radius"/> includes it).</summary>
        public double Size { get; private set; } = 1.0;

        /// <summary>Where the size sits in the settings' range, 0 smallest .. 1 biggest; heavier props roll slower.</summary>
        public double Heft { get; private set; }

        /// <summary>Rolled metres from the start where the prop stalls (a landing), or -1 when it never does.</summary>
        public double StallAt { get; private set; } = -1.0;

        /// <summary>Cycle metres the stall lasts (0 without one).</summary>
        public double StallGap { get; private set; }

        /// <summary>Mesh scale per local axis, each in [1 - max squash, 1] with the widest at 1.</summary>
        public double SquashX { get; private set; } = 1.0;
        public double SquashY { get; private set; } = 1.0;
        public double SquashZ { get; private set; } = 1.0;

        /// <summary>Seeded tip of the mesh about its local x before the spin, radians.</summary>
        public double Tilt { get; private set; }

        /// <summary>True when the prop stalls once mid-roll.</summary>
        public bool HasStall => StallAt >= 0.0;

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

        /// <summary>Most the wobble swings either side of its mean, metres; the roll strays up to twice this from its line.</summary>
        public double WobbleAmplitude => _wobbleAmplitude;

        /// <summary>Length of one roll, stall and rest cycle, metres.</summary>
        public double Cycle => Length + StallGap + RestGap;

        /// <summary>
        /// Size of the prop seeded <paramref name="seed"/>: lerp(min, max, u^bias) from its own stream, so a caller can size
        /// the ball (radius = mesh radius × size) before <see cref="Build"/> clips its roll for that radius.
        /// </summary>
        public static double SizeOf(ulong seed, RollerPathSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            double share = new SplitMix64(StableHash.Mix(seed, SizeStream)).NextDouble();
            return Lerp(settings.MinSize, settings.MaxSize, Math.Pow(share, settings.SizeBias));
        }

        /// <summary>
        /// <see cref="Build(ulong, double, double, double, double, WindVector, RollerPathSettings, Func{double, double, bool}, Func{double, double, double})"/>
        /// for a prop of size 1 (the caller's radius as it is).
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, double> groundHeight) {
            return Build(seed, slotX, slotZ, radius, 1.0, prevailing, settings, isClear, groundHeight);
        }

        /// <summary>
        /// The roll of the prop seeded <paramref name="seed"/> at slot (<paramref name="slotX"/>, <paramref name="slotZ"/>) of
        /// <paramref name="radius"/> (sized: the mesh radius × <paramref name="size"/>, see <see cref="SizeOf"/>), heading along <paramref name="prevailing"/> (unit) bent by up to the settings' jitter,
        /// clipped where <paramref name="isClear"/> says the centre may not go and riding <paramref name="groundHeight"/>;
        /// null when the slot is blocked or the clear roll is shorter than <see cref="RollerPathSettings.MinClippedLength"/>.
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, double size, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, double> groundHeight) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (isClear == null) throw new ArgumentNullException(nameof(isClear));
            if (groundHeight == null) throw new ArgumentNullException(nameof(groundHeight));
            if (!(radius > 0.0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Must be positive.");
            if (!(size > 0.0)) throw new ArgumentOutOfRangeException(nameof(size), size, "Must be positive.");
            if (!(prevailing.Length > 0.0)) throw new ArgumentException("The prevailing direction must not be zero.", nameof(prevailing));
            settings.Validate();

            var random = new SplitMix64(seed);
            double bend = WindField.SeriesTangent(settings.DirectionJitterDegrees * DegreesToRadians) * (random.NextDouble() * 2.0 - 1.0);
            double baseX = prevailing.X / prevailing.Length;
            double baseZ = prevailing.Z / prevailing.Length;
            double x = baseX + baseZ * bend;
            double z = baseZ - baseX * bend;
            double norm = Math.Sqrt(x * x + z * z);
            var traits = new SplitMix64(StableHash.Mix(seed, TraitStream));
            double heft = HeftOf(size, settings);
            double speedShare = Clamp01(heft * (1.0 - settings.SpeedJitter) + traits.NextDouble() * settings.SpeedJitter);
            double groundSpeedShare = Lerp(settings.MaxGroundSpeedShare, settings.MinGroundSpeedShare, speedShare);
            double wobble = settings.WobbleAmplitude * Lerp(settings.MinWobbleShare, 1.0, traits.NextDouble());
            var shape = new RollerPath(seed, slotX, slotZ, x / norm, z / norm, radius, wobble, groundSpeedShare, settings);

            double target = Lerp(settings.MinLength, settings.MaxLength, random.NextDouble());
            double spacing = Lerp(settings.MinHopSpacing, settings.MaxHopSpacing, random.NextDouble()) * size;
            double hopHeight = Lerp(settings.MinHopHeight, settings.MaxHopHeight, random.NextDouble()) * size;
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
            shape.Size = size;
            shape.Heft = heft;
            shape.Spin = spin;
            shape.DrawTraits(traits, settings);
            shape.Phase = phaseShare * shape.Cycle;
            shape._heights = shape.SampleHeights(start, length, groundHeight);
            return shape;
        }

        /// <summary>
        /// Rolled metres at cycle position <paramref name="cycle"/>: the cycle itself up to the stall, held at the stall's
        /// landing through it, the cycle less the stall after; past <see cref="Length"/> once resting. Never decreases.
        /// </summary>
        public double RolledAt(double cycle) {
            if (!HasStall || cycle <= StallAt) return cycle;

            return cycle < StallAt + StallGap ? StallAt : cycle - StallGap;
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

        /// <summary>Sideways offset from the line at <paramref name="lineOffset"/> metres from the slot: 0 at the slot, within 2 × amplitude.</summary>
        public double Wobble(double lineOffset) {
            return _wobbleAmplitude * Fbm.Sample1(_wobbleSeed, lineOffset, _wobbleNoise) - _wobbleOrigin;
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

        /// <summary>
        /// Box (x, y, z, metres) every pose's centre stays in at scale 1, padded by <paramref name="pad"/> on every
        /// side: the line's ends widened by the wobble, the ground profile's range lifted by the radius and highest hop.
        /// </summary>
        public void Bounds(double pad, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ) {
            double side = 2.0 * _wobbleAmplitude + pad;
            GroundEnds(out double startX, out double startZ, out double endX, out double endZ);
            minX = Math.Min(startX, endX) - side;
            maxX = Math.Max(startX, endX) + side;
            minZ = Math.Min(startZ, endZ) - side;
            maxZ = Math.Max(startZ, endZ) + side;
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (double height in _heights) {
                low = Math.Min(low, height);
                high = Math.Max(high, height);
            }
            if (_heights.Length == 0) low = high = 0.0;
            minY = low + Radius - pad;
            maxY = high + Radius + HopHeight + pad;
        }

        // The straight line's points at the roll's start and end (no wobble).
        private void GroundEnds(out double startX, out double startZ, out double endX, out double endZ) {
            startX = SlotX + DirectionX * StartOffset;
            startZ = SlotZ + DirectionZ * StartOffset;
            endX = SlotX + DirectionX * (StartOffset + Length);
            endZ = SlotZ + DirectionZ * (StartOffset + Length);
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

        // The farthest offset from `from` toward `limit` in `step` strides whose every sample is clear. The last partial
        // stride is tested at its end like every stride, so a footprint can only be grazed between samples (the gates' slack).
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

        // Stall (heavy props only, at an inner landing so no hop is cut), squash and tilt, all from the trait stream.
        private void DrawTraits(SplitMix64 traits, RollerPathSettings settings) {
            double stallRoll = traits.NextDouble();
            double stallSpot = traits.NextDouble();
            double stallLength = traits.NextDouble();
            if (Heft >= settings.StallHeft && stallRoll < settings.StallChance && HopCount >= 2) {
                int landing = 1 + Math.Min(HopCount - 2, (int)Math.Floor(stallSpot * (HopCount - 1)));
                StallAt = landing * HopSpacing;
                StallGap = Lerp(settings.MinStall, settings.MaxStall, stallLength);
            }

            double squashX = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashY = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashZ = 1.0 - settings.MaxSquash * traits.NextDouble();
            double widest = Math.Max(squashX, Math.Max(squashY, squashZ));
            SquashX = squashX / widest;
            SquashY = squashY / widest;
            SquashZ = squashZ / widest;
            Tilt = (traits.NextDouble() * 2.0 - 1.0) * settings.MaxTiltDegrees * DegreesToRadians;
        }

        private static double HeftOf(double size, RollerPathSettings settings) {
            double range = settings.MaxSize - settings.MinSize;
            return range > 0.0 ? Clamp01((size - settings.MinSize) / range) : 0.5;
        }

        private static double Clamp01(double value) {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
