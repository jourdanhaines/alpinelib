using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The landmarks near a camera binned on a coarse grid, so a cloud only tests the few whose lift or giant zone can
    /// reach it: a landmark is listed for lift only where its top can reach any cloud's base, and for bias only if a giant.
    /// Built once per landmark query; results are the same as testing every landmark. Remembers the last bin of each
    /// lookup (clouds come cell by cell, so neighbours share it): one index per thread.
    /// </summary>
    public sealed class CloudLandmarkIndex {
        /// <summary>Edge of one bin, metres.</summary>
        public const double BinSize = 256.0;

        private static readonly List<LandmarkSkyShape> None = new List<LandmarkSkyShape>();

        private readonly Dictionary<(long X, long Z), List<LandmarkSkyShape>> _lift = new Dictionary<(long X, long Z), List<LandmarkSkyShape>>();
        private readonly Dictionary<(long X, long Z), List<LandmarkSkyShape>> _bias = new Dictionary<(long X, long Z), List<LandmarkSkyShape>>();

        private (long X, long Z) _liftBin = (long.MinValue, long.MinValue);
        private (long X, long Z) _biasBin = (long.MinValue, long.MinValue);
        private IReadOnlyList<LandmarkSkyShape> _liftShapes = None;
        private IReadOnlyList<LandmarkSkyShape> _biasShapes = None;

        /// <summary>Landmarks indexed (after the altitude cut and giant filter: any listed once or more).</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Rebuilds from <paramref name="shapes"/> for clouds of footprint radius up to <paramref name="maxReach"/> whose
        /// bases sit at <paramref name="lowestAltitude"/> or above.
        /// </summary>
        public void Build(IReadOnlyList<LandmarkSkyShape> shapes, CloudLandmarkSettings settings, double maxReach, double lowestAltitude) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _lift.Clear();
            _bias.Clear();
            Count = 0;
            _liftBin = (long.MinValue, long.MinValue);
            _biasBin = (long.MinValue, long.MinValue);
            _liftShapes = None;
            _biasShapes = None;
            if (shapes == null) return;

            double biasReach = settings.BiasDownwind + settings.BiasRadius + settings.BiasFade;
            foreach (LandmarkSkyShape shape in shapes) {
                bool isLifting = shape.PlateauY + settings.LiftClearance > lowestAltitude;
                bool isGiant = shape.Tier == LandmarkSkyTier.Giant;
                if (isLifting) Bin(_lift, shape, shape.BoundX, shape.BoundZ, shape.BoundRadius + maxReach + settings.LiftFalloff);
                if (isGiant) Bin(_bias, shape, shape.AnchorX, shape.AnchorZ, biasReach);
                Count += isLifting || isGiant ? 1 : 0;
            }
        }

        /// <summary>The landmarks whose lift may reach a cloud centred at (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public IReadOnlyList<LandmarkSkyShape> LiftShapesAt(double x, double z) {
            (long X, long Z) bin = BinOf(x, z);
            if (bin == _liftBin) return _liftShapes;

            _liftBin = bin;
            _liftShapes = _lift.TryGetValue(bin, out List<LandmarkSkyShape> shapes) ? shapes : None;
            return _liftShapes;
        }

        /// <summary>The giants whose zone may reach (<paramref name="x"/>, <paramref name="z"/>) under any wind heading.</summary>
        public IReadOnlyList<LandmarkSkyShape> BiasShapesAt(double x, double z) {
            (long X, long Z) bin = BinOf(x, z);
            if (bin == _biasBin) return _biasShapes;

            _biasBin = bin;
            _biasShapes = _bias.TryGetValue(bin, out List<LandmarkSkyShape> shapes) ? shapes : None;
            return _biasShapes;
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
