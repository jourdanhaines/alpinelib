using System;
using System.Numerics;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Follows a puff emitter's position step by step: a velocity estimate that ignores snaps (a net correction, a
    /// re-pose) and the share of each step's motion that estimate does not explain, so live puffs can keep their place.
    /// </summary>
    /// <remarks>
    /// A step of <see cref="TeleportDistance"/> or more is a teleport (the old puffs are dropped). A step faster than
    /// <see cref="MaxSpeed"/> is a snap: the estimate holds. Otherwise the estimate follows the measured velocity, turning
    /// at most <see cref="MaxAcceleration"/>. Frame rate never decides which is which below the teleport distance.
    /// </remarks>
    public sealed class PuffMotion {
        public const double DefaultTeleportDistance = 8.0;
        public const double DefaultMaxSpeed = 40.0;
        public const double DefaultMaxAcceleration = 20.0;

        private Vector3 _last;
        private bool _hasLast;

        /// <summary>Step length, metres, from which the emitter counts as teleported.</summary>
        public double TeleportDistance { get; set; } = DefaultTeleportDistance;

        /// <summary>Fastest plausible emitter speed, m/s; a faster step is a snap.</summary>
        public double MaxSpeed { get; set; } = DefaultMaxSpeed;

        /// <summary>Fastest the estimate may change, m/s².</summary>
        public double MaxAcceleration { get; set; } = DefaultMaxAcceleration;

        /// <summary>The estimated emitter velocity, m/s.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Teleports and snaps seen so far.</summary>
        public int Teleports { get; private set; }
        public int Snaps { get; private set; }

        /// <summary>
        /// Measures the step to <paramref name="position"/> over <paramref name="seconds"/>; false when it was a teleport.
        /// <paramref name="residual"/> is the motion beyond <see cref="Velocity"/> × <paramref name="seconds"/>.
        /// </summary>
        public bool Measure(Vector3 position, double seconds, out Vector3 residual) {
            residual = Vector3.Zero;
            Vector3 last = _last;
            bool hadLast = _hasLast;
            _last = position;
            _hasLast = true;
            if (!hadLast || !(seconds > 0.0)) return true;

            Vector3 moved = position - last;
            if (moved.Length() >= TeleportDistance) {
                Velocity = Vector3.Zero;
                Teleports++;
                return false;
            }

            Follow(moved / (float)seconds, seconds);
            residual = moved - Velocity * (float)seconds;
            return true;
        }

        /// <summary>Moves the last position by a floating-origin shift, so the rebase never reads as motion.</summary>
        public void Shift(Vector3 delta) {
            if (_hasLast) _last += delta;
        }

        /// <summary>Forgets the last position and the estimate (the next step starts afresh).</summary>
        public void Reset() {
            _hasLast = false;
            Velocity = Vector3.Zero;
        }

        private void Follow(Vector3 measured, double seconds) {
            if (measured.Length() > MaxSpeed) {
                Snaps++;
                return;
            }

            Vector3 change = measured - Velocity;
            float most = (float)(MaxAcceleration * seconds);
            float length = change.Length();
            Velocity += length > most ? change * (most / length) : change;
        }
    }
}
