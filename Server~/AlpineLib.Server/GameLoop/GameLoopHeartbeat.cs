using System;
using System.Diagnostics;
using System.Threading;

namespace AlpineLib.Server.GameLoop {
    /// <summary>
    /// The pulse the game loop leaves behind on every step, and the only thing another thread may read
    /// about it without going through the inbox.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A health probe cannot ask the loop how it is: if the loop is wedged, the question never gets
    /// answered and the probe times out instead of failing — which reads as "slow" rather than "dead".
    /// So the loop writes a timestamp every step and the probe reads it. A stale stamp means the loop
    /// stopped stepping, and that is exactly what the probe wants to know.
    /// </para>
    /// <para>
    /// Reads and writes are plain interlocked longs. There is no lock because there is nothing to make
    /// consistent: each field stands alone, and a probe that catches a stamp one step out of date draws
    /// the same conclusion either way.
    /// </para>
    /// </remarks>
    public sealed class GameLoopHeartbeat {
        private long _lastBeatTimestamp;
        private long _stepCount;
        private long _serverTick;
        private long _isRunning;

        /// <summary>True between the loop's first step and its last.</summary>
        public bool IsRunning => Interlocked.Read(ref _isRunning) != 0L;

        /// <summary>Steps the loop has completed since it started.</summary>
        public long StepCount => Interlocked.Read(ref _stepCount);

        /// <summary>The authoritative tick as of the most recent step.</summary>
        public uint ServerTick => (uint)Interlocked.Read(ref _serverTick);

        /// <summary>
        /// How long ago the loop last completed a step. <see cref="TimeSpan.MaxValue"/> before the first
        /// step, so a probe that arrives during startup sees "no pulse" rather than "just beat".
        /// </summary>
        public TimeSpan TimeSinceLastBeat {
            get {
                long stamp = Interlocked.Read(ref _lastBeatTimestamp);

                if (stamp == 0L) {
                    return TimeSpan.MaxValue;
                }

                return Stopwatch.GetElapsedTime(stamp);
            }
        }

        /// <summary>Marks the loop as live, before its first step runs.</summary>
        public void MarkStarted() {
            Interlocked.Exchange(ref _isRunning, 1L);
            Interlocked.Exchange(ref _lastBeatTimestamp, Stopwatch.GetTimestamp());
        }

        /// <summary>Records a completed step. Called from the game thread and nowhere else.</summary>
        public void Beat(uint serverTick) {
            Interlocked.Exchange(ref _lastBeatTimestamp, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _serverTick, serverTick);
            Interlocked.Increment(ref _stepCount);
        }

        /// <summary>Marks the loop as stopped, after its last step.</summary>
        public void MarkStopped() {
            Interlocked.Exchange(ref _isRunning, 0L);
        }
    }
}
