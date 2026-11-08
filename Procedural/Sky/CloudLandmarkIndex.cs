using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The landmarks near a camera binned on a coarse grid, so a cloud only tests the few whose lift or giant zone can
    /// reach it: a landmark is listed for lift only where its top can reach any cloud's base, and for bias only if a giant.
    /// Built once per landmark query; results are the same as testing every landmark. Not thread safe: lookups remember
    /// the last bin (clouds come cell by cell, so neighbours share it), so use one index per thread.
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
        private double _maxReach;

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
            _maxReach = maxReach;
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

        /// <summary>
        /// Appends (once each) the landmarks whose lift or giant zone may reach a cloud centred anywhere in the box
        /// [<paramref name="minX"/>, <paramref name="maxX"/>] × [<paramref name="minZ"/>, <paramref name="maxZ"/>]; lifting
        /// ones only when their top plus the clearance clears <paramref name="lowestAltitude"/>.
        /// </summary>
        public void CollectNear(double minX, double minZ, double maxX, double maxZ, CloudLandmarkSettings settings, double lowestAltitude, List<LandmarkSkyShape> lift, List<LandmarkSkyShape> bias) {
            (long lowX, long lowZ) = BinOf(minX, minZ);
            (long highX, long highZ) = BinOf(maxX, maxZ);
            for (long binZ = lowZ; binZ <= highZ; binZ++) {
                for (long binX = lowX; binX <= highX; binX++) {
                    if (_lift.TryGetValue((binX, binZ), out List<LandmarkSkyShape> lifting)) AppendLifting(lifting, lift, minX, minZ, maxX, maxZ, settings, lowestAltitude);
                    if (_bias.TryGetValue((binX, binZ), out List<LandmarkSkyShape> giants)) AppendNew(giants, bias, double.PositiveInfinity, lowestAltitude);
                }
            }
        }

        // Lifting shapes not yet listed whose top clears the altitude and whose reach (hull circle + cloud reach + falloff) meets the box.
        private void AppendLifting(List<LandmarkSkyShape> from, List<LandmarkSkyShape> into, double minX, double minZ, double maxX, double maxZ, CloudLandmarkSettings settings, double lowestAltitude) {
            foreach (LandmarkSkyShape shape in from) {
                if (shape.PlateauY + settings.LiftClearance <= lowestAltitude || into.Contains(shape)) continue;

                double deltaX = shape.BoundX - Math.Max(minX, Math.Min(maxX, shape.BoundX));
                double deltaZ = shape.BoundZ - Math.Max(minZ, Math.Min(maxZ, shape.BoundZ));
                double reach = shape.BoundRadius + _maxReach + settings.LiftFalloff;
                if (deltaX * deltaX + deltaZ * deltaZ <= reach * reach) into.Add(shape);
            }
        }

        // Shapes not already in the list, and for lift only those whose top clears the altitude.
        private static void AppendNew(List<LandmarkSkyShape> from, List<LandmarkSkyShape> into, double clearance, double lowestAltitude) {
            foreach (LandmarkSkyShape shape in from) {
                if (shape.PlateauY + clearance > lowestAltitude && !into.Contains(shape)) into.Add(shape);
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
