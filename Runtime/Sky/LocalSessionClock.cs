using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>A session clock that starts at 0 when created and follows real time, in and out of play mode.</summary>
    public sealed class LocalSessionClock : ISessionClock {
        private readonly double _start;

        public LocalSessionClock() {
            _start = Time.realtimeSinceStartupAsDouble;
        }

        public double Seconds => Time.realtimeSinceStartupAsDouble - _start;
    }
}
