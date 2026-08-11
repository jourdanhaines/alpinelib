using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
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
    /// <para>
    /// <b>Mover carry is added at render time, not on the wire.</b> A pawn standing still on a moving
    /// platform replicates zero velocity — the ride is applied to its position by the simulation, never
    /// to its velocity, so that corrections, settle comparisons and the animator all keep seeing the
    /// pawn's own motion. But a Hermite span whose endpoints both claim zero velocity degenerates to a
    /// smoothstep between two positions a snapshot's travel apart: the rider eases to a stop at every
    /// sample boundary while the platform under it is drawn dead linear, and the residual is a wobble at
    /// exactly the snapshot rate. Worse, extrapolation freezes the rider in world space while the
    /// platform slides on. So when <see cref="CarryWorld"/> is set, each tangent (and the extrapolation
    /// velocity) is augmented with the mover's own per-tick delta whenever the sample's support query
    /// answers "a mover" — a pure, deterministic lookup on the same shared world the simulation stepped.
    /// The velocity handed back to callers stays the raw replicated one; the carry shapes only where the
    /// pawn is drawn.
    /// </para>
    /// <para>
    /// One bounded imperfection: each sample's carry is the delta that <em>produced</em> its tick, so at
    /// a ping-pong turnaround the tangent lags the reversal by one sample. The error is at most one tick
    /// of platform travel and is gone with the next sample.
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

        /// <summary>
        /// Metres above and below a sample's feet the carry probe searches for the supporting surface.
        /// </summary>
        /// <remarks>
        /// Generous on purpose, and for the same reason as the rider probe on the Unity mover view (which
        /// this cannot reference from an engine-free assembly): the question is yes-or-no — which deck is
        /// this state standing on — not a step resolution, and a replicated foot height sits a tolerance
        /// either side of the surface depending on when in the tick it was captured.
        /// </remarks>
        public const float CarryProbeHalfHeightMetres = 0.35f;

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
        /// Scene collision used to augment tangents with mover carry, or null to leave tangents raw.
        /// Null is the correct value for any entity that is itself a mover — a platform's own snapshots
        /// already carry its true velocity, and self-carry would double it.
        /// </summary>
        public CollisionWorld CarryWorld { get; set; }

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

                Vector3 outgoingTangent = later.State.Velocity + CarryVelocityAt(in later);

                Vector3 rawIncomingVelocity = later.State.Velocity;
                Vector3 incomingTangent = outgoingTangent;
                if (offset + 1 < count) {
                    TimedSample next = SampleAt(offset + 1);
                    rawIncomingVelocity = next.State.Velocity;
                    incomingTangent = rawIncomingVelocity + CarryVelocityAt(in next);
                }

                return Blend(in earlier, in later, outgoingTangent, incomingTangent, rawIncomingVelocity, renderSeconds);
            }

            return SampleAt(0).State;
        }

        /// <summary>
        /// The velocity a mover is imparting to this sample, or zero: grounded, supported by a mover at
        /// the sample's own tick, and a world to ask. The delta queried is the one that produced the
        /// sample's tick, matching the segment-tangent convention of the samples themselves.
        /// </summary>
        private Vector3 CarryVelocityAt(in TimedSample sample) {
            if (CarryWorld == null || !sample.State.IsGrounded) {
                return Vector3.Zero;
            }

            Vector3 foot = sample.State.Position;
            bool supported = CarryWorld.TryGetSupport(
                foot.X,
                foot.Z,
                foot.Y + CarryProbeHalfHeightMetres,
                foot.Y - CarryProbeHalfHeightMetres,
                sample.Tick,
                out SupportHit hit);

            if (!supported || !hit.IsMover) {
                return Vector3.Zero;
            }

            return CarryWorld.MoverDelta(hit.MoverIndex, sample.Tick) / (float)tickIntervalSeconds;
        }

        /// <summary>
        /// Cubic Hermite on position. The outgoing tangent of the span is the later sample's velocity —
        /// the velocity of exactly this segment, since each sample reports the motion that produced it —
        /// and the incoming tangent is the following sample's, when one exists. Both arrive already
        /// carry-augmented; the velocity handed back is deliberately the raw replicated pair, so a
        /// standing rider still reports standing still to whoever animates from it. Yaw blends the short
        /// way around; the discrete bits describe the motion across the span and therefore come from the
        /// later sample.
        /// </summary>
        private static PawnState Blend(
            in TimedSample earlier,
            in TimedSample later,
            Vector3 outgoingTangent,
            Vector3 incomingTangent,
            Vector3 rawIncomingVelocity,
            double renderSeconds) {
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
                + outgoingTangent * (startTangentWeight * spanSeconds)
                + later.State.Position * endWeight
                + incomingTangent * (endTangentWeight * spanSeconds);

            Vector3 velocity = Vector3.Lerp(later.State.Velocity, rawIncomingVelocity, normalized);
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

        /// <summary>
        /// Projects the newest sample forward along its velocity, capped so it cannot run away. The
        /// projection includes mover carry — a standing rider projected on its raw zero velocity freezes
        /// in world space while the platform under it is drawn sliding on — but the state handed back
        /// still reports the raw velocity.
        /// </summary>
        private PawnState Extrapolate(in TimedSample newest, double aheadSeconds) {
            if (aheadSeconds <= 0.0) {
                LastOutputPosition = newest.State.Position;
                return newest.State;
            }

            float clampedAhead = (float)Math.Min(aheadSeconds, MaxExtrapolationSeconds);
            Vector3 projectionVelocity = newest.State.Velocity + CarryVelocityAt(in newest);
            Vector3 position = newest.State.Position + projectionVelocity * clampedAhead;
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
