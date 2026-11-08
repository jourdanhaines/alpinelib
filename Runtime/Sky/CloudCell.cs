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
        private long _latentTick = NoTick;
        private bool _hasLatentAhead;

        /// <summary>The cell's clouds, in slot order.</summary>
        public List<CloudInstance> Clouds { get; } = new List<CloudInstance>();

        /// <summary>Landmarks that may dissolve this cell's clouds, or whose giant zone may reach them (<see cref="CloudLandmarkIndex.CollectNear"/>), cached per landmark query and 32 m drift step.</summary>
        public List<LandmarkSkyShape> BlockShapes { get; } = new List<LandmarkSkyShape>();
        public List<LandmarkSkyShape> BiasShapes { get; } = new List<LandmarkSkyShape>();

        /// <summary>The <see cref="BiasShapes"/> whose zone under the wind heading <see cref="ZoneHeading"/> (padded for a small turn) reaches the cell.</summary>
        public List<LandmarkSkyShape> ZoneShapes { get; } = new List<LandmarkSkyShape>();
        public WindVector ZoneHeading { get; set; }
        public int ZoneRevision { get; set; } = -1;

        /// <summary>Bumped each time the cell's landmarks are recollected (a new query or drift step).</summary>
        public int CollectCount { get; set; }
        public int LandmarkRevision { get; set; } = -1;
        public long LandmarkStepX { get; set; }
        public long LandmarkStepZ { get; set; }

        /// <summary>Highest landmark top plus the block margin among <see cref="BlockShapes"/>: clouds based at or above it are never blocked here.</summary>
        public double BlockCeiling { get; set; }

        /// <summary>Centre and half-diagonal of the box this cell's clouds stay in at the cached drift step, absolute metres.</summary>
        public double BoxX { get; set; }
        public double BoxZ { get; set; }
        public double BoxRadius { get; set; }

        /// <summary>True when some landmark may change a cloud of this cell.</summary>
        public bool HasInfluence => BlockShapes.Count > 0 || BiasShapes.Count > 0;

        /// <summary>Clouds before the first latent one (the scatter lists regular clouds first).</summary>
        public int RegularCount { get; private set; }

        /// <summary>Counts the regular clouds once the cell is filled.</summary>
        public void CountRegular() {
            RegularCount = 0;
            while (RegularCount < Clouds.Count && !Clouds[RegularCount].IsLatent) RegularCount++;
        }

        /// <summary>True when the offsets of the tick after next are already sampled.</summary>
        public bool HasAhead => _hasAhead;

        /// <summary>
        /// Samples the offsets of <paramref name="tick"/> and the next under their headings, reusing the previous tick's
        /// next (and the sampled-ahead tick) when the tick only advanced by one.
        /// </summary>
        /// Latent clouds are sampled only when <paramref name="withLatent"/> (a landmark may show them), and from scratch
        /// the first tick they are.
        public void Prepare(CloudFieldScatter scatter, long tick, WindVector heading, WindVector nextHeading, bool withLatent = true) {
            bool isGrown = EnsureOffsets();
            if (_tick != tick || isGrown) PrepareRange(scatter, 0, RegularCount, tick, heading, nextHeading, isGrown ? NoTick : _tick, _hasAhead);
            if (withLatent && (_latentTick != tick || isGrown)) PrepareRange(scatter, RegularCount, Clouds.Count, tick, heading, nextHeading, isGrown ? NoTick : _latentTick, _hasLatentAhead);
            if (_tick != tick) _hasAhead = false;
            if (withLatent && _latentTick != tick) _hasLatentAhead = false;
            _tick = tick;
            _latentTick = withLatent ? tick : NoTick;
        }

        private void PrepareRange(CloudFieldScatter scatter, int start, int end, long tick, WindVector heading, WindVector nextHeading, long previous, bool hadAhead) {
            bool isStep = previous != NoTick && previous + 1 == tick;
            bool hasAhead = isStep && hadAhead;
            for (int index = start; index < end; index++) PrepareCloud(scatter, index, tick, heading, nextHeading, isStep, hasAhead);
        }

        /// <summary>
        /// Samples the offsets of <paramref name="aheadTick"/> under <paramref name="aheadHeading"/> when it is the tick
        /// after this cell's next, so the next tick change only shifts. Returns the clouds sampled.
        /// </summary>
        public int PrepareAhead(CloudFieldScatter scatter, long aheadTick, WindVector aheadHeading) {
            if (_hasAhead || _tick == NoTick || _tick + 2 != aheadTick || _offsets.Length < Clouds.Count * Stride) return 0;

            bool withLatent = _latentTick == _tick;
            int end = withLatent ? Clouds.Count : RegularCount;
            for (int index = 0; index < end; index++) {
                int slot = index * Stride;
                scatter.OffsetAtTick(Clouds[index], aheadTick, aheadHeading, out _offsets[slot + 4], out _offsets[slot + 5]);
            }
            _hasAhead = true;
            _hasLatentAhead = withLatent;
            return end;
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
            BlockShapes.Clear();
            BiasShapes.Clear();
            ZoneShapes.Clear();
            ZoneRevision = -1;
            LandmarkRevision = -1;
            RegularCount = 0;
            _latentTick = NoTick;
            _hasLatentAhead = false;
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
