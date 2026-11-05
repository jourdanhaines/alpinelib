using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One wind-rolled prop's roll from its spawn point: a seeded walk (<see cref="RollerWalk"/>) along the prevailing
    /// wind across the caller's world (<see cref="IRollerWorld"/>), run until the prop has rolled its life, meets
    /// something too big to hop or reaches the edge of the loaded world (<see cref="End"/>), with the world's support
    /// (height, contact normal, obstacles) sampled along it. The prop rolls in contact, its outline (<see cref="Profile"/>)
    /// bumping it up and down, and leaves the ground only at events (<see cref="Jumps"/>): obstacle and crest runs of the
    /// support (rail heads, low rocks, embankment shoulders) and seeded bumps, each with one small bounce. It scales in
    /// over its first <see cref="FadeLength"/> metres and out over its last. Pure data built once per prop (worker-safe);
    /// <see cref="RollerPose"/> places the prop on it from the rolled distance.
    /// </summary>
    /// <remarks>
    /// Coordinates are the world's plane (x, z), metres, x East and z North. Size, speed, squash and tilt are drawn from
    /// their own seeded stream (<see cref="SizeOf"/>, <see cref="GroundSpeedShareOf"/>), so a spawner knows them before
    /// any world does, and they never move the walk.
    /// </remarks>
    public sealed class RollerPath {
        private const ulong SizeStream = 0x53495A45UL;
        private const ulong TraitStream = 0x54524149UL;
        private const ulong JumpStream = 0x4A554D50UL;

        // Steepest contact normal the outline is leaned to (a ball touching a rail head's side): 50° off up.
        private const double LeastNormalUp = 0.6428;

        private double[] _x;
        private double[] _z;
        private double[] _headingX;
        private double[] _headingZ;
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

        private RollerPath(ulong seed, double startX, double startZ, double radius, double size, double heft, double groundSpeedShare, RollerPathSettings settings, RollerWalk walk) {
            Seed = seed;
            StartX = startX;
            StartZ = startZ;
            Radius = radius;
            Size = size;
            Heft = heft;
            GroundSpeedShare = groundSpeedShare;
            ProfileStep = settings.ProfileStep;
            Walk = walk;
            _x = _z = _headingX = _headingZ = Array.Empty<double>();
            _heights = _pitch = _side = _inverseUp = Array.Empty<double>();
            _besideHeights = _besidePitch = _besideSide = _besideInverseUp = _detail = Array.Empty<double>();
            _jumps = Array.Empty<RollerJump>();
            Profile = RollerProfile.Ball(radius);
            RollRadius = radius;
        }

        public ulong Seed { get; }

        /// <summary>Where the roll starts (its spawn point).</summary>
        public double StartX { get; }
        public double StartZ { get; }

        /// <summary>Ball radius, metres (the caller's radius, already sized): the support and the blocking are for this ball.</summary>
        public double Radius { get; }

        /// <summary>The prop's outline as it rolls (a ball of <see cref="Radius"/> without a shape).</summary>
        public RollerProfile Profile { get; private set; }

        /// <summary>Radius the prop turns at, metres: roll angle = rolled distance / this (the outline's mean reach).</summary>
        public double RollRadius { get; private set; }

        /// <summary>The prop's leaps in rolled order, never overlapping; between them it rolls in contact.</summary>
        public IReadOnlyList<RollerJump> Jumps => _jumps;

        /// <summary>The heading the roll follows.</summary>
        public RollerWalk Walk { get; }

        /// <summary>The prop's size over its kind's mesh (the mesh scale; <see cref="Radius"/> includes it).</summary>
        public double Size { get; }

        /// <summary>Where the size sits in the settings' range, 0 smallest .. 1 biggest; heavier props roll slower.</summary>
        public double Heft { get; }

        /// <summary>Mesh scale per local axis, each in [1 - max squash, 1] with the widest at 1.</summary>
        public double SquashX { get; private set; } = 1.0;
        public double SquashY { get; private set; } = 1.0;
        public double SquashZ { get; private set; } = 1.0;

        /// <summary>Seeded tip of the mesh about its local x before the spin, radians.</summary>
        public double Tilt { get; private set; }

        /// <summary>Seeded yaw of the prop about up before it rolls, radians.</summary>
        public double Spin { get; private set; }

        /// <summary>Rolled metres from the start to the end.</summary>
        public double Length { get; private set; }

        /// <summary>Why the roll ends there.</summary>
        public RollerEnd End { get; private set; }

        /// <summary>Rolled metres the prop scales in over at the start and out over before the end.</summary>
        public double FadeLength { get; private set; }

        /// <summary>Chance this path's seeded bump spots fire.</summary>
        public double BumpChance { get; private set; }

        /// <summary>Highest rise of any jump's arc over its line, metres.</summary>
        public double HighestJump { get; private set; }

        /// <summary>Share of the wind's drift along the prevailing direction the prop rolls.</summary>
        public double GroundSpeedShare { get; }

        public double ProfileStep { get; }

        /// <summary>
        /// Size of the prop seeded <paramref name="seed"/>: lerp(min, max, u^bias) from its own stream, so a caller can size
        /// the ball (radius = mesh radius × size) before <see cref="Build"/> rolls it for that radius.
        /// </summary>
        public static double SizeOf(ulong seed, RollerPathSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            double share = new SplitMix64(StableHash.Mix(seed, SizeStream)).NextDouble();
            return Lerp(settings.MinSize, settings.MaxSize, Math.Pow(share, settings.SizeBias));
        }

        /// <summary>Ground speed share of the prop seeded <paramref name="seed"/> at <paramref name="size"/>: heavier props slower, jittered.</summary>
        public static double GroundSpeedShareOf(ulong seed, double size, RollerPathSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return ShareOf(new SplitMix64(StableHash.Mix(seed, TraitStream)).NextDouble(), HeftOf(size, settings), settings);
        }

        /// <summary>
        /// The roll of the prop seeded <paramref name="seed"/> from (<paramref name="startX"/>, <paramref name="startZ"/>) of
        /// ball <paramref name="radius"/> (sized: the mesh radius × <paramref name="size"/>, see <see cref="SizeOf"/>) and
        /// outline <paramref name="outline"/> at size 1 (null: a plain ball), walking along <paramref name="prevailing"/> for
        /// at most <paramref name="life"/> metres over <paramref name="world"/>; null when the start is blocked or the roll
        /// stops sooner than <see cref="RollerPathSettings.MinClippedLength"/> (a roll cut by the world's edge is kept).
        /// </summary>
        public static RollerPath Build(ulong seed, double startX, double startZ, double radius, double size, double life, WindVector prevailing,
                RollerPathSettings settings, IRollerWorld world, RollerShape outline) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!(radius > 0.0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Must be positive.");
            if (!(size > 0.0)) throw new ArgumentOutOfRangeException(nameof(size), size, "Must be positive.");
            if (!(life > 0.0)) throw new ArgumentOutOfRangeException(nameof(life), life, "Must be positive.");
            if (!(prevailing.Length > 0.0)) throw new ArgumentException("The prevailing direction must not be zero.", nameof(prevailing));
            settings.Validate();
            if (world.BlockAt(startX, startZ, radius) != RollerBlock.None) return null;

            var traits = new SplitMix64(StableHash.Mix(seed, TraitStream));
            double heft = HeftOf(size, settings);
            double share = ShareOf(traits.NextDouble(), heft, settings);
            var walk = new RollerWalk(seed, prevailing, settings, life);
            var shape = new RollerPath(seed, startX, startZ, radius, size, heft, share, settings, walk) {
                Spin = new SplitMix64(seed).NextDouble() * 2.0 * Math.PI,
            };
            shape.WalkClear(world, life, settings.ClipStep);
            if (shape.Length < settings.ProfileStep || (shape.End != RollerEnd.Edge && shape.Length < settings.MinClippedLength)) return null;

            shape.DrawShape(traits, settings);
            shape.Profile = outline != null ? RollerProfile.Of(outline, size, shape.SquashX, shape.SquashY, shape.SquashZ, shape.Spin, shape.Tilt) : RollerProfile.Ball(radius);
            shape.RollRadius = shape.Profile.MeanExtent;
            RollerObstacle[] obstacles = shape.SampleSupport(world, out bool[] rough);
            shape.DropOnRough(rough, world);
            var planner = new RollerJumpPlanner(shape, settings, new SplitMix64(StableHash.Mix(seed, JumpStream)));
            shape.BumpChance = planner.BumpChance;
            shape.Length = planner.Plan(obstacles, out shape._jumps);
            if (shape.End != RollerEnd.Edge && shape.Length < settings.MinClippedLength) return null;
            if (shape.Length < settings.ProfileStep) return null;

            foreach (RollerJump jump in shape._jumps) shape.HighestJump = Math.Max(shape.HighestJump, jump.Height);
            shape.FadeLength = Math.Min(share * settings.FadeDrift, shape.Length * 0.5);
            return shape;
        }

        /// <summary>The roll of <paramref name="spawn"/> from absolute (<paramref name="x"/>, <paramref name="z"/>), its size, life and seed taken from the spawn.</summary>
        public static RollerPath Build(RollerSpawn spawn, double x, double z, double radius, WindVector prevailing, RollerPathSettings settings, IRollerWorld world,
                RollerShape outline) {
            return Build(spawn.Id, x, z, radius, spawn.Size, spawn.Life, prevailing, settings, world, outline);
        }

        /// <summary>Ground position of the centre after rolling <paramref name="distance"/> metres from the start (clamped to the roll).</summary>
        public void GroundPoint(double distance, out double x, out double z) {
            Locate(distance, out int index, out double share);
            x = _x[index] + (_x[index + 1] - _x[index]) * share;
            z = _z[index] + (_z[index + 1] - _z[index]) * share;
        }

        /// <summary>Unit heading of the roll after <paramref name="distance"/> metres.</summary>
        public void HeadingAt(double distance, out double x, out double z) {
            Locate(distance, out int index, out double share);
            double headingX = _headingX[index] + (_headingX[index + 1] - _headingX[index]) * share;
            double headingZ = _headingZ[index] + (_headingZ[index + 1] - _headingZ[index]) * share;
            double length = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            x = headingX / length;
            z = headingZ / length;
        }

        /// <summary>Visible share of the prop's size at <paramref name="distance"/>: scaling in over the first fade, out over the last, 0 off the roll.</summary>
        public double FadeAt(double distance) {
            if (distance < 0.0 || distance > Length) return 0.0;
            if (!(FadeLength > 0.0)) return 1.0;

            return Math.Min(SmoothStep(distance / FadeLength), SmoothStep((Length - distance) / FadeLength));
        }

        /// <summary>The ball's support height (centre less <see cref="Radius"/>) under the centre at <paramref name="distance"/>, linear between samples.</summary>
        public double GroundHeight(double distance) {
            return Sampled(_heights, distance);
        }

        /// <summary>Roll angle at <paramref name="distance"/> rolled metres, radians.</summary>
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

        /// <summary><see cref="ContactHeight(double, double)"/> for the prop drawn at <paramref name="scale"/> of its size (fading in or out), still touching.</summary>
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
        /// Box (x, y, z, metres) every pose's centre stays in at scale 1, padded by <paramref name="pad"/> on every side:
        /// the walk's extent, the contact heights' range (down to the ground itself, for a prop scaled to nothing) and
        /// lifted by the highest jump.
        /// </summary>
        public void Bounds(double pad, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ) {
            int last = LastSample();
            minX = minZ = double.PositiveInfinity;
            maxX = maxZ = double.NegativeInfinity;
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            for (int index = 0; index <= last; index++) {
                minX = Math.Min(minX, _x[index]);
                maxX = Math.Max(maxX, _x[index]);
                minZ = Math.Min(minZ, _z[index]);
                maxZ = Math.Max(maxZ, _z[index]);
                double centre = _heights[index] + Radius;
                low = Math.Min(low, centre - Radius * _inverseUp[index]);
                high = Math.Max(high, centre + (Profile.MaxSupport - Radius) * _inverseUp[index]);
                high = Math.Max(high, _besideHeights[index] + Radius + (Profile.MaxSupport - Radius) * _besideInverseUp[index]);
            }
            minX -= pad;
            minZ -= pad;
            maxX += pad;
            maxZ += pad;
            minY = low - pad;
            maxY = high + HighestJump + pad;
        }

        // Walks from the start in profile steps, testing the ball against the world every clip step, until the life is
        // rolled or a test fails; the roll then ends at the last clear test.
        private void WalkClear(IRollerWorld world, double life, double clipStep) {
            double step = ProfileStep;
            int every = Math.Max(1, (int)Math.Round(clipStep / step));
            int most = Math.Max(1, (int)Math.Floor(life / step + 1e-9));
            var xs = new List<double>(Math.Min(most, 8192) + 1) { StartX };
            var zs = new List<double>(xs.Capacity) { StartZ };
            Walk.DirectionAt(0.0, out double firstX, out double firstZ);
            var hxs = new List<double>(xs.Capacity) { firstX };
            var hzs = new List<double>(xs.Capacity) { firstZ };
            int clear = 0;
            End = RollerEnd.Life;
            double x = StartX, z = StartZ;
            for (int index = 1; index <= most; index++) {
                Walk.DirectionAt((index - 0.5) * step, out double midX, out double midZ);
                x += midX * step;
                z += midZ * step;
                Walk.DirectionAt(index * step, out double headingX, out double headingZ);
                xs.Add(x);
                zs.Add(z);
                hxs.Add(headingX);
                hzs.Add(headingZ);
                if (index % every != 0 && index != most) continue;

                RollerBlock block = world.BlockAt(x, z, Radius);
                if (block != RollerBlock.None) {
                    End = block == RollerBlock.Edge ? RollerEnd.Edge : RollerEnd.Blocked;
                    break;
                }
                clear = index;
            }
            int count = clear + 1;
            _x = Trimmed(xs, count);
            _z = Trimmed(zs, count);
            _headingX = Trimmed(hxs, count);
            _headingZ = Trimmed(hzs, count);
            Length = clear * step;
        }

        private static double[] Trimmed(List<double> values, int count) {
            var array = new double[Math.Max(2, count)];
            for (int index = 0; index < array.Length; index++) array[index] = values[Math.Min(index, values.Count - 1)];

            return array;
        }

        // Support height, contact normal in the heading's frame (pitch about the roll axis, sideways sine, 1 / up share)
        // and obstacle kind at every walk sample from the start to the end.
        private RollerObstacle[] SampleSupport(IRollerWorld world, out bool[] rough) {
            int count = LastSample() + 1;
            _heights = new double[count];
            _pitch = new double[count];
            _side = new double[count];
            _inverseUp = new double[count];
            _besideHeights = new double[count];
            _besidePitch = new double[count];
            _besideSide = new double[count];
            _besideInverseUp = new double[count];
            var obstacles = new RollerObstacle[count];
            rough = new bool[count];
            for (int index = 0; index < count; index++) {
                RollerSupport sample = world.SupportAt(_x[index], _z[index], Radius);
                double headingX = _headingX[index], headingZ = _headingZ[index];
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
        private void DropOnRough(bool[] rough, IRollerWorld world) {
            if (Profile.IsBall || Array.IndexOf(rough, true) < 0) return;

            _detail = new double[rough.Length];
            for (int index = 0; index < rough.Length; index++) {
                if (!rough[index]) continue;

                double distance = Math.Min(Length, index * ProfileStep);
                double angle = AngleAt(distance);
                double model = ContactHeight(distance, angle);
                double dropped = Dropped(index, angle, world);
                if (!double.IsNegativeInfinity(dropped)) _detail[index] = dropped - model;
            }
        }

        // Centre height at which the outline turned to `angle` over sample `index` first touches the point heights below it.
        private double Dropped(int index, double angle, IRollerWorld world) {
            double x = _x[index], z = _z[index];
            double headingX = _headingX[index], headingZ = _headingZ[index];
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
                best = Math.Max(best, world.SurfaceAt(worldX, worldZ) - up);
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

        // The walk sample at or after the end (the last one any query reads).
        private int LastSample() {
            return Math.Min(_x.Length - 1, (int)Math.Ceiling(Length / ProfileStep - 1e-9));
        }

        // The sample interval holding `distance` (clamped to the roll) and the share across it.
        private void Locate(double distance, out int index, out double share) {
            double clamped = Math.Max(0.0, Math.Min(Length, distance));
            int last = _x.Length - 1;
            index = Math.Max(0, Math.Min(last - 1, (int)Math.Floor(clamped / ProfileStep)));
            share = Math.Max(0.0, Math.Min(1.0, (clamped - index * ProfileStep) / ProfileStep));
        }

        // A profile sampled every step, linear between samples, clamped to the roll.
        private double Sampled(double[] values, double distance) {
            if (values.Length == 0) return 0.0;
            if (values.Length == 1) return values[0];

            double clamped = Math.Max(0.0, Math.Min(Length, distance));
            int last = values.Length - 1;
            int index = Math.Min(last - 1, (int)Math.Floor(clamped / ProfileStep));
            double share = (clamped - index * ProfileStep) / ProfileStep;
            return values[index] + (values[index + 1] - values[index]) * Math.Max(0.0, Math.Min(1.0, share));
        }

        // Squash and tilt from the trait stream (drawn after the speed share, before the outline is built from them).
        private void DrawShape(SplitMix64 traits, RollerPathSettings settings) {
            double squashX = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashY = 1.0 - settings.MaxSquash * traits.NextDouble();
            double squashZ = 1.0 - settings.MaxSquash * traits.NextDouble();
            double widest = Math.Max(squashX, Math.Max(squashY, squashZ));
            SquashX = squashX / widest;
            SquashY = squashY / widest;
            SquashZ = squashZ / widest;
            Tilt = (traits.NextDouble() * 2.0 - 1.0) * settings.MaxTiltDegrees * Math.PI / 180.0;
        }

        private static double ShareOf(double jitter, double heft, RollerPathSettings settings) {
            double speedShare = Clamp01(heft * (1.0 - settings.SpeedJitter) + jitter * settings.SpeedJitter);
            return Lerp(settings.MaxGroundSpeedShare, settings.MinGroundSpeedShare, speedShare);
        }

        private static double HeftOf(double size, RollerPathSettings settings) {
            double range = settings.MaxSize - settings.MinSize;
            return range > 0.0 ? Clamp01((size - settings.MinSize) / range) : 0.5;
        }

        private static double SmoothStep(double share) {
            double clamped = Clamp01(share);
            return clamped * clamped * (3.0 - 2.0 * clamped);
        }

        private static double Clamp01(double value) {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double Lerp(double from, double to, double share) {
            return from + (to - from) * share;
        }
    }
}
