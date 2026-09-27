using System;
using System.Diagnostics;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>Main-thread time a <see cref="CellStreamer"/> may spend instantiating cells in one frame.</summary>
    public sealed class CellBuildBudget {
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private double _frameMilliseconds;

        /// <summary>A budget of <paramref name="frameMilliseconds"/> per frame; positive infinity is unlimited.</summary>
        public CellBuildBudget(double frameMilliseconds) {
            FrameMilliseconds = frameMilliseconds;
        }

        /// <summary>Milliseconds per frame; positive infinity never yields.</summary>
        public double FrameMilliseconds {
            get => _frameMilliseconds;
            set {
                if (!(value > 0.0)) throw new ArgumentOutOfRangeException(nameof(value), value, "Frame budget must be positive.");

                _frameMilliseconds = value;
            }
        }

        /// <summary>True when the budget never runs out (synchronous builds).</summary>
        public bool IsUnlimited => double.IsPositiveInfinity(_frameMilliseconds);

        /// <summary>Time spent since the frame began.</summary>
        public double ElapsedMilliseconds => _stopwatch.Elapsed.TotalMilliseconds;

        /// <summary>True once this frame's time is used up; builders yield when they see it.</summary>
        public bool ShouldYield => !IsUnlimited && ElapsedMilliseconds >= _frameMilliseconds;

        /// <summary>A budget that never yields.</summary>
        public static CellBuildBudget CreateUnlimited() {
            return new CellBuildBudget(double.PositiveInfinity);
        }

        /// <summary>Restarts the frame clock.</summary>
        public void BeginFrame() {
            _stopwatch.Restart();
        }
    }
}
