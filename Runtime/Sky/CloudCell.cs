using System.Collections.Generic;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    /// <summary>
    /// One cached field cell of a class's lattice: its clouds and each cloud's offset at the current motion tick, the
    /// next (<see cref="CloudFieldScatter.MotionTick"/>) and, once sampled ahead, the one after, so a frame only blends
    /// them and a tick change only shifts them (the tick after next is sampled a few cells a frame beforehand).
    /// </summary>
    internal sealed class CloudCell {
        private const long NoTick = long.MinValue;
        private const int Stride = 6;

        private double[] _offsets = new double[0];
        private long _tick = NoTick;
        private bool _hasAhead;

        /// <summary>The cell's clouds, in slot order.</summary>
        public List<CloudInstance> Clouds { get; } = new List<CloudInstance>();

        /// <summary>True when the offsets of the tick after next are already sampled.</summary>
        public bool HasAhead => _hasAhead;

        /// <summary>
        /// Samples the offsets of <paramref name="tick"/> and the next under their headings, reusing the previous tick's
        /// next (and the sampled-ahead tick) when the tick only advanced by one.
        /// </summary>
        public void Prepare(CloudFieldScatter scatter, long tick, WindVector heading, WindVector nextHeading) {
            if (_tick == tick) return;

            bool isGrown = EnsureOffsets();
            bool isStep = !isGrown && _tick != NoTick && _tick + 1 == tick;
            bool hasAhead = isStep && _hasAhead;
            for (int index = 0; index < Clouds.Count; index++) PrepareCloud(scatter, index, tick, heading, nextHeading, isStep, hasAhead);
            _tick = tick;
            _hasAhead = false;
        }

        /// <summary>
        /// Samples the offsets of <paramref name="aheadTick"/> under <paramref name="aheadHeading"/> when it is the tick
        /// after this cell's next, so the next tick change only shifts. Returns the clouds sampled.
        /// </summary>
        public int PrepareAhead(CloudFieldScatter scatter, long aheadTick, WindVector aheadHeading) {
            if (_hasAhead || _tick == NoTick || _tick + 2 != aheadTick || _offsets.Length < Clouds.Count * Stride) return 0;

            for (int index = 0; index < Clouds.Count; index++) {
                int slot = index * Stride;
                scatter.OffsetAtTick(Clouds[index], aheadTick, aheadHeading, out _offsets[slot + 4], out _offsets[slot + 5]);
            }
            _hasAhead = true;
            return Clouds.Count;
        }

        /// <summary>Cloud <paramref name="index"/>'s offset from its anchor, blended <paramref name="share"/> of the way to the next tick.</summary>
        public void OffsetOf(int index, double share, out double offsetX, out double offsetZ) {
            int slot = index * Stride;
            offsetX = CloudFieldScatter.Blend(_offsets[slot], _offsets[slot + 2], share);
            offsetZ = CloudFieldScatter.Blend(_offsets[slot + 1], _offsets[slot + 3], share);
        }

        /// <summary>Empties the cell for reuse.</summary>
        public void Clear() {
            Clouds.Clear();
            _tick = NoTick;
            _hasAhead = false;
        }

        private void PrepareCloud(CloudFieldScatter scatter, int index, long tick, WindVector heading, WindVector nextHeading, bool isStep, bool hasAhead) {
            int slot = index * Stride;
            CloudInstance cloud = Clouds[index];
            if (isStep) {
                _offsets[slot] = _offsets[slot + 2];
                _offsets[slot + 1] = _offsets[slot + 3];
            } else {
                scatter.OffsetAtTick(cloud, tick, heading, out _offsets[slot], out _offsets[slot + 1]);
            }

            if (hasAhead) {
                _offsets[slot + 2] = _offsets[slot + 4];
                _offsets[slot + 3] = _offsets[slot + 5];
                return;
            }

            scatter.OffsetAtTick(cloud, tick + 1, nextHeading, out _offsets[slot + 2], out _offsets[slot + 3]);
        }

        // True when the buffer had to grow (its old offsets are gone).
        private bool EnsureOffsets() {
            if (_offsets.Length >= Clouds.Count * Stride) return false;

            _offsets = new double[Clouds.Count * Stride];
            return true;
        }
    }
}
