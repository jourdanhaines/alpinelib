using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Noise;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Seeds clouds on the wind-frame lattices of a field, one lattice per <see cref="CloudClass"/>: each cell's clouds
    /// depend only on the seed, the settings, the class and the cell, so every client computes the same sky, and each
    /// class's lattice slides over the world by its own share of the wind's drift (<see cref="DriftOf"/>,
    /// <see cref="CloudFieldFrame"/>), so big clouds lag the air and small ones run ahead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every cell draws its class's candidates, each from its own slot seed with a fixed set of draws, so no
    /// candidate's fate shifts another's values. A candidate is kept when the coverage noise keeps it and no
    /// higher-priority candidate of its own or a neighbouring cell lies within their <see cref="CloudFieldSettings.Exclusion"/>
    /// (both drawn radii, the gap and both clouds' motion): a purely local rule, so cells build in any order.
    /// </para>
    /// <para>
    /// A cloud's centre sits at its anchor plus a bounded wander (<see cref="Wander"/>) plus a bounded surge along the
    /// wind (<see cref="Surge"/>, its own speed variance); anchors sit at least both amplitudes inside their cell, so a
    /// cloud never leaves its cell and drawn clouds of one class never touch. Thread safe.
    /// </para>
    /// </remarks>
    public sealed class CloudFieldScatter {
        private const int Neighbourhood = 9;
        private const ulong CoverageSalt = 0x434F5645UL;
        private const ulong WanderXSalt = 0x57414E58UL;
        private const ulong WanderZSalt = 0x57414E5AUL;
        private const ulong SurgeSalt = 0x53555247UL;

        /// <summary>Gain on the surge noise before it is clamped to ±1, so a cloud spends its time across the whole swing.</summary>
        public const double SurgeGain = 2.0;

        private readonly ulong _seed;
        private readonly ulong[] _coverageSeeds = new ulong[CloudFieldSettings.ClassCount];
        private readonly FbmSettings _coverageNoise;
        private readonly FbmSettings _wanderNoise;
        private readonly FbmSettings _surgeNoise;
        private readonly double _logMinScale;
        private readonly double _logScaleSpan;

        // Per-thread candidate scratch, so building a cell allocates nothing once it has grown.
        [ThreadStatic] private static double[] _candidateX;
        [ThreadStatic] private static double[] _candidateZ;
        [ThreadStatic] private static double[] _candidateReach;
        [ThreadStatic] private static ulong[] _candidatePriority;
        [ThreadStatic] private static bool[] _candidateCovered;

        /// <summary>A scatter of <paramref name="seed"/> shaped by <paramref name="settings"/> (validated).</summary>
        public CloudFieldScatter(ulong seed, CloudFieldSettings settings) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Validate();
            Settings = settings;
            _seed = seed;
            for (int index = 0; index < _coverageSeeds.Length; index++) _coverageSeeds[index] = StableHash.Mix(StableHash.Mix(seed, CoverageSalt), (ulong)index);
            _coverageNoise = new FbmSettings(2, 1.0 / settings.CoveragePeriod);
            _wanderNoise = new FbmSettings(2, 1.0 / settings.WanderPeriodSeconds);
            _surgeNoise = new FbmSettings(2, 1.0 / settings.SurgePeriodSeconds);
            _logMinScale = Math.Log(settings.MinScale);
            _logScaleSpan = Math.Log(settings.MaxScale) - _logMinScale;
        }

        public CloudFieldSettings Settings { get; }

        public ulong Seed => _seed;

        /// <summary>The seed slot <paramref name="slot"/> of <paramref name="cloudClass"/>'s field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) draws from.</summary>
        public static ulong SlotSeed(ulong seed, CloudClass cloudClass, long cellX, long cellZ, int slot) {
            ulong classSeed = StableHash.Mix(seed, (ulong)cloudClass);
            return StableHash.Mix(StableHash.Mix(StableHash.Mix(classSeed, (ulong)cellX), (ulong)cellZ), (ulong)(uint)slot);
        }

        /// <summary>How far <paramref name="cloudClass"/>'s lattice has slid when the air moved by <paramref name="displacement"/>: its drift factor of it, in double.</summary>
        public WindVector DriftOf(CloudClass cloudClass, WindVector displacement) {
            return displacement * Settings.ClassOf(cloudClass).DriftFactor;
        }

        /// <summary>Mesh footprint diameter of variant <paramref name="variant"/> of <paramref name="cloudClass"/>, metres (see <see cref="CloudHeap.TargetDiameter"/>).</summary>
        public double MeshDiameter(CloudClass cloudClass, int variant) {
            return CloudHeap.TargetDiameter(Settings.ClassOf(cloudClass), variant);
        }

        /// <summary>The clouds of <paramref name="cloudClass"/>'s field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>), in slot order.</summary>
        public CloudInstance[] Cell(CloudClass cloudClass, long cellX, long cellZ) {
            var clouds = new List<CloudInstance>(Settings.ClassOf(cloudClass).CandidatesPerCell);
            AppendCell(cloudClass, cellX, cellZ, clouds);
            return clouds.ToArray();
        }

        /// <summary>Appends the clouds of <paramref name="cloudClass"/>'s field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) to <paramref name="clouds"/>, in slot order.</summary>
        public void AppendCell(CloudClass cloudClass, long cellX, long cellZ, List<CloudInstance> clouds) {
            if (clouds == null) throw new ArgumentNullException(nameof(clouds));

            CloudClassSettings classSettings = Settings.ClassOf(cloudClass);
            int perCell = classSettings.CandidatesPerCell;
            int candidates = Neighbourhood * perCell;
            EnsureScratch(candidates);
            for (int neighbour = 0; neighbour < Neighbourhood; neighbour++) {
                long otherX = cellX + neighbour % 3 - 1;
                long otherZ = cellZ + neighbour / 3 - 1;
                for (int slot = 0; slot < perCell; slot++) DrawCandidate(cloudClass, classSettings, otherX, otherZ, neighbour, slot, neighbour * perCell + slot);
            }

            int own = 4 * perCell;
            for (int slot = 0; slot < perCell; slot++) {
                if (IsKept(classSettings, own + slot, candidates)) clouds.Add(CreateCloud(cloudClass, classSettings, cellX, cellZ, slot));
            }
        }

        private void DrawCandidate(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int neighbour, int slot, int index) {
            var random = new SplitMix64(SlotSeed(_seed, cloudClass, cellX, cellZ, slot));
            ReadPlace(random, classSettings, out double localX, out double localZ, out _candidatePriority[index], out double keepRoll, out int variant, out double scale);
            _candidateX[index] = (neighbour % 3 - 1) * Settings.CellSize + localX;
            _candidateZ[index] = (neighbour / 3 - 1) * Settings.CellSize + localZ;
            _candidateReach[index] = CloudFieldSettings.ReachOf(MeshDiameter(cloudClass, variant), scale);
            _candidateCovered[index] = keepRoll < Coverage(cloudClass, cellX, cellZ, localX, localZ);
        }

        private static void EnsureScratch(int candidates) {
            if (_candidateX != null && _candidateX.Length >= candidates) return;

            _candidateX = new double[candidates];
            _candidateZ = new double[candidates];
            _candidateReach = new double[candidates];
            _candidatePriority = new ulong[candidates];
            _candidateCovered = new bool[candidates];
        }

        /// <summary>
        /// Chance a candidate of <paramref name="cloudClass"/> anchored at <paramref name="localX"/>, <paramref name="localZ"/>
        /// into field cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) is kept: low-frequency noise over its wind frame.
        /// </summary>
        public double Coverage(CloudClass cloudClass, long cellX, long cellZ, double localX, double localZ) {
            double windX = cellX * Settings.CellSize + localX;
            double windZ = cellZ * Settings.CellSize + localZ;
            double noise = Fbm.Sample2(_coverageSeeds[(int)cloudClass], windX, windZ, _coverageNoise);
            double thickness = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.8 * noise));
            return Settings.MinCoverage + (1.0 - Settings.MinCoverage) * thickness;
        }

        /// <summary>
        /// The bounded wander of <paramref name="cloud"/> about its anchor at session <paramref name="time"/>, metres:
        /// two 1D fBm over time, held to its class's wander amplitude in length.
        /// </summary>
        public void Wander(in CloudInstance cloud, double time, out double wanderX, out double wanderZ) {
            double amplitude = Settings.ClassOf(cloud.Class).WanderAmplitude;
            double noiseX = Fbm.Sample1(StableHash.Mix(cloud.WanderSeed, WanderXSalt), time, _wanderNoise);
            double noiseZ = Fbm.Sample1(StableHash.Mix(cloud.WanderSeed, WanderZSalt), time, _wanderNoise);
            double length = Math.Sqrt(noiseX * noiseX + noiseZ * noiseZ);
            double scale = length > 1.0 ? amplitude / length : amplitude;
            wanderX = noiseX * scale;
            wanderZ = noiseZ * scale;
        }

        /// <summary>
        /// How far <paramref name="cloud"/> runs ahead of (positive) or lags (negative) its lattice along the wind at
        /// session <paramref name="time"/>, metres: slow 1D fBm, clamped to its class's surge amplitude.
        /// </summary>
        public double Surge(in CloudInstance cloud, double time) {
            double amplitude = Settings.ClassOf(cloud.Class).SurgeAmplitude;
            double noise = Fbm.Sample1(StableHash.Mix(cloud.WanderSeed, SurgeSalt), time, _surgeNoise) * SurgeGain;
            return amplitude * Math.Max(-1.0, Math.Min(1.0, noise));
        }

        /// <summary>
        /// Where <paramref name="cloud"/>'s centre sits from its anchor at <paramref name="time"/>, metres: its wander plus its
        /// surge along the unit wind heading (<paramref name="headingX"/>, <paramref name="headingZ"/>).
        /// </summary>
        public void Offset(in CloudInstance cloud, double time, double headingX, double headingZ, out double offsetX, out double offsetZ) {
            Wander(cloud, time, out offsetX, out offsetZ);
            double surge = Surge(cloud, time);
            offsetX += surge * headingX;
            offsetZ += surge * headingZ;
        }

        /// <summary>Size share a cloud <paramref name="distance"/> metres (horizontal) from the camera is drawn at (<see cref="CloudFieldSettings.Presence"/>).</summary>
        public double Presence(double distance) {
            return Settings.Presence(distance);
        }

        // Kept unless out of coverage or crowded by a covered candidate of higher priority (ties broken by index).
        private bool IsKept(CloudClassSettings classSettings, int index, int candidates) {
            if (!_candidateCovered[index]) return false;

            for (int other = 0; other < candidates; other++) {
                if (other == index || !_candidateCovered[other] || !Outranks(other, index)) continue;
                if (IsCrowding(classSettings, other, index)) return false;
            }

            return true;
        }

        private bool IsCrowding(CloudClassSettings classSettings, int other, int index) {
            double exclusion = Settings.Exclusion(classSettings, _candidateReach[other], _candidateReach[index]);
            double deltaX = _candidateX[other] - _candidateX[index];
            double deltaZ = _candidateZ[other] - _candidateZ[index];
            return deltaX * deltaX + deltaZ * deltaZ < exclusion * exclusion;
        }

        private static bool Outranks(int other, int index) {
            return _candidatePriority[other] > _candidatePriority[index] || (_candidatePriority[other] == _candidatePriority[index] && other < index);
        }

        // The first draws of a slot: where it sits, its priority, its coverage roll, its mesh and its scale (log-uniform).
        private void ReadPlace(SplitMix64 random, CloudClassSettings classSettings, out double localX, out double localZ, out ulong priority, out double keepRoll, out int variant, out double scale) {
            double inset = Settings.AnchorInset(classSettings);
            double span = Settings.CellSize - 2.0 * inset;
            localX = inset + random.NextDouble() * span;
            localZ = inset + random.NextDouble() * span;
            priority = random.NextULong();
            keepRoll = random.NextDouble();
            variant = random.NextInt(CloudFieldSettings.VariantsPerClass);
            scale = Math.Exp(_logMinScale + _logScaleSpan * random.NextDouble());
        }

        // Every slot reads the same draws in the same order whatever it ends up as.
        private CloudInstance CreateCloud(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int slot) {
            var random = new SplitMix64(SlotSeed(_seed, cloudClass, cellX, cellZ, slot));
            ReadPlace(random, classSettings, out double localX, out double localZ, out ulong _, out double _, out int variant, out double scale);
            double yaw = random.NextDouble() * 2.0 * Math.PI;
            double altitude = Lerp(classSettings.MinAltitude, classSettings.MaxAltitude, random.NextDouble());
            ulong wanderSeed = random.NextULong();
            ulong seed = random.NextULong();
            double reach = CloudFieldSettings.ReachOf(MeshDiameter(cloudClass, variant), scale);
            return new CloudInstance(localX, localZ, altitude, cloudClass, variant, scale, reach, yaw, wanderSeed, seed);
        }

        private static double Lerp(double low, double high, double share) {
            return low + (high - low) * share;
        }
    }
}
