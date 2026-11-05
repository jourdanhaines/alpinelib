using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The heading of a wind-rolled prop along its roll: the wind (the prevailing direction, or the live one a
    /// <see cref="RollerPath"/> follows) turned by a seeded noise wander (its wavelength drawn per prop) plus seeded
    /// course changes eased in over a few metres, the sum held within ±<see cref="RollerPathSettings.WanderDegrees"/>.
    /// The direction bends the wind's unit vector by the offset's tangent (a fixed series), so the walk is IEEE-exact on
    /// every client. Pure data per prop, any thread.
    /// </summary>
    /// <remarks>Offsets are radians clockwise from the prevailing direction seen from above (x East, z North).</remarks>
    public sealed class RollerWalk {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const ulong WanderSalt = 0x57414E44UL;
        private const ulong CourseSalt = 0x434F5552UL;
        private const int WanderOctaves = 2;

        private readonly double _prevailingX;
        private readonly double _prevailingZ;
        private readonly ulong _wanderSeed;
        private readonly FbmSettings _noise;
        private readonly double _wander;
        private readonly double _gain;
        private readonly double _blend;
        private readonly double[] _spots;
        private readonly double[] _from;
        private readonly double[] _to;

        /// <summary>The walk of the prop seeded <paramref name="seed"/> along <paramref name="prevailing"/>, course changes planned out to <paramref name="length"/> metres.</summary>
        public RollerWalk(ulong seed, WindVector prevailing, RollerPathSettings settings, double length) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!(prevailing.Length > 0.0)) throw new ArgumentException("The prevailing direction must not be zero.", nameof(prevailing));

            _prevailingX = prevailing.X / prevailing.Length;
            _prevailingZ = prevailing.Z / prevailing.Length;
            _wanderSeed = StableHash.Mix(seed, WanderSalt);
            var random = new SplitMix64(StableHash.Mix(seed, CourseSalt));
            WanderPeriod = settings.MinWanderPeriod + (settings.MaxWanderPeriod - settings.MinWanderPeriod) * random.NextDouble();
            _noise = new FbmSettings(WanderOctaves, 1.0 / WanderPeriod);
            _wander = settings.WanderDegrees * DegreesToRadians;
            _gain = settings.WanderGain;
            _blend = settings.CourseBlend;
            PlanCourse(random, settings, length, out _spots, out _from, out _to);
        }

        /// <summary>Wavelength of this prop's wander, metres.</summary>
        public double WanderPeriod { get; }

        /// <summary>Course change spots planned (each eases the mean heading to a new seeded offset).</summary>
        public int CourseChanges => _spots.Length;

        /// <summary>Heading offset off the prevailing wind after <paramref name="distance"/> rolled metres, radians, within the wander.</summary>
        public double OffsetAt(double distance) {
            double offset = _wander * _gain * Fbm.Sample1(_wanderSeed, distance, _noise) + CourseAt(distance);
            return Math.Max(-_wander, Math.Min(_wander, offset));
        }

        /// <summary>Unit heading after <paramref name="distance"/> rolled metres.</summary>
        public void DirectionAt(double distance, out double x, out double z) {
            Bend(_prevailingX, _prevailingZ, OffsetAt(distance), out x, out z);
        }

        /// <summary>
        /// Unit direction <paramref name="offset"/> radians clockwise (seen from above) of the unit direction
        /// (<paramref name="baseX"/>, <paramref name="baseZ"/>), through the tangent series so it is IEEE-exact; |offset| under 90°.
        /// </summary>
        public static void Bend(double baseX, double baseZ, double offset, out double x, out double z) {
            double bend = WindField.SeriesTangent(offset);
            double bentX = baseX + baseZ * bend;
            double bentZ = baseZ - baseX * bend;
            double length = Math.Sqrt(bentX * bentX + bentZ * bentZ);
            x = bentX / length;
            z = bentZ / length;
        }

        // The mean heading's offset: the last spot's change eased in from the one before.
        private double CourseAt(double distance) {
            int index = Array.BinarySearch(_spots, distance);
            if (index < 0) index = ~index - 1;
            if (index < 0) return 0.0;

            double share = Math.Max(0.0, Math.Min(1.0, (distance - _spots[index]) / _blend));
            share = share * share * (3.0 - 2.0 * share);
            return _from[index] + (_to[index] - _from[index]) * share;
        }

        private static void PlanCourse(SplitMix64 random, RollerPathSettings settings, double length, out double[] spots, out double[] from, out double[] to) {
            var spotList = new List<double>();
            var fromList = new List<double>();
            var toList = new List<double>();
            double course = 0.0;
            double limit = settings.CourseDegrees * DegreesToRadians;
            double spot = 0.0;
            while (true) {
                spot += settings.MinCourseSpacing + (settings.MaxCourseSpacing - settings.MinCourseSpacing) * random.NextDouble();
                if (spot >= length) break;

                bool turns = random.NextDouble() < settings.CourseChance;
                double target = (random.NextDouble() * 2.0 - 1.0) * limit;
                if (!turns) continue;

                spotList.Add(spot);
                fromList.Add(course);
                toList.Add(target);
                course = target;
            }
            spots = spotList.ToArray();
            from = fromList.ToArray();
            to = toList.ToArray();
        }
    }
}
