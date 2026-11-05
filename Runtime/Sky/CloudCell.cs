using System.Collections.Generic;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    /// <summary>
    /// One cached field cell of a class's lattice: its clouds and each cloud's offset at the current motion tick and the
    /// next (<see cref="CloudFieldScatter.MotionTick"/>), so a frame only blends them and a new tick samples one.
    /// </summary>
    internal sealed class CloudCell {
        private const long NoTick = long.MinValue;

        private double[] _offsets = new double[0];
        private long _tick = NoTick;

        /// <summary>The cell's clouds, in slot order.</summary>
        public List<CloudInstance> Clouds { get; } = new List<CloudInstance>();

        /// <summary>Samples the offsets of <paramref name="tick"/> and the next (reusing the next of the previous tick) under their headings.</summary>
        public void Prepare(CloudFieldScatter scatter, long tick, WindVector heading, WindVector nextHeading) {
            if (_tick == tick) return;

            bool isNext = !EnsureOffsets() && _tick != NoTick && _tick + 1 == tick;
            for (int index = 0; index < Clouds.Count; index++) PrepareCloud(scatter, index, tick, heading, nextHeading, isNext);
            _tick = tick;
        }

        /// <summary>Cloud <paramref name="index"/>'s offset from its anchor, blended <paramref name="share"/> of the way to the next tick.</summary>
        public void OffsetOf(int index, double share, out double offsetX, out double offsetZ) {
            int slot = index * 4;
            offsetX = CloudFieldScatter.Blend(_offsets[slot], _offsets[slot + 2], share);
            offsetZ = CloudFieldScatter.Blend(_offsets[slot + 1], _offsets[slot + 3], share);
        }

        /// <summary>Empties the cell for reuse.</summary>
        public void Clear() {
            Clouds.Clear();
            _tick = NoTick;
        }

        private void PrepareCloud(CloudFieldScatter scatter, int index, long tick, WindVector heading, WindVector nextHeading, bool isNext) {
            int slot = index * 4;
            CloudInstance cloud = Clouds[index];
            if (isNext) {
                _offsets[slot] = _offsets[slot + 2];
                _offsets[slot + 1] = _offsets[slot + 3];
            } else {
                scatter.OffsetAtTick(cloud, tick, heading, out _offsets[slot], out _offsets[slot + 1]);
            }

            scatter.OffsetAtTick(cloud, tick + 1, nextHeading, out _offsets[slot + 2], out _offsets[slot + 3]);
        }

        // True when the buffer had to grow (its old offsets are gone).
        private bool EnsureOffsets() {
            if (_offsets.Length >= Clouds.Count * 4) return false;

            _offsets = new double[Clouds.Count * 4];
            return true;
        }
    }
}
