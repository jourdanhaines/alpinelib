using System;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One wind-rolled prop's track: a near-straight line along the prevailing wind through its scattered slot, wobbling
    /// sideways, clipped where it would leave the caller's clear area, with the caller's support (height, contact normal,
    /// obstacles) sampled along it. The prop rolls in contact, its outline (<see cref="Profile"/>) bumping it up and down,
    /// and leaves the ground only at events (<see cref="Jumps"/>): obstacle and crest runs of the support and seeded bumps,
    /// each with one small bounce. Pure data built once per prop (worker-safe); <see cref="RollerPose"/> places the prop
    /// on it from the wind's drift.
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
        private const int WobbleOctaves = 2;
        private const ulong SizeStream = 0x53495A45UL;
        private const ulong TraitStream = 0x54524149UL;
        private const ulong JumpStream = 0x4A554D50UL;

        // Steepest contact normal the outline is leaned to (a ball touching a rail head's side): 50° off up.
        private const double LeastNormalUp = 0.6428;

        private readonly ulong _wobbleSeed;
        private readonly FbmSettings _wobbleNoise;
        private readonly double _wobbleAmplitude;
        private readonly double _wobbleOrigin;
        private double[] _heights;
        private double[] _pitch;
        private double[] _side;
        private double[] _inverseUp;
        private double[] _besideHeights;
        private double[] _besidePitch;
        private double[] _besideSide;
        private double[] _besideInverseUp;
        private double[] _detail;
        private RollerJump[] _jumps;

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
            _wobbleNoise = new FbmSettings(WobbleOctaves, 1.0 / settings.WobblePeriod);
            _wobbleOrigin = _wobbleAmplitude * Fbm.Sample1(_wobbleSeed, 0.0, _wobbleNoise);
            _heights = _pitch = _side = _inverseUp = Array.Empty<double>();
            _besideHeights = _besidePitch = _besideSide = _besideInverseUp = _detail = Array.Empty<double>();
            _jumps = Array.Empty<RollerJump>();
            Profile = RollerProfile.Ball(radius);
            RollRadius = radius;
        }

        public ulong Seed { get; }

        /// <summary>The scattered slot the line runs through.</summary>
        public double SlotX { get; }
        public double SlotZ { get; }

        /// <summary>Unit direction of the roll (downwind).</summary>
        public double DirectionX { get; }
        public double DirectionZ { get; }

        /// <summary>Ball radius, metres (the caller's radius, already sized): the support and the clipping are for this ball.</summary>
        public double Radius { get; }

        /// <summary>The prop's outline as it rolls (a ball of <see cref="Radius"/> without a shape).</summary>
        public RollerProfile Profile { get; private set; }

        /// <summary>Radius the prop turns at, metres: roll angle = rolled distance / this (the outline's mean reach).</summary>
        public double RollRadius { get; private set; }

        /// <summary>The prop's leaps in rolled order, never overlapping; between them it rolls in contact.</summary>
        public System.Collections.Generic.IReadOnlyList<RollerJump> Jumps => _jumps;

        /// <summary>The prop's size over its kind's mesh (the mesh scale; <see cref="Radius"/> includes it).</summary>
        public double Size { get; private set; } = 1.0;

        /// <summary>Where the size sits in the settings' range, 0 smallest .. 1 biggest; heavier props roll slower.</summary>
        public double Heft { get; private set; }

        /// <summary>Rolled metres from the start where the prop stalls (in contact, clear of every jump), or -1 when it never does.</summary>
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

        /// <summary>Chance this path's seeded bump spots fire.</summary>
        public double BumpChance { get; private set; }

        /// <summary>Highest rise of any jump's arc over its line, metres.</summary>
        public double HighestJump { get; private set; }

        /// <summary>Seeded yaw of the prop about up before it rolls, radians.</summary>
        public double Spin { get; private set; }

        public double GroundSpeedShare { get; }
        public double ScaleDistance { get; }
        public double RockAngle { get; }
        public double RockPeriod { get; }
        public double ProfileStep { get; }

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
        /// <see cref="Build(ulong, double, double, double, double, WindVector, RollerPathSettings, Func{double, double, bool}, Func{double, double, RollerSupport}, RollerShape)"/>
        /// for a ball of size 1 (the caller's radius as it is) on the height field <paramref name="groundHeight"/>.
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, double> groundHeight) {
            return Build(seed, slotX, slotZ, radius, 1.0, prevailing, settings, isClear, groundHeight);
        }

        /// <summary>
        /// <see cref="Build(ulong, double, double, double, double, WindVector, RollerPathSettings, Func{double, double, bool}, Func{double, double, RollerSupport}, RollerShape)"/>
        /// for a ball on the height field <paramref name="groundHeight"/> (<see cref="RollerSupport.BallOnHeights"/>, no obstacles).
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, double size, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, double> groundHeight) {
            if (groundHeight == null) throw new ArgumentNullException(nameof(groundHeight));

            return Build(seed, slotX, slotZ, radius, size, prevailing, settings, isClear, (x, z) => RollerSupport.BallOnHeights(groundHeight, x, z, radius), null);
        }

        /// <summary>
        /// The roll of the prop seeded <paramref name="seed"/> at slot (<paramref name="slotX"/>, <paramref name="slotZ"/>) of
        /// ball <paramref name="radius"/> (sized: the mesh radius × <paramref name="size"/>, see <see cref="SizeOf"/>) and outline
        /// <paramref name="outline"/> at size 1 (null: a plain ball), heading along <paramref name="prevailing"/> bent by up to the
        /// settings' jitter, clipped where <paramref name="isClear"/> says the centre may not go and resting on
        /// <paramref name="support"/> (a ball of <paramref name="radius"/> over each point); null when the slot is blocked
        /// or the clear roll is shorter than <see cref="RollerPathSettings.MinClippedLength"/>.
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, double size, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, RollerSupport> support, RollerShape outline) {
            return Build(seed, slotX, slotZ, radius, size, prevailing, settings, isClear, support, outline, null);
        }

        /// <summary>
        /// <see cref="Build(ulong, double, double, double, double, WindVector, RollerPathSettings, Func{double, double, bool}, Func{double, double, RollerSupport}, RollerShape)"/>
        /// that, where <paramref name="support"/> is rough, drops the posed outline onto <paramref name="surface"/> (the
        /// caller's height under a point, null: none) and keeps the difference to the contact model as a correction.
        /// </summary>
        public static RollerPath Build(ulong seed, double slotX, double slotZ, double radius, double size, WindVector prevailing, RollerPathSettings settings,
                Func<double, double, bool> isClear, Func<double, double, RollerSupport> support, RollerShape outline, Func<double, double, double> surface) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (isClear == null) throw new ArgumentNullException(nameof(isClear));
            if (support == null) throw new ArgumentNullException(nameof(support));
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
            shape.RestGap = restGap;
            shape.Size = size;
            shape.Heft = heft;
            shape.Spin = spin;
            shape.DrawShape(traits, settings);
            shape.Profile = outline != null ? RollerProfile.Of(outline, size, shape.SquashX, shape.SquashY, shape.SquashZ, spin, shape.Tilt) : RollerProfile.Ball(radius);
            shape.RollRadius = shape.Profile.MeanExtent;
            RollerObstacle[] obstacles = shape.SampleSupport(support, out bool[] rough);
            if (surface != null) shape.DropOnRough(rough, surface);
            var planner = new RollerJumpPlanner(shape, settings, new SplitMix64(StableHash.Mix(seed, JumpStream)));
            shape.BumpChance = planner.BumpChance;
            double kept = planner.Plan(obstacles, out shape._jumps);
            if (kept < settings.MinClippedLength) return null;

            shape.Length = kept;
            shape.HighestJump = 0.0;
            foreach (RollerJump jump in shape._jumps) shape.HighestJump = Math.Max(shape.HighestJump, jump.Height);
            shape.DrawStall(traits, settings);
            shape.Phase = phaseShare * shape.Cycle;
            return shape;
        }

        /// <summary>
        /// Rolled metres at cycle position <paramref name="cycle"/>: the cycle itself up to the stall, held at the stall's
        /// spot through it, the cycle less the stall after; past <see cref="Length"/> once resting. Never decreases.
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

        /// <summary>Ground position of the centre after rolling <paramref name="distance"/> metres from the start.</summary>
        public void GroundPoint(double distance, out double x, out double z) {
            PointOnLine(StartOffset + distance, out x, out z);
        }

        /// <summary>The ball's support height (centre less <see cref="Radius"/>) under the centre at <paramref name="distance"/>, linear between samples.</summary>
        public double GroundHeight(double distance) {
            return Sampled(_heights, distance);
        }

        /// <summary>Roll angle at <paramref name="distance"/> rolled metres, radians (before any rocking).</summary>
        public double AngleAt(double distance) {
            return distance / RollRadius;
        }

        /// <summary>
        /// Centre height of the prop in contact at <paramref name="distance"/> turned to <paramref name="angle"/>: the ball's
        /// centre plus how much farther (or nearer) the outline reaches than the ball along the contact normal, over that
        /// normal's up share. On level ground this is the support plus the outline's reach straight down.
        /// </summary>
        public double ContactHeight(double distance, double angle) {
            return ContactHeight(distance, angle, 1.0);
        }

        /// <summary><see cref="ContactHeight(double, double)"/> for the prop drawn at <paramref name="scale"/> of its size (scaling in or out), still touching.</summary>
        public double ContactHeight(double distance, double angle, double scale) {
            double onSupport = OnSurface(_heights, _pitch, _side, _inverseUp, distance, angle, scale);
            double model = Math.Max(onSupport, OnSurface(_besideHeights, _besidePitch, _besideSide, _besideInverseUp, distance, angle, scale));
            return _detail.Length > 0 ? model + Sampled(_detail, distance) * scale : model;
        }

        // Centre height of the outline touching one sampled surface (ball height, contact normal).
        private double OnSurface(double[] heights, double[] pitch, double[] side, double[] inverseUp, double distance, double angle, double scale) {
            double reach = Profile.Support(angle - Sampled(pitch, distance), Sampled(side, distance)) * scale;
            return Sampled(heights, distance) + Radius + (reach - Radius) * Sampled(inverseUp, distance);
        }

        /// <summary>The jump <paramref name="distance"/> lies inside, false while in contact.</summary>
        public bool TryGetJump(double distance, out RollerJump jump) {
            foreach (RollerJump candidate in _jumps) {
                if (candidate.Takeoff >= distance) break;
                if (!candidate.Contains(distance)) continue;

                jump = candidate;
                return true;
            }
            jump = default;
            return false;
        }

        /// <summary>Centre height at <paramref name="distance"/> turned to <paramref name="angle"/>: on a jump's arc, else in contact.</summary>
        public double CentreHeight(double distance, double angle) {
            return TryGetJump(distance, out RollerJump jump) ? jump.HeightAt(distance) : ContactHeight(distance, angle);
        }

        /// <summary>Share of the rolled length spent in contact (outside every jump).</summary>
        public double ContactShare() {
            double airborne = 0.0;
            foreach (RollerJump jump in _jumps) airborne += jump.Length;

            return Length > 0.0 ? 1.0 - airborne / Length : 1.0;
        }

        /// <summary>
        /// Box (x, y, z, metres) every pose's centre stays in at scale 1, padded by <paramref name="pad"/> on every
        /// side: the line's ends widened by the wobble, the contact heights' range (down to the ground itself, for a prop
        /// scaled to nothing) and lifted by the highest jump.
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
            for (int index = 0; index < _heights.Length; index++) {
                double centre = _heights[index] + Radius;
                low = Math.Min(low, centre - Radius * _inverseUp[index]);
                high = Math.Max(high, centre + (Profile.MaxSupport - Radius) * _inverseUp[index]);
                high = Math.Max(high, _besideHeights[index] + Radius + (Profile.MaxSupport - Radius) * _besideInverseUp[index]);
            }
            if (_heights.Length == 0) low = high = Radius;
            minY = low - pad;
            maxY = high + HighestJump + pad;
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

        // Support height, contact normal in the heading's frame (pitch about the roll axis, sideways sine, 1 / up share)
        // and obstacle kind every profile step from the start to the end.
        private RollerObstacle[] SampleSupport(Func<double, double, RollerSupport> support, out bool[] rough) {
            int intervals = Math.Max(1, (int)Math.Ceiling(Length / ProfileStep - 1e-9));
            _heights = new double[intervals + 1];
            _pitch = new double[intervals + 1];
            _side = new double[intervals + 1];
            _inverseUp = new double[intervals + 1];
            _besideHeights = new double[intervals + 1];
            _besidePitch = new double[intervals + 1];
            _besideSide = new double[intervals + 1];
            _besideInverseUp = new double[intervals + 1];
            var obstacles = new RollerObstacle[intervals + 1];
            rough = new bool[intervals + 1];
            for (int index = 0; index <= intervals; index++) {
                double lineOffset = StartOffset + Math.Min(Length, index * ProfileStep);
                PointOnLine(lineOffset, out double x, out double z);
                RollerSupport sample = support(x, z);
                RollerPose.Heading(this, WobbleSlope(lineOffset), out double headingX, out double headingZ);
                FrameNormal(sample.NormalX, sample.NormalY, sample.NormalZ, headingX, headingZ, out _pitch[index], out _side[index], out _inverseUp[index]);
                _heights[index] = sample.Height;
                _besideHeights[index] = sample.HasBeside ? sample.BesideHeight : sample.Height;
                RollerSupport beside = sample.HasBeside ? new RollerSupport(sample.BesideHeight, sample.BesideX, sample.BesideY, sample.BesideZ, RollerObstacle.None) : sample;
                FrameNormal(beside.NormalX, beside.NormalY, beside.NormalZ, headingX, headingZ, out _besidePitch[index], out _besideSide[index], out _besideInverseUp[index]);
                obstacles[index] = sample.Obstacle;
                rough[index] = sample.IsRough;
            }
            return obstacles;
        }

        // At every rough sample, the posed outline's lower points dropped onto the point heights: the correction from the
        // contact model to the height where the first of them touches.
        private void DropOnRough(bool[] rough, Func<double, double, double> surface) {
            if (Profile.IsBall || Array.IndexOf(rough, true) < 0) return;

            _detail = new double[rough.Length];
            for (int index = 0; index < rough.Length; index++) {
                if (!rough[index]) continue;

                double distance = Math.Min(Length, index * ProfileStep);
                double angle = AngleAt(distance);
                double model = ContactHeight(distance, angle);
                double dropped = Dropped(distance, angle, surface);
                if (!double.IsNegativeInfinity(dropped)) _detail[index] = dropped - model;
            }
        }

        // Centre height at which the outline turned to `angle` over `distance` first touches the point heights below it.
        private double Dropped(double distance, double angle, Func<double, double, double> surface) {
            double lineOffset = StartOffset + distance;
            PointOnLine(lineOffset, out double x, out double z);
            RollerPose.Heading(this, WobbleSlope(lineOffset), out double headingX, out double headingZ);
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double lowest = -0.25 * RollRadius;
            double best = double.NegativeInfinity;
            for (int point = 0; point < Profile.PointCount; point++) {
                Profile.Point(point, out double px, out double py, out double pz);
                double up = py * cos - pz * sin;
                if (up > lowest) continue;

                double ahead = py * sin + pz * cos;
                double worldX = x + px * headingZ + ahead * headingX;
                double worldZ = z - px * headingX + ahead * headingZ;
                best = Math.Max(best, surface(worldX, worldZ) - up);
            }
            return best;
        }

        // A contact normal in the heading's frame: pitch about the roll axis, sideways sine and 1 / up share, tipped back
        // toward up first when steeper than the outline is leaned to.
        private static void FrameNormal(double x, double y, double z, double headingX, double headingZ, out double pitch, out double side, out double inverseUp) {
            if (y < LeastNormalUp) {
                double flat = Math.Sqrt(x * x + z * z);
                double scale = flat > 1e-12 ? Math.Sqrt(1.0 - LeastNormalUp * LeastNormalUp) / flat : 0.0;
                x *= scale;
                z *= scale;
                y = LeastNormalUp;
            }
            double ahead = x * headingX + z * headingZ;
            double across = x * headingZ - z * headingX;
            pitch = Math.Atan2(ahead, y);
            side = -across;
            inverseUp = 1.0 / y;
        }

        // A profile sampled every step, linear between samples, clamped to the roll.
        private double Sampled(double[] values, double distance) {
            if (values.Length == 0) return 0.0;
            if (values.Length == 1) return values[0];

            double clamped = Math.Max(0.0, Math.Min(Length, distance));
            int last = values.Length - 1;
            int index = Math.Min(last - 1, (int)Math.Floor(clamped / ProfileStep));
            double from = index * ProfileStep;
            double to = Math.Min(Length, (index + 1) * ProfileStep);
            double share = to > from ? (clamped - from) / (to - from) : 0.0;
            return values[index] + (values[index + 1] - values[index]) * Math.Max(0.0, Math.Min(1.0, share));
        }

        // Squash and tilt from the trait stream (drawn before the outline is built from them).
        private void DrawShape(SplitMix64 traits, RollerPathSettings settings) {
            double squashX = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashY = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashZ = 1.0 - settings.MaxSquash * traits.NextDouble();
            double widest = Math.Max(squashX, Math.Max(squashY, squashZ));
            SquashX = squashX / widest;
            SquashY = squashY / widest;
            SquashZ = squashZ / widest;
            Tilt = (traits.NextDouble() * 2.0 - 1.0) * settings.MaxTiltDegrees * DegreesToRadians;
        }

        // Stall (heavy props only): a seeded spot in the middle of the roll, moved past any jump (and its bounce) it falls in.
        private void DrawStall(SplitMix64 traits, RollerPathSettings settings) {
            double stallRoll = traits.NextDouble();
            double stallSpot = traits.NextDouble();
            double stallLength = traits.NextDouble();
            if (Heft < settings.StallHeft || stallRoll >= settings.StallChance) return;

            double spot = Length * (0.25 + 0.5 * stallSpot);
            foreach (RollerJump jump in _jumps) {
                if (spot > jump.Takeoff && spot < jump.Landing) spot = jump.Landing;
            }
            if (spot >= Length - ProfileStep) return;

            StallAt = spot;
            StallGap = Lerp(settings.MinStall, settings.MaxStall, stallLength);
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
