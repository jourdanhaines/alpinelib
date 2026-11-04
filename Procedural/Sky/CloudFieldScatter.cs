using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Seeds clouds on the wind-frame lattice of a field: each cell's clouds depend only on the seed, the settings
    /// and the cell, so every client computes the same sky, and the whole lattice slides over the world with the
    /// wind's drift (see <see cref="CloudFieldFrame"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every cell draws <see cref="CloudFieldSettings.CandidatesPerCell"/> candidates, each from its own slot seed
    /// with a fixed set of draws, so no candidate's fate shifts another's values. A candidate is kept when the
    /// coverage noise keeps it and no higher-priority candidate of its own or a neighbouring cell lies within
    /// <see cref="CloudFieldSettings.AnchorSpacing"/>: a purely local rule, so cells build in any order.
    /// </para>
    /// <para>
    /// Anchors sit at least the wander amplitude inside their cell, and the wander (<see cref="Wander"/>) is
    /// bounded by it, so a cloud never leaves its cell and drawn clouds stay <see cref="CloudFieldSettings.MinSpacing"/> apart.
    /// Thread safe.
    /// </para>
    /// </remarks>
    public sealed class CloudFieldScatter {
        private const int Neighbourhood = 9;
        private const ulong CoverageSalt = 0x434F5645UL;
        private const ulong WanderXSalt = 0x57414E58UL;
        private const ulong WanderZSalt = 0x57414E5AUL;

        private readonly ulong _seed;
        private readonly ulong _coverageSeed;
        private readonly FbmSettings _coverageNoise;
        private readonly FbmSettings _wanderNoise;
        private readonly double _totalWeight;

        /// <summary>A scatter of <paramref name="seed"/> shaped by <paramref name="settings"/> (validated).</summary>
        public CloudFieldScatter(ulong seed, CloudFieldSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Validate();
            Settings = settings;
            _seed = seed;
            _coverageSeed = StableHash.Mix(seed, CoverageSalt);
            _coverageNoise = new FbmSettings(2, 1.0 / settings.CoveragePeriod);
            _wanderNoise = new FbmSettings(2, 1.0 / settings.WanderPeriodSeconds);
            foreach (CloudClassSettings cloudClass in settings.Classes) _totalWeight += cloudClass.Weight;
        }

        public CloudFieldSettings Settings { get; }

        public ulong Seed => _seed;

        /// <summary>The seed slot <paramref name="slot"/> of field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) draws from.</summary>
        public static ulong SlotSeed(ulong seed, long cellX, long cellZ, int slot) {
            return StableHash.Mix(StableHash.Mix(StableHash.Mix(seed, (ulong)cellX), (ulong)cellZ), (ulong)(uint)slot);
        }

        /// <summary>The clouds of field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>), in slot order.</summary>
        public CloudInstance[] Cell(long cellX, long cellZ) {
            var clouds = new List<CloudInstance>(Settings.CandidatesPerCell);
            AppendCell(cellX, cellZ, clouds);
            return clouds.ToArray();
        }

        /// <summary>Appends the clouds of field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) to <paramref name="clouds"/>, in slot order.</summary>
        public void AppendCell(long cellX, long cellZ, List<CloudInstance> clouds) {
            if (clouds == null) throw new ArgumentNullException(nameof(clouds));

            int perCell = Settings.CandidatesPerCell;
            var x = new double[Neighbourhood * perCell];
            var z = new double[x.Length];
            var priority = new ulong[x.Length];
            var isCovered = new bool[x.Length];
            for (int neighbour = 0; neighbour < Neighbourhood; neighbour++) {
                long otherX = cellX + neighbour % 3 - 1;
                long otherZ = cellZ + neighbour / 3 - 1;
                for (int slot = 0; slot < perCell; slot++) {
                    int index = neighbour * perCell + slot;
                    DrawPlace(otherX, otherZ, slot, out double localX, out double localZ, out priority[index], out double keepRoll);
                    x[index] = (neighbour % 3 - 1) * Settings.CellSize + localX;
                    z[index] = (neighbour / 3 - 1) * Settings.CellSize + localZ;
                    isCovered[index] = keepRoll < Coverage(otherX, otherZ, localX, localZ);
                }
            }

            int own = 4 * perCell;
            for (int slot = 0; slot < perCell; slot++) {
                if (IsKept(own + slot, x, z, priority, isCovered)) clouds.Add(CreateCloud(cellX, cellZ, slot));
            }
        }

        /// <summary>
        /// Chance a candidate anchored at <paramref name="localX"/>, <paramref name="localZ"/> into field cell
        /// (<paramref name="cellX"/>, <paramref name="cellZ"/>) is kept: low-frequency noise over the wind frame.
        /// </summary>
        public double Coverage(long cellX, long cellZ, double localX, double localZ) {
            double windX = cellX * Settings.CellSize + localX;
            double windZ = cellZ * Settings.CellSize + localZ;
            double noise = Fbm.Sample2(_coverageSeed, windX, windZ, _coverageNoise);
            double thickness = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.8 * noise));
            return Settings.MinCoverage + (1.0 - Settings.MinCoverage) * thickness;
        }

        /// <summary>
        /// The bounded wander of <paramref name="cloud"/> about its anchor at session <paramref name="time"/>, metres:
        /// two 1D fBm over time, held to <see cref="CloudFieldSettings.WanderAmplitude"/> in length.
        /// </summary>
        public void Wander(in CloudInstance cloud, double time, out double x, out double z) {
            double noiseX = Fbm.Sample1(StableHash.Mix(cloud.WanderSeed, WanderXSalt), time, _wanderNoise);
            double noiseZ = Fbm.Sample1(StableHash.Mix(cloud.WanderSeed, WanderZSalt), time, _wanderNoise);
            double length = Math.Sqrt(noiseX * noiseX + noiseZ * noiseZ);
            double scale = length > 1.0 ? Settings.WanderAmplitude / length : Settings.WanderAmplitude;
            x = noiseX * scale;
            z = noiseZ * scale;
        }

        /// <summary>
        /// Size share a cloud <paramref name="distance"/> metres (horizontal) from the camera is drawn at: 1 within
        /// <see cref="CloudFieldSettings.FullRadius"/>, smoothstepped to 0 at <see cref="CloudFieldSettings.WindowRadius"/>.
        /// </summary>
        public double Presence(double distance) {
            double full = Settings.FullRadius;
            if (distance <= full) return 1.0;

            double t = Math.Min(1.0, (distance - full) / (Settings.WindowRadius - full));
            return 1.0 - t * t * (3.0 - 2.0 * t);
        }

        // Kept unless out of coverage or crowded by a covered candidate of higher priority (ties broken by index).
        private bool IsKept(int index, double[] x, double[] z, ulong[] priority, bool[] isCovered) {
            if (!isCovered[index]) return false;

            double spacing = Settings.AnchorSpacing * Settings.AnchorSpacing;
            for (int other = 0; other < x.Length; other++) {
                if (other == index || !isCovered[other] || !Outranks(other, index, priority)) continue;

                double deltaX = x[other] - x[index];
                double deltaZ = z[other] - z[index];
                if (deltaX * deltaX + deltaZ * deltaZ < spacing) return false;
            }

            return true;
        }

        private static bool Outranks(int other, int index, ulong[] priority) {
            return priority[other] > priority[index] || (priority[other] == priority[index] && other < index);
        }

        // The first four draws of a slot: where it sits, its priority and its coverage roll.
        private void DrawPlace(long cellX, long cellZ, int slot, out double localX, out double localZ, out ulong priority, out double keepRoll) {
            var random = new SplitMix64(SlotSeed(_seed, cellX, cellZ, slot));
            ReadPlace(random, out localX, out localZ, out priority, out keepRoll);
        }

        private void ReadPlace(SplitMix64 random, out double localX, out double localZ, out ulong priority, out double keepRoll) {
            double span = Settings.CellSize - 2.0 * Settings.AnchorInset;
            localX = Settings.AnchorInset + random.NextDouble() * span;
            localZ = Settings.AnchorInset + random.NextDouble() * span;
            priority = random.NextULong();
            keepRoll = random.NextDouble();
        }

        // Every slot reads the same draws in the same order whatever it ends up as.
        private CloudInstance CreateCloud(long cellX, long cellZ, int slot) {
            var random = new SplitMix64(SlotSeed(_seed, cellX, cellZ, slot));
            ReadPlace(random, out double localX, out double localZ, out ulong _, out double _);
            CloudClass cloudClass = PickClass(random.NextDouble());
            int variant = random.NextInt(CloudFieldSettings.VariantsPerClass);
            double scale = Lerp(Settings.MinScale, Settings.MaxScale, random.NextDouble());
            double yaw = random.NextDouble() * 2.0 * Math.PI;
            CloudClassSettings settings = Settings.ClassOf(cloudClass);
            double altitude = Lerp(settings.MinAltitude, settings.MaxAltitude, random.NextDouble());
            ulong wanderSeed = random.NextULong();
            ulong seed = random.NextULong();
            return new CloudInstance(localX, localZ, altitude, cloudClass, variant, scale, yaw, wanderSeed, seed);
        }

        private CloudClass PickClass(double roll) {
            double pick = roll * _totalWeight;
            for (int index = 0; index < CloudFieldSettings.ClassCount - 1; index++) {
                pick -= Settings.Classes[index].Weight;
                if (pick < 0.0) return (CloudClass)index;
            }

            return (CloudClass)(CloudFieldSettings.ClassCount - 1);
        }

        private static double Lerp(double low, double high, double t) {
            return low + (high - low) * t;
        }
    }
}
