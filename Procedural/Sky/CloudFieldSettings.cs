using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of the drifting cloud field: the wind-frame lattices clouds are seeded on (one per class, each sliding
    /// by its own share of the drift), how densely, how they wander and surge about their anchors, the window drawn
    /// around a camera and the classes they come in.
    /// </summary>
    /// <remarks>
    /// A cloud is shown at full size within (1 − <see cref="DissolveShare"/>) of <see cref="WindowRadius"/> and
    /// condenses or evaporates across the rest, past the fog's reach, so the field never dissolves in view. Classes
    /// drift at different speeds and so pass each other; their altitude bands overlap as in a real sky, so two clouds
    /// of different classes may run through each other for a while as one overtakes the other.
    /// </remarks>
    public sealed class CloudFieldSettings {
        /// <summary>Mesh variants per class.</summary>
        public const int VariantsPerClass = 4;

        /// <summary>Classes of <see cref="CloudClass"/>.</summary>
        public const int ClassCount = 3;

        /// <summary>Distinct cloud meshes the field draws.</summary>
        public const int MeshCount = VariantsPerClass * ClassCount;

        /// <summary>Most share of a field cell a cloud's surge may swing across (peak to peak, so over any window of time).</summary>
        public const double MaxSurgeShare = 0.15;

        /// <summary>Edge of one field cell of each wind-frame lattice, metres.</summary>
        public double CellSize { get; set; } = 256.0;

        /// <summary>Field cells from the camera's cell to the window's edge (4 = a 9 × 9 window per class).</summary>
        public int WindowCells { get; set; } = 4;

        /// <summary>Least gap between the footprints of two drawn clouds of one class at any time, metres (wander and surge included).</summary>
        public double MinGap { get; set; } = 6.0;

        /// <summary>Time scale of the wander, seconds.</summary>
        public double WanderPeriodSeconds { get; set; } = 150.0;

        /// <summary>Time scale of the along-wind surge, seconds: how long a cloud keeps running ahead or lagging.</summary>
        public double SurgePeriodSeconds { get; set; } = 150.0;

        /// <summary>Outer share of the window radius over which clouds condense in and evaporate out.</summary>
        public double DissolveShare { get; set; } = 0.15;

        /// <summary>Horizontal distance past which a cloud draws its coarse mesh (same puffs, fewer facets), metres; infinite = never.</summary>
        public double FarMeshRadius { get; set; } = double.PositiveInfinity;

        /// <summary>Half-width of the band about <see cref="FarMeshRadius"/> a cloud must cross before it swaps mesh, metres.</summary>
        public double FarMeshHysteresis { get; set; } = 50.0;

        /// <summary>Least chance a candidate is kept, where the coverage noise is thinnest (1 everywhere at its thickest).</summary>
        public double MinCoverage { get; set; } = 0.45;

        /// <summary>Wavelength of the coverage noise over the wind frame, metres: clear and crowded stretches of sky.</summary>
        public double CoveragePeriod { get; set; } = 1800.0;

        /// <summary>Instance scale range on top of a class's mesh diameters, drawn log-uniform.</summary>
        public double MinScale { get; set; } = 0.6;
        public double MaxScale { get; set; } = 1.6;

        /// <summary>The classes, indexed by <see cref="CloudClass"/>.</summary>
        public CloudClassSettings[] Classes { get; set; } = DefaultClasses();

        /// <summary>How clouds react to landmarks (lift, giant clustering, latent clouds); null = a field that ignores them.</summary>
        public CloudLandmarkSettings Landmarks { get; set; }

        /// <summary>The shipped field.</summary>
        public static CloudFieldSettings Default => new CloudFieldSettings();

        /// <summary>Horizontal distance from the camera at which clouds have evaporated, metres.</summary>
        public double WindowRadius => WindowCells * CellSize;

        /// <summary>Horizontal distance from the camera within which clouds are full size, metres.</summary>
        public double FullRadius => WindowRadius * (1.0 - DissolveShare);

        /// <summary>
        /// Far clip a camera on the ground needs to see every drawn cloud whole, dissolving ones too: a cloud centred
        /// <c>d</c> out is drawn at <see cref="Presence"/>(d) of its size, reaching its footprint radius further out and
        /// its height above its altitude.
        /// </summary>
        public double RequiredFarClip {
            get {
                double required = 0.0;
                foreach (CloudClassSettings cloudClass in Classes) required = Math.Max(required, FarClipOf(cloudClass));
                return required;
            }
        }

        /// <summary>The settings of <paramref name="cloudClass"/>.</summary>
        public CloudClassSettings ClassOf(CloudClass cloudClass) {
            return Classes[(int)cloudClass];
        }

        /// <summary>The class drawn by mesh <paramref name="meshIndex"/>.</summary>
        public static CloudClass ClassOfMesh(int meshIndex) {
            return (CloudClass)(meshIndex / VariantsPerClass);
        }

        /// <summary>Margin kept between an anchor of <paramref name="cloudClass"/> and its cell's edges, metres: its wander plus surge.</summary>
        public double AnchorInset(CloudClassSettings cloudClass) {
            return cloudClass.MotionReach;
        }

        /// <summary>Horizontal radius of a drawn cloud from a mesh <paramref name="diameter"/> across at <paramref name="scale"/>, metres.</summary>
        public static double ReachOf(double diameter, double scale) {
            return diameter * scale * CloudHeap.ReachShare;
        }

        /// <summary>Largest drawn footprint radius of <paramref name="cloudClass"/>, metres.</summary>
        public double MaxReach(CloudClassSettings cloudClass) {
            return ReachOf(cloudClass.MaxDiameter, MaxScale);
        }

        /// <summary>Highest point a cloud of <paramref name="cloudClass"/> reaches above the ground, metres.</summary>
        public double TopOf(CloudClassSettings cloudClass) {
            return cloudClass.MaxAltitude + cloudClass.MaxDiameter * MaxScale * cloudClass.HeightShare;
        }

        /// <summary>
        /// Most a cloud of <paramref name="cloudClass"/> grows in a giant's zone: 1 + size boost × giant bias, held where
        /// two of the class's largest clouds so grown still fit one cell; 1 without <see cref="Landmarks"/>.
        /// </summary>
        public double BoostCap(CloudClassSettings cloudClass) {
            if (Landmarks == null) return 1.0;

            double room = CellSize - MinGap - 2.0 * cloudClass.MotionReach;
            double fits = room / (2.0 * MaxReach(cloudClass));
            return Math.Max(1.0, Math.Min(1.0 + Landmarks.SizeBoost * Landmarks.GiantBias, fits));
        }

        /// <summary>
        /// Least distance between the anchors of two clouds of <paramref name="cloudClass"/> with footprint radii
        /// <paramref name="reach"/> and <paramref name="otherReach"/>: both radii, the gap and both clouds' motion.
        /// </summary>
        public double Exclusion(CloudClassSettings cloudClass, double reach, double otherReach) {
            return reach + otherReach + MinGap + 2.0 * cloudClass.MotionReach;
        }

        /// <summary>
        /// Size share a cloud <paramref name="distance"/> metres (horizontal) from the camera is drawn at: 1 within
        /// <see cref="FullRadius"/>, smoothstepped to 0 at <see cref="WindowRadius"/>.
        /// </summary>
        public double Presence(double distance) {
            double full = FullRadius;
            if (distance <= full) return 1.0;

            double share = Math.Min(1.0, (distance - full) / (WindowRadius - full));
            return 1.0 - share * share * (3.0 - 2.0 * share);
        }

        /// <summary>
        /// True when a cloud <paramref name="distance"/> metres (horizontal) out draws its coarse mesh: past the band about
        /// <see cref="FarMeshRadius"/> it is coarse, inside it fine, and within the band it keeps <paramref name="wasFar"/>.
        /// </summary>
        public bool IsFarMesh(double distance, bool wasFar) {
            if (distance > FarMeshRadius + FarMeshHysteresis) return true;
            if (distance < FarMeshRadius - FarMeshHysteresis) return false;

            return wasFar;
        }

        /// <summary>The mesh a cloud seen for the first time <paramref name="distance"/> metres out draws: coarse past <see cref="FarMeshRadius"/>.</summary>
        public bool IsFarMesh(double distance) {
            return distance > FarMeshRadius;
        }

        /// <summary>Throws when a value is out of range or the classes do not fit.</summary>
        public void Validate() {
            if (!(CellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(CellSize), CellSize, "Must be positive.");
            if (WindowCells < 1) throw new ArgumentOutOfRangeException(nameof(WindowCells), WindowCells, "Must be at least 1.");
            if (!(MinGap >= 0.0)) throw new ArgumentOutOfRangeException(nameof(MinGap), MinGap, "Must not be negative.");
            if (!(WanderPeriodSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(WanderPeriodSeconds), WanderPeriodSeconds, "Must be positive.");
            if (!(SurgePeriodSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(SurgePeriodSeconds), SurgePeriodSeconds, "Must be positive.");
            if (!(DissolveShare > 0.0 && DissolveShare < 1.0)) throw new ArgumentOutOfRangeException(nameof(DissolveShare), DissolveShare, "Must be in (0, 1).");
            if (!(MinCoverage >= 0.0 && MinCoverage <= 1.0)) throw new ArgumentOutOfRangeException(nameof(MinCoverage), MinCoverage, "Must be in [0, 1].");
            if (!(CoveragePeriod > 0.0)) throw new ArgumentOutOfRangeException(nameof(CoveragePeriod), CoveragePeriod, "Must be positive.");
            if (!(MinScale > 0.0 && MaxScale >= MinScale)) throw new ArgumentOutOfRangeException(nameof(MaxScale), MaxScale, "Need 0 < min <= max scale.");
            if (!(FarMeshHysteresis >= 0.0 && FarMeshRadius > FarMeshHysteresis)) throw new ArgumentOutOfRangeException(nameof(FarMeshRadius), FarMeshRadius, "Need 0 <= hysteresis < far mesh radius.");
            ValidateClasses();
            Landmarks?.Validate();
        }

        private void ValidateClasses() {
            if (Classes == null || Classes.Length != ClassCount) throw new ArgumentException($"Need one class setting per {nameof(CloudClass)} ({ClassCount}).", nameof(Classes));

            for (int index = 0; index < ClassCount; index++) {
                CloudClassSettings cloudClass = Classes[index];
                if (cloudClass == null) throw new ArgumentException("A class setting is null.", nameof(Classes));

                cloudClass.Validate();
                ValidateMotion(cloudClass);
            }
        }

        // The anchor inset leaves room in the cell, the surge stays under its share and the widest exclusion fits one cell (the scatter looks one cell over).
        private void ValidateMotion(CloudClassSettings cloudClass) {
            if (!(2.0 * AnchorInset(cloudClass) < CellSize)) throw new ArgumentOutOfRangeException(nameof(Classes), cloudClass.MotionReach, "Wander plus surge must be under half the cell.");
            if (!(2.0 * cloudClass.SurgeAmplitude <= MaxSurgeShare * CellSize)) throw new ArgumentOutOfRangeException(nameof(Classes), cloudClass.SurgeAmplitude, $"A surge may swing at most {MaxSurgeShare:P0} of a cell.");

            double widest = MaxReach(cloudClass);
            if (!(Exclusion(cloudClass, widest, widest) <= CellSize)) throw new ArgumentOutOfRangeException(nameof(Classes), cloudClass.MaxDiameter, $"Two of the largest clouds plus gap and motion must fit in one cell (scale at most {MaxScaleThatFits():0.000}).");
        }

        /// <summary>
        /// Largest <see cref="MaxScale"/> the classes, gap, motion and cell allow: two of a class's largest clouds plus
        /// the gap and both clouds' motion still fit one cell. Editors clamp to it so a slider never empties the sky.
        /// </summary>
        public double MaxScaleThatFits() {
            double fits = double.MaxValue;
            if (Classes == null) return fits;

            foreach (CloudClassSettings cloudClass in Classes) {
                if (cloudClass == null || !(cloudClass.MaxDiameter > 0.0)) continue;

                double room = CellSize - MinGap - 2.0 * cloudClass.MotionReach;
                fits = Math.Min(fits, room / (2.0 * cloudClass.MaxDiameter * CloudHeap.ReachShare));
            }

            return fits;
        }

        // A cloud centred d out reaches d + p·R across and alt + p·H up at presence p; sampled every metre of the window.
        private double FarClipOf(CloudClassSettings cloudClass) {
            double reach = MaxReach(cloudClass);
            double height = cloudClass.MaxDiameter * MaxScale * cloudClass.HeightShare;
            double farthest = 0.0;
            for (double distance = 0.0; distance <= WindowRadius; distance += 1.0) {
                double presence = Presence(distance);
                double across = distance + presence * reach;
                double up = cloudClass.MaxAltitude + presence * height;
                farthest = Math.Max(farthest, Math.Sqrt(across * across + up * up));
            }

            return farthest;
        }

        // Mesh diameters (instances scale 0.6–1.6 on top): Small 10–26 m at 120–160 m, Medium 24–48 m at 140–190 m,
        // Large 44–90 m at 170–220 m and slow; small clouds shadow as one disc each.
        private static CloudClassSettings[] DefaultClasses() {
            return new[] {
                new CloudClassSettings(10.0, 26.0, 120.0, 160.0, 3, 7, 20, 300, 0.9, 1.1, 3.0, 9.0, true),
                new CloudClassSettings(24.0, 48.0, 140.0, 190.0, 6, 11, 12, 500, 0.72, 0.8, 4.0, 11.0),
                new CloudClassSettings(44.0, 90.0, 170.0, 220.0, 10, 15, 3, 800, 0.58, 0.55, 8.0, 14.0),
            };
        }
    }
}
