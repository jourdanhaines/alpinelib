using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Steers one roll around the round stops ahead of it (<see cref="IRollerWorld.StopsNear"/>): every
    /// <see cref="RollerPathSettings.SteerStep"/> it looks <see cref="RollerPathSettings.SteerLookahead"/> metres ahead
    /// of the wandering heading and asks for the smallest extra turn that keeps every stop clear by
    /// <see cref="RollerPathSettings.SteerClearance"/> (staying on the side already taken unless the other saves
    /// <see cref="RollerPathSettings.SteerSidePenalty"/>), with the whole heading held within
    /// <see cref="RollerPathSettings.MaxOffWindDegrees"/> of the wind. The turn eases toward that need and back to the
    /// wander once clear. When no turn within the limit clears (a wall across the wind) it is boxed in and holds its
    /// turn; the walk's own clip test then ends the roll. Pure data per roll, so every client steers alike.
    /// </summary>
    /// <remarks>Turns are radians clockwise seen from above (x East, z North), as <see cref="RollerWalk"/>'s offsets.</remarks>
    public sealed class RollerSteering {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const double Margin = 1e-6;
        private const double HalfTurn = Math.PI * 0.5;

        private readonly RollerPathSettings _settings;
        private readonly double _radius;
        private readonly bool _prefersClockwise;
        private readonly List<RollerDisc> _discs = new List<RollerDisc>();
        private readonly List<double> _candidates = new List<double>();
        private double _nextLook;
        private double _target;

        /// <summary>Steering of a ball of <paramref name="radius"/>; <paramref name="seed"/> breaks exact ties between sides.</summary>
        public RollerSteering(ulong seed, double radius, RollerPathSettings settings) {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _radius = radius;
            _prefersClockwise = (StableHash.Mix(seed, 0x53544552UL) & 1UL) == 0UL;
        }

        /// <summary>The extra turn now, radians.</summary>
        public double Turn { get; private set; }

        /// <summary>Looks ahead that asked for a turn (a stop in the way).</summary>
        public int Steers { get; private set; }

        /// <summary>Looks ahead that found no clear turn within the limit.</summary>
        public int Boxed { get; private set; }

        /// <summary>True once a look ahead reached into a part of the world that is not loaded.</summary>
        public bool TouchedUnloaded { get; private set; }

        /// <summary>
        /// Advances to <paramref name="distance"/> rolled metres (<paramref name="step"/> since the last call) at
        /// (<paramref name="x"/>, <paramref name="z"/>), the wind's unit direction (<paramref name="windX"/>,
        /// <paramref name="windZ"/>) and the walk's own offset <paramref name="wander"/>, and returns the extra turn.
        /// </summary>
        public double Advance(IRollerWorld world, double distance, double step, double x, double z, double windX, double windZ, double wander) {
            if (_settings.SteerLookahead > 0.0 && distance >= _nextLook) {
                _target = Look(world, x, z, windX, windZ, wander);
                _nextLook = distance + _settings.SteerStep;
            }
            bool isTurningIn = Math.Abs(_target) > Math.Abs(Turn) || _target * Turn < 0.0;
            double rate = (isTurningIn ? _settings.SteerTurnRate : _settings.SteerReturnRate) * DegreesToRadians * step;
            Turn += Math.Max(-rate, Math.Min(rate, _target - Turn));
            return Turn;
        }

        // The smallest clear turn off the wandering heading, or the current turn when boxed in.
        private double Look(IRollerWorld world, double x, double z, double windX, double windZ, double wander) {
            _discs.Clear();
            TouchedUnloaded |= !world.StopsNear(x, z, _radius, _settings.SteerLookahead, _discs);
            if (_discs.Count == 0) return 0.0;

            RollerWalk.Bend(windX, windZ, wander, out double headingX, out double headingZ);
            double limit = RollerPathSettings.MaxOffWindDegrees * DegreesToRadians;
            double low = -limit - wander, high = limit - wander;
            CollectCandidates(x, z, headingX, headingZ);
            double best = double.NaN, bestCost = double.PositiveInfinity;
            foreach (double candidate in _candidates) {
                if (candidate < low || candidate > high || !IsClear(candidate, x, z, headingX, headingZ)) continue;

                double cost = CostOf(candidate);
                if (cost >= bestCost) continue;

                best = candidate;
                bestCost = cost;
            }
            if (double.IsNaN(best)) {
                Boxed++;
                return Math.Max(low, Math.Min(high, Turn));
            }
            if (best != 0.0) Steers++;
            return best;
        }

        // No turn, and each stop's two tangent turns just past its edge.
        private void CollectCandidates(double x, double z, double headingX, double headingZ) {
            _candidates.Clear();
            _candidates.Add(0.0);
            foreach (RollerDisc disc in _discs) {
                if (!TryCone(disc, x, z, headingX, headingZ, out double centre, out double half)) continue;

                _candidates.Add(centre - half - Margin);
                _candidates.Add(centre + half + Margin);
            }
        }

        private bool IsClear(double turn, double x, double z, double headingX, double headingZ) {
            foreach (RollerDisc disc in _discs) {
                if (!TryCone(disc, x, z, headingX, headingZ, out double centre, out double half)) continue;
                if (Math.Abs(Wrapped(turn - centre)) < half) return false;
            }
            return true;
        }

        // The turns (relative to the heading) that would roll into the stop's padded disc: the bearing to its centre and
        // the half-width of the tangent cone; false when the stop lies past the look ahead.
        private bool TryCone(RollerDisc disc, double x, double z, double headingX, double headingZ, out double centre, out double half) {
            double dx = disc.X - x, dz = disc.Z - z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double reach = disc.Radius + _radius + _settings.SteerClearance;
            centre = Math.Atan2(dx * headingZ - dz * headingX, dx * headingX + dz * headingZ);
            half = distance > reach ? Math.Asin(reach / distance) : HalfTurn;
            return distance - reach <= _settings.SteerLookahead;
        }

        private static double Wrapped(double angle) {
            if (angle > Math.PI) return angle - 2.0 * Math.PI;
            return angle < -Math.PI ? angle + 2.0 * Math.PI : angle;
        }

        // A turn's size, plus the side penalty when it swaps the side already turned to; an exact tie goes the seeded way.
        private double CostOf(double turn) {
            double penalty = _settings.SteerSidePenalty * DegreesToRadians;
            bool swapsSide = (Turn > Margin && turn < 0.0) || (Turn < -Margin && turn > 0.0);
            double cost = Math.Abs(turn) + (swapsSide ? penalty : 0.0);
            bool isPreferred = _prefersClockwise ? turn >= 0.0 : turn <= 0.0;
            return isPreferred ? cost : cost + Margin;
        }
    }
}
