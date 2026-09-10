using System;

namespace AlpineLib.Server.Hosting {
    /// <summary>
    /// Decides when a server that nobody is connected to should stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A local server launched behind a player's single-player session has no operator to shut it down,
    /// so it has to notice for itself. The timer runs from the moment the loop starts rather than from
    /// the first departure: a process that was launched and then never dialled — a launcher that crashed,
    /// a player who backed out of the loading screen — is exactly the case that would otherwise be left
    /// running until the machine is rebooted.
    /// </para>
    /// <para>
    /// The count is over continuous idle time, not total. One connection resets it, so a session that
    /// briefly empties between two players is not mistaken for an abandoned process.
    /// </para>
    /// </remarks>
    public sealed class IdleShutdownTimer {
        private readonly double _idleExitSeconds;

        private double _idleSeconds;

        /// <param name="idleExitSeconds">
        /// How long the process may sit with no connections before it stops. Zero or less disables the
        /// timer, which is what a long-lived dedicated server wants.
        /// </param>
        public IdleShutdownTimer(double idleExitSeconds) {
            _idleExitSeconds = idleExitSeconds;
        }

        /// <summary>True when this timer will ever ask for a shutdown.</summary>
        public bool IsEnabled => _idleExitSeconds > 0.0;

        /// <summary>How long the process has been continuously idle.</summary>
        public double IdleSeconds => _idleSeconds;

        /// <summary>How long idleness is tolerated before the timer fires.</summary>
        public double IdleExitSeconds => _idleExitSeconds;

        /// <summary>
        /// Folds one step into the timer. Returns true exactly once the idle window has elapsed, and
        /// keeps returning true until a peer connects — the caller stops on the first one.
        /// </summary>
        public bool Observe(int connectedPeerCount, double deltaSeconds) {
            if (connectedPeerCount > 0) {
                _idleSeconds = 0.0;
                return false;
            }

            if (!IsEnabled) {
                return false;
            }

            _idleSeconds += Math.Max(0.0, deltaSeconds);
            return _idleSeconds >= _idleExitSeconds;
        }

        /// <summary>Starts the idle window over, as a fresh connection does.</summary>
        public void Reset() {
            _idleSeconds = 0.0;
        }
    }
}
