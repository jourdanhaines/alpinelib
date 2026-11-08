using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The landmarks near a camera binned on a coarse grid, so a cloud only tests the few that can dissolve it or whose
    /// giant zone can reach it: a landmark is listed as blocking only when its top can stand above some cloud's base, and for bias only if a giant.
    /// Built once per landmark query; results are the same as testing every landmark. Not thread safe: lookups remember
    /// the last bin (clouds come cell by cell, so neighbours share it), so use one index per thread.
    /// </summary>
    public sealed class CloudLandmarkIndex {
        /// <summary>Edge of one bin, metres.</summary>
        public const double BinSize = 256.0;

        private static readonly List<LandmarkSkyShape> None = new List<LandmarkSkyShape>();

        private readonly Dictionary<(long X, long Z), List<LandmarkSkyShape>> _block = new Dictionary<(long X, long Z), List<LandmarkSkyShape>>();
        private readonly Dictionary<(long X, long Z), List<LandmarkSkyShape>> _bias = new Dictionary<(long X, long Z), List<LandmarkSkyShape>>();

        private (long X, long Z) _blockBin = (long.MinValue, long.MinValue);
        private (long X, long Z) _biasBin = (long.MinValue, long.MinValue);
        private IReadOnlyList<LandmarkSkyShape> _blockShapes = None;
        private IReadOnlyList<LandmarkSkyShape> _biasShapes = None;
        private double _maxReach;

        /// <summary>Landmarks indexed (after the altitude cut and giant filter: any listed once or more).</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Rebuilds from <paramref name="shapes"/> for clouds of footprint radius up to <paramref name="maxReach"/> whose
        /// bases sit at <paramref name="lowestAltitude"/> or above.
        /// </summary>
        public void Build(IReadOnlyList<LandmarkSkyShape> shapes, CloudLandmarkSettings settings, double maxReach, double lowestAltitude) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _block.Clear();
            _bias.Clear();
            Count = 0;
            _maxReach = maxReach;
            _blockBin = (long.MinValue, long.MinValue);
            _biasBin = (long.MinValue, long.MinValue);
            _blockShapes = None;
            _biasShapes = None;
            if (shapes == null) return;

            double biasReach = settings.BiasDownwind + settings.BiasRadius + settings.BiasFade;
            foreach (LandmarkSkyShape shape in shapes) {
                bool isBlocking = shape.PlateauY + settings.BlockMargin > lowestAltitude;
                bool isGiant = shape.Tier == LandmarkSkyTier.Giant;
                if (isBlocking) Bin(_block, shape, shape.BoundX, shape.BoundZ, shape.BoundRadius + maxReach + settings.DissolveFar);
                if (isGiant) Bin(_bias, shape, shape.AnchorX, shape.AnchorZ, biasReach);
                Count += isBlocking || isGiant ? 1 : 0;
            }
        }

        /// <summary>The landmarks that may dissolve a cloud centred at (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public IReadOnlyList<LandmarkSkyShape> BlockShapesAt(double x, double z) {
            (long X, long Z) bin = BinOf(x, z);
            if (bin == _blockBin) return _blockShapes;

            _blockBin = bin;
            _blockShapes = _block.TryGetValue(bin, out List<LandmarkSkyShape> shapes) ? shapes : None;
            return _blockShapes;
        }

        /// <summary>The giants whose zone may reach (<paramref name="x"/>, <paramref name="z"/>) under any wind heading.</summary>
        public IReadOnlyList<LandmarkSkyShape> BiasShapesAt(double x, double z) {
            (long X, long Z) bin = BinOf(x, z);
            if (bin == _biasBin) return _biasShapes;

            _biasBin = bin;
            _biasShapes = _bias.TryGetValue(bin, out List<LandmarkSkyShape> shapes) ? shapes : None;
            return _biasShapes;
        }

        /// <summary>
        /// Appends (once each) the landmarks that may dissolve, or whose giant zone may reach, a cloud centred anywhere in the box
        /// [<paramref name="minX"/>, <paramref name="maxX"/>] × [<paramref name="minZ"/>, <paramref name="maxZ"/>]; blocking
        /// ones only when their top plus the margin is above <paramref name="lowestAltitude"/>.
        /// </summary>
        public void CollectNear(double minX, double minZ, double maxX, double maxZ, CloudLandmarkSettings settings, double lowestAltitude, List<LandmarkSkyShape> block, List<LandmarkSkyShape> bias) {
            (long lowX, long lowZ) = BinOf(minX, minZ);
            (long highX, long highZ) = BinOf(maxX, maxZ);
            for (long binZ = lowZ; binZ <= highZ; binZ++) {
                for (long binX = lowX; binX <= highX; binX++) {
                    if (_block.TryGetValue((binX, binZ), out List<LandmarkSkyShape> blocking)) AppendBlocking(blocking, block, minX, minZ, maxX, maxZ, settings, lowestAltitude);
                    if (_bias.TryGetValue((binX, binZ), out List<LandmarkSkyShape> giants)) AppendNew(giants, bias, double.PositiveInfinity, lowestAltitude);
                }
            }
        }

        // Blocking shapes not yet listed whose top stands above the altitude and whose reach (hull circle + cloud reach + dissolve band) meets the box.
        private void AppendBlocking(List<LandmarkSkyShape> from, List<LandmarkSkyShape> into, double minX, double minZ, double maxX, double maxZ, CloudLandmarkSettings settings, double lowestAltitude) {
            foreach (LandmarkSkyShape shape in from) {
                if (shape.PlateauY + settings.BlockMargin <= lowestAltitude || into.Contains(shape)) continue;

                double deltaX = shape.BoundX - Math.Max(minX, Math.Min(maxX, shape.BoundX));
                double deltaZ = shape.BoundZ - Math.Max(minZ, Math.Min(maxZ, shape.BoundZ));
                double reach = shape.BoundRadius + _maxReach + settings.DissolveFar;
                if (deltaX * deltaX + deltaZ * deltaZ <= reach * reach) into.Add(shape);
            }
        }

        // Shapes not already in the list whose top plus the margin is above the altitude.
        private static void AppendNew(List<LandmarkSkyShape> from, List<LandmarkSkyShape> into, double margin, double lowestAltitude) {
            foreach (LandmarkSkyShape shape in from) {
                if (shape.PlateauY + margin > lowestAltitude && !into.Contains(shape)) into.Add(shape);
            }
        }

        private static (long X, long Z) BinOf(double x, double z) {
            return ((long)Math.Floor(x / BinSize), (long)Math.Floor(z / BinSize));
        }

        private static void Bin(Dictionary<(long X, long Z), List<LandmarkSkyShape>> bins, LandmarkSkyShape shape, double x, double z, double reach) {
            (long minX, long minZ) = BinOf(x - reach, z - reach);
            (long maxX, long maxZ) = BinOf(x + reach, z + reach);
            for (long binZ = minZ; binZ <= maxZ; binZ++) {
                for (long binX = minX; binX <= maxX; binX++) Add(bins, (binX, binZ), shape);
            }
        }

        private static void Add(Dictionary<(long X, long Z), List<LandmarkSkyShape>> bins, (long X, long Z) bin, LandmarkSkyShape shape) {
            if (!bins.TryGetValue(bin, out List<LandmarkSkyShape> shapes)) {
                shapes = new List<LandmarkSkyShape>();
                bins.Add(bin, shapes);
            }
            shapes.Add(shape);
        }
    }
}
