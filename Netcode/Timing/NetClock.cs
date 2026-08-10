using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Timing {
    /// <summary>
    /// A client's running estimate of the server's simulation clock.
    ///
    /// The client never sees the server's clock directly — it sees tick stamps that are already one
    /// half-trip old. So the clock free-runs locally via <see cref="Advance"/> and is nudged toward the
    /// truth each time a stamped packet arrives. Small errors are eased out over several frames, because
    /// snapping the timeline on every packet would make every interpolated pawn stutter; a large error
    /// (a stall, a resume from sleep, a rejoin) is snapped, because easing across a gap that size would
    /// take longer than the drift is tolerable.
    ///
    /// <see cref="InterpolationSeconds"/> is the timeline remote pawns are actually rendered on: the
    /// estimate held back by the configured interpolation delay.
    /// </summary>
    public sealed class NetClock {
        /// <summary>Error beyond which the estimate is snapped rather than eased.</summary>
        private const double ResyncThresholdSeconds = 0.25;

        /// <summary>Fraction of the remaining error corrected per observation while easing.</summary>
        private const double SmoothingFactor = 0.1;

        private readonly int serverTickRate;
        private readonly double interpolationDelaySeconds;
        private double estimatedServerSeconds;
        private bool hasObservation;

        public NetClock(NetConfig config) : this(RequireTickRate(config), config.InterpolationDelayMs) { }

        public NetClock(int serverTickRate, int interpolationDelayMs) {
            if (serverTickRate <= 0) {
                throw new ArgumentOutOfRangeException(nameof(serverTickRate));
            }

            if (interpolationDelayMs < 0) {
                throw new ArgumentOutOfRangeException(nameof(interpolationDelayMs));
            }

            this.serverTickRate = serverTickRate;
            interpolationDelaySeconds = interpolationDelayMs / 1000.0;
        }

        /// <summary>Ticks per second of the authoritative loop this clock tracks.</summary>
        public int ServerTickRate => serverTickRate;

        /// <summary>Seconds per authoritative tick.</summary>
        public double TickInterval => 1.0 / serverTickRate;

        /// <summary>Current estimate of the server's clock, in seconds since the server started ticking.</summary>
        public double EstimatedServerSeconds => estimatedServerSeconds;

        /// <summary>Current estimate of the server's tick counter.</summary>
        public uint EstimatedServerTick => (uint)Math.Max(0.0, Math.Floor(estimatedServerSeconds * serverTickRate));

        /// <summary>The delayed timeline remote pawns are rendered on.</summary>
        public double InterpolationSeconds => estimatedServerSeconds - interpolationDelaySeconds;

        /// <summary>Interpolation delay this clock was configured with.</summary>
        public double InterpolationDelaySeconds => interpolationDelaySeconds;

        /// <summary>Round-trip time from the most recent observation, in milliseconds.</summary>
        public int PingMs { get; private set; }

        /// <summary>False until the first server packet has been observed; the estimate is meaningless before then.</summary>
        public bool IsSynchronized => hasObservation;

        /// <summary>
        /// Folds in a tick stamp from the server. Half the round trip is added back because the stamp
        /// describes where the server was when it sent, not where it is now.
        /// </summary>
        public void OnServerTickObserved(uint tick, int pingMs) {
            PingMs = Math.Max(0, pingMs);
            double oneWaySeconds = PingMs / 2000.0;
            double observedSeconds = tick * TickInterval + oneWaySeconds;

            if (!hasObservation) {
                estimatedServerSeconds = observedSeconds;
                hasObservation = true;
                return;
            }

            double error = observedSeconds - estimatedServerSeconds;
            if (Math.Abs(error) > ResyncThresholdSeconds) {
                estimatedServerSeconds = observedSeconds;
                return;
            }

            estimatedServerSeconds += error * SmoothingFactor;
        }

        /// <summary>Free-runs the estimate forward by a frame's worth of local time.</summary>
        public void Advance(double deltaSeconds) {
            if (deltaSeconds <= 0.0) {
                return;
            }

            estimatedServerSeconds += deltaSeconds;
        }

        private static int RequireTickRate(NetConfig config) {
            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            return config.ServerTickRate;
        }

        /// <summary>Drops the estimate so the next observation is treated as a fresh sync. Used on reconnect.</summary>
        public void Reset() {
            estimatedServerSeconds = 0.0;
            hasObservation = false;
            PingMs = 0;
        }
    }
}
