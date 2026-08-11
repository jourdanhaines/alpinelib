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
    /// this holds a short history and renders at the time the caller asks for — the estimated server
    /// clock minus the connection's interpolation delay, owned by <see cref="InterpolationTimeline"/> —
    /// so under normal jitter there is always a sample on each side of the render time and the pose is
    /// an interpolation rather than a guess.
    /// </para>
    /// <para>
    /// <b>Hermite, with segment tangents.</b> Each sample's velocity is the velocity of the motion that
    /// <em>produced</em> it — the segment ending at the sample — so for the span between samples a and b
    /// the outgoing tangent at a is b's velocity, and the incoming tangent at b is the velocity of the
    /// sample after b when one is buffered. Using each sample's own velocity at both ends looks right at
    /// constant speed and bulges on every accelerate, turn and stop.
    /// </para>
    /// <para>
    /// <b>Extrapolation is bounded, and its exit is blended.</b> When the stream stalls the last sample
    /// is projected forward along its velocity for at most <see cref="MaxExtrapolationSeconds"/>. When
    /// the stream resumes, the gap between where extrapolation left the pawn and where the spline says
    /// it is decays over <see cref="RecoverySmoothingSeconds"/> instead of popping — the pop at stream
    /// resume is otherwise a vibration at exactly the snapshot rate on any connection whose latency
    /// outruns the delay.
    /// </para>
    /// </remarks>
    public sealed class StateInterpolator {
        /// <summary>Samples held per entity: about two seconds of history at the default snapshot rate.</summary>
        public const int DefaultCapacity = 32;

        /// <summary>Longest the last known state may be projected forward, in seconds.</summary>
        public const double MaxExtrapolationSeconds = 0.1;

        /// <summary>Time constant of the exponential decay that blends extrapolation back onto the spline.</summary>
        public const double RecoverySmoothingSeconds = 0.08;

        /// <summary>Offset magnitude below which the recovery blend is considered finished, in metres.</summary>
        public const float RecoveryEpsilon = 0.001f;

        private readonly TimedSample[] samples;
        private readonly double tickIntervalSeconds;

        private int head;
        private int count;
        private bool wasExtrapolating;
        private Vector3 recoveryOffset;
        private double lastRenderSeconds;
        private bool hasRendered;

        /// <summary>Creates an interpolator using the tick rate from configuration.</summary>
        public StateInterpolator(NetConfig config)
            : this(
                (config ?? throw new ArgumentNullException(nameof(config))).ServerTickInterval,
                DefaultCapacity) { }

        /// <summary>Creates an interpolator with explicit timing, for tests and for non-standard pawns.</summary>
        public StateInterpolator(double tickIntervalSeconds, int capacity) {
            if (tickIntervalSeconds <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(tickIntervalSeconds), "Tick interval must be positive.");
            }

            if (capacity <= 1) {
                throw new ArgumentOutOfRangeException(nameof(capacity), "An interpolator needs room for at least two samples.");
            }

            this.tickIntervalSeconds = tickIntervalSeconds;
            samples = new TimedSample[capacity];
            head = 0;
            count = 0;
        }

        /// <summary>Samples currently buffered.</summary>
        public int Count => count;

        /// <summary>Tick of the newest sample, or zero when empty.</summary>
        public uint NewestTick => count == 0 ? 0u : SampleAt(count - 1).Tick;

        /// <summary>Samples handed out from the extrapolation branch since creation. Diagnostic.</summary>
        public int ExtrapolatedSamples { get; private set; }

        /// <summary>
        /// Adds a sample where its tick belongs. Snapshots and keyframes ride different channels with no
        /// mutual ordering, so a sample legitimately arrives behind one already buffered; splicing it in
        /// keeps the span it belongs to instead of throwing it away and doubling that span. Exact
        /// duplicates are dropped, and a sample older than everything a full buffer holds is not worth
        /// evicting history for.
        /// </summary>
        public void Push(uint tick, in PawnState state) {
            if (count == 0 || IsAfter(tick, SampleAt(count - 1).Tick)) {
                Append(tick, in state);
                return;
            }

            InsertInOrder(tick, in state);
        }

        /// <summary>
        /// Produces the pose to render at a time on the server's clock.
        /// </summary>
        /// <param name="renderSeconds">
        /// The time to render, already delayed: estimated server seconds minus the connection's
        /// interpolation delay.
        /// </param>
        /// <param name="state">The interpolated, held or extrapolated pose.</param>
        /// <returns>False only when nothing has ever been pushed.</returns>
        public bool Sample(double renderSeconds, out PawnState state) {
            if (count == 0) {
                state = default;
                return false;
            }

            double deltaSeconds = hasRendered ? renderSeconds - lastRenderSeconds : 0.0;
            lastRenderSeconds = renderSeconds;
            hasRendered = true;

            TimedSample oldest = SampleAt(0);
            TimedSample newest = SampleAt(count - 1);

            if (renderSeconds <= oldest.Seconds) {
                state = oldest.State;
                wasExtrapolating = false;
                recoveryOffset = Vector3.Zero;
                return true;
            }

            if (renderSeconds >= newest.Seconds) {
                state = Extrapolate(in newest, renderSeconds - newest.Seconds);
                ExtrapolatedSamples++;
                wasExtrapolating = true;
                return true;
            }

            PawnState spline = InterpolateAcross(renderSeconds);
            state = BlendBackFromExtrapolation(in spline, deltaSeconds);
            return true;
        }

        /// <summary>Forgets all history. Used on despawn, on rejoin and after an authority snap.</summary>
        public void Clear() {
            head = 0;
            count = 0;
            wasExtrapolating = false;
            recoveryOffset = Vector3.Zero;
            hasRendered = false;
        }

        private void Append(uint tick, in PawnState state) {
            if (count == samples.Length) {
                head = Advance(head);
                count--;
            }

            samples[IndexOf(count)] = new TimedSample(tick, tick * tickIntervalSeconds, in state);
            count++;
        }

        /// <summary>
        /// Splices a late sample in ahead of newer ones: walks back from the tail to where it belongs,
        /// shifts the newer samples up one slot, and writes it. The walk is over at most the couple of
        /// slots cross-channel reordering can displace a packet by.
        /// </summary>
        private void InsertInOrder(uint tick, in PawnState state) {
            int insertOffset = count;

            while (insertOffset > 0 && IsAfter(SampleAt(insertOffset - 1).Tick, tick)) {
                insertOffset--;
            }

            bool duplicatesExisting = insertOffset > 0 && SampleAt(insertOffset - 1).Tick == tick;
            if (duplicatesExisting) {
                return;
            }

            if (count == samples.Length) {
                if (insertOffset == 0) {
                    // Older than everything in a full buffer: history would be evicted to keep it.
                    return;
                }

                head = Advance(head);
                count--;
                insertOffset--;
            }

            for (int shiftOffset = count; shiftOffset > insertOffset; shiftOffset--) {
                samples[IndexOf(shiftOffset)] = samples[IndexOf(shiftOffset - 1)];
            }

            samples[IndexOf(insertOffset)] = new TimedSample(tick, tick * tickIntervalSeconds, in state);
            count++;
        }

        /// <summary>Finds the bracketing pair for a render time and blends between them.</summary>
        private PawnState InterpolateAcross(double renderSeconds) {
            for (int offset = count - 1; offset > 0; offset--) {
                TimedSample later = SampleAt(offset);
                TimedSample earlier = SampleAt(offset - 1);

                if (earlier.Seconds > renderSeconds) {
                    continue;
                }

                Vector3 incomingTangent = offset + 1 < count
                    ? SampleAt(offset + 1).State.Velocity
                    : later.State.Velocity;

                return Blend(in earlier, in later, incomingTangent, renderSeconds);
            }

            return SampleAt(0).State;
        }

        /// <summary>
        /// Cubic Hermite on position. The outgoing tangent of the span is the later sample's velocity —
        /// the velocity of exactly this segment, since each sample reports the motion that produced it —
        /// and the incoming tangent is the following sample's, when one exists. Yaw blends the short way
        /// around; the discrete bits describe the motion across the span and therefore come from the
        /// later sample.
        /// </summary>
        private static PawnState Blend(
            in TimedSample earlier,
            in TimedSample later,
            Vector3 incomingTangent,
            double renderSeconds) {
            double span = later.Seconds - earlier.Seconds;
            float normalized = span <= 0.0 ? 0f : (float)((renderSeconds - earlier.Seconds) / span);
            float spanSeconds = (float)span;

            Vector3 outgoingTangent = later.State.Velocity;

            float squared = normalized * normalized;
            float cubed = squared * normalized;
            float startWeight = 2f * cubed - 3f * squared + 1f;
            float startTangentWeight = cubed - 2f * squared + normalized;
            float endWeight = -2f * cubed + 3f * squared;
            float endTangentWeight = cubed - squared;

            Vector3 position =
                earlier.State.Position * startWeight
                + outgoingTangent * (startTangentWeight * spanSeconds)
                + later.State.Position * endWeight
                + incomingTangent * (endTangentWeight * spanSeconds);

            Vector3 velocity = Vector3.Lerp(outgoingTangent, incomingTangent, normalized);
            float yaw = LerpYaw(earlier.State.YawDegrees, later.State.YawDegrees, normalized);

            return new PawnState(position, yaw, velocity, later.State.Flags);
        }

        /// <summary>
        /// Pays back the positional gap left by an extrapolation episode, exponentially, so the first
        /// interpolated frames after a stall continue from where the pawn was drawn rather than popping
        /// onto the spline.
        /// </summary>
        private PawnState BlendBackFromExtrapolation(in PawnState spline, double deltaSeconds) {
            if (wasExtrapolating) {
                wasExtrapolating = false;
                recoveryOffset = LastOutputPosition - spline.Position;
            }

            if (recoveryOffset == Vector3.Zero) {
                LastOutputPosition = spline.Position;
                return spline;
            }

            if (deltaSeconds > 0.0) {
                recoveryOffset *= (float)Math.Exp(-deltaSeconds / RecoverySmoothingSeconds);
            }

            if (recoveryOffset.LengthSquared() < RecoveryEpsilon * RecoveryEpsilon) {
                recoveryOffset = Vector3.Zero;
                LastOutputPosition = spline.Position;
                return spline;
            }

            PawnState offsetState = spline;
            offsetState.Position += recoveryOffset;
            LastOutputPosition = offsetState.Position;
            return offsetState;
        }

        /// <summary>Projects the newest sample forward along its velocity, capped so it cannot run away.</summary>
        private PawnState Extrapolate(in TimedSample newest, double aheadSeconds) {
            if (aheadSeconds <= 0.0) {
                LastOutputPosition = newest.State.Position;
                return newest.State;
            }

            float clampedAhead = (float)Math.Min(aheadSeconds, MaxExtrapolationSeconds);
            Vector3 position = newest.State.Position + newest.State.Velocity * clampedAhead;
            LastOutputPosition = position;

            return new PawnState(position, newest.State.YawDegrees, newest.State.Velocity, newest.State.Flags);
        }

        /// <summary>The position handed out by the most recent sample, extrapolated or not.</summary>
        private Vector3 LastOutputPosition { get; set; }

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
