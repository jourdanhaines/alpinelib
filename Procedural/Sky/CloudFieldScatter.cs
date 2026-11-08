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
    /// cloud never leaves its cell and drawn clouds of one class never touch. The field draws the offset sampled at
    /// <see cref="MotionTickSeconds"/> ticks and blended between them (<see cref="SmoothOffset"/>), which keeps both
    /// bounds. Thread safe.
    /// </para>
    /// </remarks>
    public sealed class CloudFieldScatter {
        private const int Neighbourhood = 9;
        private const int WideNeighbourhood = 25;
        private const ulong CoverageSalt = 0x434F5645UL;
        private const ulong WanderXSalt = 0x57414E58UL;
        private const ulong WanderZSalt = 0x57414E5AUL;
        private const ulong SurgeSalt = 0x53555247UL;

        /// <summary>Gain on the surge noise before it is clamped to ±1, so a cloud spends its time across the whole swing.</summary>
        public const double SurgeGain = 2.0;

        /// <summary>Step of the motion ticks the wander and surge are sampled at and blended between, seconds (they move millimetres a frame; the blend is within millimetres of the curve).</summary>
        public const double MotionTickSeconds = 0.25;

        /// <summary>Steps per metre (or unit) values from libm are rounded to, so every runtime gets the same bits.</summary>
        public const double QuantumSteps = 1024.0;

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
        [ThreadStatic] private static bool[] _candidateKept;

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
            if (Settings.Landmarks != null) {
                AppendLandmarkCell(cloudClass, classSettings, cellX, cellZ, clouds);
                return;
            }

            int perCell = classSettings.CandidatesPerCell;
            int candidates = Neighbourhood * perCell;
            EnsureScratch(candidates);
            for (int neighbour = 0; neighbour < Neighbourhood; neighbour++) {
                int stepX = neighbour % 3 - 1;
                int stepZ = neighbour / 3 - 1;
                for (int slot = 0; slot < perCell; slot++) DrawCandidate(cloudClass, classSettings, cellX + stepX, cellZ + stepZ, stepX, stepZ, slot, neighbour * perCell + slot, 1.0);
            }

            int own = 4 * perCell;
            for (int slot = 0; slot < perCell; slot++) {
                if (IsKept(classSettings, own + slot, candidates)) clouds.Add(CreateCloud(cloudClass, classSettings, cellX, cellZ, slot));
            }
        }

        // A candidate stepX, stepZ cells from the built one, its reach grown by reachScale (the boost cap with landmarks).
        private void DrawCandidate(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int stepX, int stepZ, int slot, int index, double reachScale) {
            var random = new SplitMix64(SlotSeed(_seed, cloudClass, cellX, cellZ, slot));
            ReadPlace(random, classSettings, out double localX, out double localZ, out _candidatePriority[index], out double keepRoll, out int variant, out double scale);
            _candidateX[index] = stepX * Settings.CellSize + localX;
            _candidateZ[index] = stepZ * Settings.CellSize + localZ;
            _candidateReach[index] = CloudFieldSettings.ReachOf(MeshDiameter(cloudClass, variant), scale) * reachScale;
            _candidateCovered[index] = keepRoll < Coverage(cloudClass, cellX, cellZ, localX, localZ);
        }

        private static void EnsureScratch(int candidates) {
            if (_candidateX != null && _candidateX.Length >= candidates) return;

            _candidateX = new double[candidates];
            _candidateZ = new double[candidates];
            _candidateReach = new double[candidates];
            _candidatePriority = new ulong[candidates];
            _candidateCovered = new bool[candidates];
            _candidateKept = new bool[candidates];
        }

        // With landmarks: the regular clouds of the 3 x 3 about the cell are settled first (their own 3 x 3, so a 5 x 5
        // of candidates, every reach grown by the boost cap), then latent candidates of the 3 x 3 are kept where no kept
        // regular cloud and no higher-priority latent one crowds them. Regular clouds come first, then latent, each in slot order.
        private void AppendLandmarkCell(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, List<CloudInstance> clouds) {
            int perCell = classSettings.CandidatesPerCell;
            int latentPerCell = Settings.Landmarks.LatentOf(cloudClass);
            int regular = WideNeighbourhood * perCell;
            int total = regular + Neighbourhood * latentPerCell;
            double reachScale = Settings.BoostCap(classSettings);
            EnsureScratch(total);
            for (int neighbour = 0; neighbour < WideNeighbourhood; neighbour++) {
                int stepX = neighbour % 5 - 2;
                int stepZ = neighbour / 5 - 2;
                for (int slot = 0; slot < perCell; slot++) DrawCandidate(cloudClass, classSettings, cellX + stepX, cellZ + stepZ, stepX, stepZ, slot, neighbour * perCell + slot, reachScale);
            }
            SettleRegular(classSettings, perCell, regular);
            DrawLatent(cloudClass, classSettings, cellX, cellZ, perCell, latentPerCell, regular, reachScale);

            int own = 12 * perCell;
            for (int slot = 0; slot < perCell; slot++) {
                if (_candidateKept[own + slot]) clouds.Add(CreateCloud(cloudClass, classSettings, cellX, cellZ, slot));
            }

            int ownLatent = regular + 4 * latentPerCell;
            for (int latent = 0; latent < latentPerCell; latent++) {
                if (IsLatentKept(classSettings, ownLatent + latent, perCell, regular, total)) clouds.Add(CreateLatentCloud(cloudClass, classSettings, cellX, cellZ, perCell + latent));
            }
        }

        // Kept flags of the regular candidates of the inner 3 x 3 (indices of the 5 x 5); the outer ring is only ever crowding.
        private void SettleRegular(CloudClassSettings classSettings, int perCell, int regular) {
            for (int neighbour = 0; neighbour < WideNeighbourhood; neighbour++) {
                bool isInner = IsInner(neighbour);
                for (int slot = 0; slot < perCell; slot++) {
                    int index = neighbour * perCell + slot;
                    _candidateKept[index] = isInner && IsKept(classSettings, index, regular);
                }
            }
        }

        private static bool IsInner(int neighbour) {
            int stepX = neighbour % 5 - 2;
            int stepZ = neighbour / 5 - 2;
            return stepX >= -1 && stepX <= 1 && stepZ >= -1 && stepZ <= 1;
        }

        // Latent slots follow the regular ones in the slot seeds, so they never shift a regular cloud's draws.
        private void DrawLatent(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int perCell, int latentPerCell, int regular, double reachScale) {
            for (int neighbour = 0; neighbour < Neighbourhood; neighbour++) {
                int stepX = neighbour % 3 - 1;
                int stepZ = neighbour / 3 - 1;
                for (int latent = 0; latent < latentPerCell; latent++) {
                    int index = regular + neighbour * latentPerCell + latent;
                    DrawCandidate(cloudClass, classSettings, cellX + stepX, cellZ + stepZ, stepX, stepZ, perCell + latent, index, reachScale);
                    _candidateCovered[index] = true;
                }
            }
        }

        private bool IsLatentKept(CloudClassSettings classSettings, int index, int perCell, int regular, int total) {
            for (int other = 0; other < regular; other++) {
                if (_candidateKept[other] && IsCrowding(classSettings, other, index)) return false;
            }
            for (int other = regular; other < total; other++) {
                if (other != index && Outranks(other, index) && IsCrowding(classSettings, other, index)) return false;
            }
            return true;
        }

        /// <summary>
        /// Share of its size <paramref name="cloud"/> is drawn at under giant bias <paramref name="bias"/>: 1 for a
        /// regular cloud; a latent one condenses from 0 to 1 as its density (coverage + bias × class weight) climbs the
        /// last <see cref="CloudLandmarkSettings.EnvelopeWidth"/> to its threshold.
        /// </summary>
        public double Envelope(in CloudInstance cloud, double bias) {
            if (!cloud.IsLatent) return 1.0;

            CloudLandmarkSettings landmarks = Settings.Landmarks;
            if (landmarks == null) return 0.0;

            double density = cloud.BaseDensity + bias * landmarks.WeightOf(cloud.Class);
            return CloudLandmarkInfluence.SmoothStep(cloud.Threshold - landmarks.EnvelopeWidth, cloud.Threshold, density);
        }

        /// <summary>Scale factor a cloud of <paramref name="cloudClass"/> grows by under giant bias <paramref name="bias"/>, held at its <see cref="CloudFieldSettings.BoostCap"/>.</summary>
        public double SizeBoost(CloudClass cloudClass, double bias) {
            CloudLandmarkSettings landmarks = Settings.Landmarks;
            if (landmarks == null || !(bias > 0.0)) return 1.0;

            return Math.Min(1.0 + landmarks.SizeBoost * bias, Settings.BoostCap(Settings.ClassOf(cloudClass)));
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

        /// <summary>The motion tick <paramref name="time"/> falls in: the wander and surge blend from it to the next.</summary>
        public static long MotionTick(double time) {
            return (long)Math.Floor(time / MotionTickSeconds);
        }

        /// <summary>Session time of motion tick <paramref name="tick"/>, seconds.</summary>
        public static double TickTime(long tick) {
            return tick * MotionTickSeconds;
        }

        /// <summary>How far <paramref name="time"/> is from motion tick <paramref name="tick"/> to the next, [0, 1).</summary>
        public static double TickShare(double time, long tick) {
            return Math.Min(1.0, Math.Max(0.0, time / MotionTickSeconds - tick));
        }

        /// <summary>Linear blend of two tick offsets; convex, so the blend keeps within the wander and surge bounds.</summary>
        public static double Blend(double from, double to, double share) {
            return from + (to - from) * share;
        }

        /// <summary><paramref name="value"/> rounded to 1/<see cref="QuantumSteps"/>.</summary>
        public static double Quantise(double value) {
            return Math.Round(value * QuantumSteps) / QuantumSteps;
        }

        /// <summary><see cref="Offset"/> at motion tick <paramref name="tick"/>, under the wind heading of that tick (<paramref name="heading"/>).</summary>
        public void OffsetAtTick(in CloudInstance cloud, long tick, WindVector heading, out double offsetX, out double offsetZ) {
            Offset(cloud, TickTime(tick), heading.X, heading.Z, out offsetX, out offsetZ);
        }

        /// <summary>
        /// Where the field draws <paramref name="cloud"/>'s centre from its anchor at <paramref name="time"/>, metres: its
        /// offset at the two motion ticks about the time (headings from <paramref name="wind"/>, none when null), blended.
        /// </summary>
        public void SmoothOffset(in CloudInstance cloud, double time, WindField wind, out double offsetX, out double offsetZ) {
            long tick = MotionTick(time);
            OffsetAtTick(cloud, tick, HeadingAt(wind, tick), out double fromX, out double fromZ);
            OffsetAtTick(cloud, tick + 1, HeadingAt(wind, tick + 1), out double toX, out double toZ);
            double share = TickShare(time, tick);
            offsetX = Blend(fromX, toX, share);
            offsetZ = Blend(fromZ, toZ, share);
        }

        /// <summary>The unit wind heading at motion tick <paramref name="tick"/>, or none without a wind.</summary>
        public static WindVector HeadingAt(WindField wind, long tick) {
            return wind != null ? wind.Direction(TickTime(tick)) : WindVector.Zero;
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

        // The first draws of a slot: where it sits, its priority, its coverage roll, its mesh and its scale (log-uniform, quantised).
        private void ReadPlace(SplitMix64 random, CloudClassSettings classSettings, out double localX, out double localZ, out ulong priority, out double keepRoll, out int variant, out double scale) {
            double inset = Settings.AnchorInset(classSettings);
            double span = Settings.CellSize - 2.0 * inset;
            localX = inset + random.NextDouble() * span;
            localZ = inset + random.NextDouble() * span;
            priority = random.NextULong();
            keepRoll = random.NextDouble();
            variant = random.NextInt(CloudFieldSettings.VariantsPerClass);
            scale = Math.Max(Settings.MinScale, Math.Min(Settings.MaxScale, Quantise(Math.Exp(_logMinScale + _logScaleSpan * random.NextDouble()))));
        }

        // Every slot reads the same draws in the same order whatever it ends up as.
        private CloudInstance CreateCloud(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int slot) {
            var random = new SplitMix64(SlotSeed(_seed, cloudClass, cellX, cellZ, slot));
            ReadPlace(random, classSettings, out double localX, out double localZ, out ulong _, out double keepRoll, out int variant, out double scale);
            double yaw = random.NextDouble() * 2.0 * Math.PI;
            double altitude = Lerp(classSettings.MinAltitude, classSettings.MaxAltitude, random.NextDouble());
            ulong wanderSeed = random.NextULong();
            ulong seed = random.NextULong();
            double reach = CloudFieldSettings.ReachOf(MeshDiameter(cloudClass, variant), scale);
            if (Settings.Landmarks == null) return new CloudInstance(localX, localZ, altitude, cloudClass, variant, scale, reach, yaw, wanderSeed, seed);

            double coverage = Coverage(cloudClass, cellX, cellZ, localX, localZ);
            return new CloudInstance(localX, localZ, altitude, cloudClass, variant, scale, reach, yaw, wanderSeed, seed, false, keepRoll, coverage);
        }

        // A latent slot's keep roll places its threshold in (1 + envelope width, 1 + giant bias]: above any coverage alone.
        private CloudInstance CreateLatentCloud(CloudClass cloudClass, CloudClassSettings classSettings, long cellX, long cellZ, int slot) {
            CloudInstance cloud = CreateCloud(cloudClass, classSettings, cellX, cellZ, slot);
            CloudLandmarkSettings landmarks = Settings.Landmarks;
            double threshold = 1.0 + landmarks.EnvelopeWidth + cloud.Threshold * (landmarks.GiantBias - landmarks.EnvelopeWidth);
            return new CloudInstance(cloud.AnchorX, cloud.AnchorZ, cloud.Altitude, cloudClass, cloud.Variant, cloud.Scale, cloud.Reach, cloud.Yaw, cloud.WanderSeed, cloud.Seed, true, threshold, cloud.BaseDensity);
        }

        private static double Lerp(double low, double high, double share) {
            return low + (high - low) * share;
        }
    }
}
