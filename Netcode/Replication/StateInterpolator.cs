using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Turns the stuttering stream of snapshots arriving for one remote entity into a smooth pose to
    /// render, by deliberately drawing it slightly in the past.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Snapshots arrive fifteen times a second, unevenly, and sometimes not at all. Rendering the newest
    /// one directly gives fifteen visible steps a second and a freeze whenever a packet is lost. Instead
    /// this holds a short history and renders <see cref="DelaySeconds"/> behind the estimated server
    /// clock, so under normal jitter there is always a sample on each side of the render time and the
    /// pose is an interpolation rather than a guess.
    /// </para>
    /// <para>
    /// <b>Hermite, not linear.</b> Each sample carries a velocity, and a cubic Hermite spline uses those
    /// as tangents. Linear interpolation between the same two samples gives the right positions and
    /// visibly wrong motion — a pawn that changes direction appears to stop dead at each snapshot and
    /// jerk onto the new heading. The tangents are what make a turn read as a turn.
    /// </para>
    /// <para>
    /// <b>Extrapolation is bounded.</b> When the stream stalls the last sample is projected forward along
    /// its velocity, but only for <see cref="MaxExtrapolationSeconds"/>. Past that the guess is worse
    /// than a freeze: a pawn that ran off in a straight line for a second and then teleports back is more
    /// disorienting, and more misleading to shoot at, than one that simply stopped.
    /// </para>
    /// </remarks>
    public sealed class StateInterpolator {
        /// <summary>Samples held per entity: several seconds of history at the default snapshot rate.</summary>
        public const int DefaultCapacity = 32;

        /// <summary>Longest the last known state may be projected forward, in seconds.</summary>
        public const double MaxExtrapolationSeconds = 0.1;

        private readonly TimedSample[] samples;
        private readonly double delaySeconds;
        private readonly double tickIntervalSeconds;

        private int head;
        private int count;

        /// <summary>Creates an interpolator using the delay and tick rate from configuration.</summary>
        public StateInterpolator(NetConfig config)
            : this(
                (config ?? throw new ArgumentNullException(nameof(config))).InterpolationDelaySeconds,
                config.ServerTickInterval,
                DefaultCapacity) { }

        /// <summary>Creates an interpolator with explicit timing, for tests and for non-standard pawns.</summary>
        public StateInterpolator(double delaySeconds, double tickIntervalSeconds, int capacity) {
            if (tickIntervalSeconds <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(tickIntervalSeconds), "Tick interval must be positive.");
            }

            if (capacity <= 1) {
                throw new ArgumentOutOfRangeException(nameof(capacity), "An interpolator needs room for at least two samples.");
            }

            this.delaySeconds = delaySeconds;
            this.tickIntervalSeconds = tickIntervalSeconds;
            samples = new TimedSample[capacity];
            head = 0;
            count = 0;
        }

        /// <summary>How far behind the estimated server clock this renders.</summary>
        public double DelaySeconds => delaySeconds;

        /// <summary>Samples currently buffered.</summary>
        public int Count => count;

        /// <summary>Tick of the newest sample, or zero when empty.</summary>
        public uint NewestTick => count == 0 ? 0u : SampleAt(count - 1).Tick;

        /// <summary>
        /// Adds a sample. Out-of-order and duplicate ticks are dropped rather than inserted: snapshots
        /// ride a sequenced channel, so anything arriving late has already been superseded, and splicing
        /// it into the history would make the spline briefly interpolate backwards.
        /// </summary>
        public void Push(uint tick, in PawnState state) {
            if (count > 0 && !IsAfter(tick, SampleAt(count - 1).Tick)) {
                return;
            }

            if (count == samples.Length) {
                head = Advance(head);
                count--;
            }

            samples[IndexOf(count)] = new TimedSample(tick, tick * tickIntervalSeconds, in state);
            count++;
        }

        /// <summary>
        /// Produces the pose to render at an estimated server time.
        /// </summary>
        /// <param name="serverSeconds">The client's estimate of the server clock, from <c>NetClock</c>.</param>
        /// <param name="state">The interpolated, held or extrapolated pose.</param>
        /// <returns>False only when nothing has ever been pushed.</returns>
        public bool Sample(double serverSeconds, out PawnState state) {
            if (count == 0) {
                state = default;
                return false;
            }

            double renderSeconds = serverSeconds - delaySeconds;
            TimedSample oldest = SampleAt(0);
            TimedSample newest = SampleAt(count - 1);

            if (renderSeconds <= oldest.Seconds) {
                state = oldest.State;
                return true;
            }

            if (renderSeconds >= newest.Seconds) {
                state = Extrapolate(in newest, renderSeconds - newest.Seconds);
                return true;
            }

            state = InterpolateAcross(renderSeconds);
            return true;
        }

        /// <summary>Forgets all history. Used on despawn, on rejoin and after an authority snap.</summary>
        public void Clear() {
            head = 0;
            count = 0;
        }

        /// <summary>Finds the bracketing pair for a render time and blends between them.</summary>
        private PawnState InterpolateAcross(double renderSeconds) {
            for (int offset = count - 1; offset > 0; offset--) {
                TimedSample later = SampleAt(offset);
                TimedSample earlier = SampleAt(offset - 1);

                if (earlier.Seconds > renderSeconds) {
                    continue;
                }

                return Blend(in earlier, in later, renderSeconds);
            }

            return SampleAt(0).State;
        }

        /// <summary>
        /// Cubic Hermite on position with the samples' velocities as tangents; everything else blends
        /// linearly, and the discrete bits come from whichever end of the span is nearer, since a gait or
        /// a grounded flag has no meaningful halfway value.
        /// </summary>
        private static PawnState Blend(in TimedSample earlier, in TimedSample later, double renderSeconds) {
            double span = later.Seconds - earlier.Seconds;
            float normalized = span <= 0.0 ? 0f : (float)((renderSeconds - earlier.Seconds) / span);
            float spanSeconds = (float)span;

            float squared = normalized * normalized;
            float cubed = squared * normalized;
            float startWeight = 2f * cubed - 3f * squared + 1f;
            float startTangentWeight = cubed - 2f * squared + normalized;
            float endWeight = -2f * cubed + 3f * squared;
            float endTangentWeight = cubed - squared;

            Vector3 position =
                earlier.State.Position * startWeight
                + earlier.State.Velocity * (startTangentWeight * spanSeconds)
                + later.State.Position * endWeight
                + later.State.Velocity * (endTangentWeight * spanSeconds);

            Vector3 velocity = Vector3.Lerp(earlier.State.Velocity, later.State.Velocity, normalized);
            float yaw = LerpYaw(earlier.State.YawDegrees, later.State.YawDegrees, normalized);
            byte flags = normalized < 0.5f ? earlier.State.Flags : later.State.Flags;

            return new PawnState(position, yaw, velocity, flags);
        }

        /// <summary>Projects the newest sample forward along its velocity, capped so it cannot run away.</summary>
        private static PawnState Extrapolate(in TimedSample newest, double aheadSeconds) {
            if (aheadSeconds <= 0.0) {
                return newest.State;
            }

            float clampedAhead = (float)Math.Min(aheadSeconds, MaxExtrapolationSeconds);
            Vector3 position = newest.State.Position + newest.State.Velocity * clampedAhead;

            return new PawnState(position, newest.State.YawDegrees, newest.State.Velocity, newest.State.Flags);
        }

        /// <summary>Blends two yaws the short way around, so 350 to 10 turns twenty degrees, not 340.</summary>
        private static float LerpYaw(float fromDegrees, float toDegrees, float normalized) {
            float difference = (toDegrees - fromDegrees) % 360f;

            if (difference > 180f) {
                difference -= 360f;
            }
            else if (difference < -180f) {
                difference += 360f;
            }

            return fromDegrees + difference * normalized;
        }

        /// <summary>Tick ordering that survives the counter wrapping past uint.MaxValue.</summary>
        private static bool IsAfter(uint tick, uint reference) {
            return (int)(tick - reference) > 0;
        }

        private TimedSample SampleAt(int offset) {
            return samples[IndexOf(offset)];
        }

        private int IndexOf(int offset) {
            return (head + offset) % samples.Length;
        }

        private int Advance(int index) {
            return (index + 1) % samples.Length;
        }

        /// <summary>One received state, with the server time it belongs to precomputed.</summary>
        private readonly struct TimedSample {
            public TimedSample(uint tick, double seconds, in PawnState state) {
                Tick = tick;
                Seconds = seconds;
                State = state;
            }

            /// <summary>The server tick this state came from.</summary>
            public uint Tick { get; }

            /// <summary>That tick expressed on the server's seconds timeline.</summary>
            public double Seconds { get; }

            /// <summary>The state itself.</summary>
            public PawnState State { get; }
        }
    }
}
