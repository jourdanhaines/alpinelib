using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Decides how far in the past remote pawns are rendered, and moves that decision as the connection
    /// changes underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fixed interpolation delay is a bet placed before the connection is known. Rendering stays
    /// interpolated only while <c>one-way latency + snapshot interval + jitter</c> fits inside the
    /// delay; a bet that loses spends the tail of every snapshot cycle extrapolating and pops back onto
    /// the spline when the next snapshot lands — a vibration at exactly the snapshot rate, visible
    /// whenever the pawn moves. This tracks the budget instead: the delay follows measured latency and
    /// arrival jitter between a configured floor and ceiling.
    /// </para>
    /// <para>
    /// The delay never steps. Render time is <c>estimated server time − delay</c>, so a step in the
    /// delay is a step in every remote pawn's pose; the target is approached at
    /// <see cref="SlewPerSecond"/> instead, which bounds the playback-rate skew at five percent — below
    /// what a viewer can pick out.
    /// </para>
    /// <para>
    /// Pure C#, one per connection, allocation-free. Fed by the client world: one call per snapshot
    /// arrival, one per frame.
    /// </para>
    /// </remarks>
    public sealed class InterpolationTimeline {
        /// <summary>Smoothing factor for the arrival-jitter estimate; ~ten arrivals to converge.</summary>
        public const double JitterSmoothing = 0.1;

        /// <summary>How many jitter deviations the delay budgets for.</summary>
        public const double JitterHeadroomMultiplier = 2.0;

        /// <summary>Fastest the live delay may move toward its target, in seconds per second.</summary>
        public const double SlewPerSecond = 0.05;

        private readonly double snapshotIntervalSeconds;
        private readonly double minDelaySeconds;
        private readonly double maxDelaySeconds;

        private double delaySeconds;
        private double jitterEwmaSeconds;
        private double lastArrivalSeconds;
        private bool hasArrival;

        /// <summary>Creates a timeline from configuration, starting at the configured initial delay.</summary>
        public InterpolationTimeline(NetConfig config) {
            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            snapshotIntervalSeconds = config.SnapshotInterval;
            minDelaySeconds = config.InterpolationDelayMinSeconds;
            maxDelaySeconds = config.InterpolationDelayMaxSeconds;
            delaySeconds = Clamp(config.InterpolationDelaySeconds);
        }

        /// <summary>The delay to render behind the estimated server clock right now, in seconds.</summary>
        public double DelaySeconds => delaySeconds;

        /// <summary>The delay the timeline is currently slewing toward, in seconds. Diagnostic.</summary>
        public double TargetDelaySeconds { get; private set; }

        /// <summary>The smoothed arrival-jitter estimate, in seconds. Diagnostic.</summary>
        public double JitterSeconds => jitterEwmaSeconds;

        /// <summary>
        /// Records that a snapshot arrived now, on the local clock, and folds the arrival spacing into
        /// the jitter estimate.
        /// </summary>
        /// <param name="localSeconds">Any monotonic local clock, consistent across calls.</param>
        public void OnSnapshotArrived(double localSeconds) {
            if (!hasArrival) {
                hasArrival = true;
                lastArrivalSeconds = localSeconds;
                return;
            }

            double spacing = localSeconds - lastArrivalSeconds;
            lastArrivalSeconds = localSeconds;

            if (spacing < 0.0) {
                return;
            }

            double deviation = Math.Abs(spacing - snapshotIntervalSeconds);
            jitterEwmaSeconds += JitterSmoothing * (deviation - jitterEwmaSeconds);
        }

        /// <summary>
        /// Advances the live delay toward its target for one frame.
        /// </summary>
        /// <param name="deltaSeconds">Local frame time.</param>
        /// <param name="pingMs">Current round trip from the transport, in milliseconds.</param>
        public void Update(double deltaSeconds, int pingMs) {
            TargetDelaySeconds = Clamp(ResolveTargetDelay(pingMs));

            if (deltaSeconds <= 0.0) {
                return;
            }

            double error = TargetDelaySeconds - delaySeconds;
            double maxStep = SlewPerSecond * deltaSeconds;

            if (error > maxStep) {
                error = maxStep;
            } else if (error < -maxStep) {
                error = -maxStep;
            }

            delaySeconds += error;
        }

        /// <summary>
        /// The delay the current connection actually needs: one-way latency to cover the age of the
        /// newest sample, one snapshot interval to cover the gap to the next, and headroom of two jitter
        /// deviations — floored at half an interval so a perfectly clean link still keeps one sample of
        /// slack.
        /// </summary>
        private double ResolveTargetDelay(int pingMs) {
            double oneWaySeconds = pingMs > 0 ? pingMs / 2000.0 : 0.0;
            double headroom = JitterHeadroomMultiplier * jitterEwmaSeconds;
            double minimumHeadroom = 0.5 * snapshotIntervalSeconds;

            if (headroom < minimumHeadroom) {
                headroom = minimumHeadroom;
            }

            return oneWaySeconds + snapshotIntervalSeconds + headroom;
        }

        private double Clamp(double value) {
            if (value < minDelaySeconds) return minDelaySeconds;
            if (value > maxDelaySeconds) return maxDelaySeconds;

            return value;
        }
    }
}
