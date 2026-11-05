using System;
using System.Threading;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Maps the wind's drift (its travel along the prevailing direction, never decreasing) back to session time, so a
    /// prop rolled from its drift can follow the live wind direction (<see cref="WindField.Direction"/>) at the moment it
    /// rolls each metre. The drift at every whole step is summed once, in order and from zero, as the wind's own
    /// displacement is, so every client maps a drift to the same time. Safe to read from any thread; it extends itself
    /// under a lock as later drifts are asked for.
    /// </summary>
    public sealed class RollerWindClock {
        private const int GrowSteps = 256;

        private readonly object _gate = new object();
        private readonly WindField _field;
        private double[] _drifts = new double[GrowSteps];
        private int _count = 1;
        private WindVector _sum;

        /// <summary>A clock over <paramref name="field"/>'s drift (only its pure functions are read).</summary>
        public RollerWindClock(WindField field) {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            if (!(field.Prevailing.Length > 0.0)) throw new ArgumentException("The wind has no prevailing direction.", nameof(field));
        }

        public WindField Field => _field;

        /// <summary>Whole steps summed so far.</summary>
        public int Steps => Volatile.Read(ref _count) - 1;

        /// <summary>Session seconds at which the drift reaches <paramref name="drift"/> metres (0 at or before the start).</summary>
        public double TimeAt(double drift) {
            int hint = 0;
            return TimeAt(drift, ref hint);
        }

        /// <summary>
        /// <see cref="TimeAt(double)"/> starting the search from step <paramref name="hint"/>, left on the step found, so a
        /// rising series of drifts costs a step or two each.
        /// </summary>
        public double TimeAt(double drift, ref int hint) {
            if (!(drift > 0.0)) {
                hint = 0;
                return 0.0;
            }
            if (double.IsInfinity(drift) || double.IsNaN(drift)) throw new ArgumentOutOfRangeException(nameof(drift), drift, "Must be finite.");

            double[] drifts = Cover(drift, out int count);
            int step = Math.Max(0, Math.Min(hint, count - 2));
            while (step > 0 && drifts[step] > drift) step--;
            while (step < count - 2 && drifts[step + 1] <= drift) step++;
            hint = step;
            double span = drifts[step + 1] - drifts[step];
            double share = span > 0.0 ? (drift - drifts[step]) / span : 0.0;
            return (step + share) * WindField.StepSeconds;
        }

        /// <summary>Unit direction the wind blows toward when its drift reaches <paramref name="drift"/>.</summary>
        public WindVector DirectionAt(double drift, ref int hint) {
            return _field.Direction(TimeAt(drift, ref hint));
        }

        // The summed drifts, extended until one lies past `drift`, and how many are valid.
        private double[] Cover(double drift, out int count) {
            count = Volatile.Read(ref _count);
            double[] drifts = Volatile.Read(ref _drifts);
            if (drifts[count - 1] > drift) return drifts;

            lock (_gate) {
                while (_drifts[_count - 1] <= drift) Extend();
                count = _count;
                return _drifts;
            }
        }

        // Sums the next batch of steps into a (grown) copy, published before the count that makes it valid.
        private void Extend() {
            int count = _count;
            double[] drifts = _drifts;
            if (count + GrowSteps > drifts.Length) {
                var grown = new double[Math.Max(drifts.Length * 2, count + GrowSteps)];
                Array.Copy(drifts, grown, count);
                drifts = grown;
            }
            WindVector prevailing = _field.Prevailing;
            for (int step = count - 1; step < count - 1 + GrowSteps; step++) {
                _sum += _field.Velocity((step + 0.5) * WindField.StepSeconds) * WindField.StepSeconds;
                drifts[step + 1] = _sum.X * prevailing.X + _sum.Z * prevailing.Z;
            }
            Volatile.Write(ref _drifts, drifts);
            Volatile.Write(ref _count, count + GrowSteps);
        }
    }
}
