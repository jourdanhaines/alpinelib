using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of the drifting cloud field: the wind-frame lattice clouds are seeded on, how densely, how far they
    /// wander about their anchors, the window drawn around a camera and the classes they come in.
    /// </summary>
    /// <remarks>
    /// A cloud is shown at full size within (1 − <see cref="DissolveShare"/>) of <see cref="WindowRadius"/> and
    /// condenses or evaporates across the rest, past the fog's reach, so the field never dissolves in view.
    /// </remarks>
    public sealed class CloudFieldSettings {
        /// <summary>Mesh variants per class.</summary>
        public const int VariantsPerClass = 4;

        /// <summary>Classes of <see cref="CloudClass"/>.</summary>
        public const int ClassCount = 3;

        /// <summary>Distinct cloud meshes the field draws.</summary>
        public const int MeshCount = VariantsPerClass * ClassCount;

        /// <summary>Edge of one field cell of the wind-frame lattice, metres.</summary>
        public double CellSize { get; set; } = 256.0;

        /// <summary>Field cells from the camera's cell to the window's edge (4 = a 9 × 9 window).</summary>
        public int WindowCells { get; set; } = 4;

        /// <summary>Candidate clouds drawn per cell; those crowded by a higher-priority neighbour or out of coverage are dropped.</summary>
        public int CandidatesPerCell { get; set; } = 16;

        /// <summary>Least distance between two drawn clouds' centres at any time, metres (wander included).</summary>
        public double MinSpacing { get; set; } = 30.0;

        /// <summary>Most a cloud strays from its anchor, metres: less than the inset, so it never leaves its cell.</summary>
        public double WanderAmplitude { get; set; } = 12.0;

        /// <summary>Time scale of the wander, seconds.</summary>
        public double WanderPeriodSeconds { get; set; } = 150.0;

        /// <summary>Outer share of the window radius over which clouds condense in and evaporate out.</summary>
        public double DissolveShare { get; set; } = 0.15;

        /// <summary>Least chance a candidate is kept, where the coverage noise is thinnest (1 everywhere at its thickest).</summary>
        public double MinCoverage { get; set; } = 0.45;

        /// <summary>Wavelength of the coverage noise over the wind frame, metres: clear and crowded stretches of sky.</summary>
        public double CoveragePeriod { get; set; } = 1800.0;

        public double MinScale { get; set; } = 0.85;
        public double MaxScale { get; set; } = 1.15;

        /// <summary>The classes, indexed by <see cref="CloudClass"/>.</summary>
        public CloudClassSettings[] Classes { get; set; } = DefaultClasses();

        /// <summary>The shipped field.</summary>
        public static CloudFieldSettings Default => new CloudFieldSettings();

        /// <summary>Horizontal distance from the camera at which clouds have evaporated, metres.</summary>
        public double WindowRadius => WindowCells * CellSize;

        /// <summary>Horizontal distance from the camera within which clouds are full size, metres.</summary>
        public double FullRadius => WindowRadius * (1.0 - DissolveShare);

        /// <summary>Least distance between two anchors: the spacing plus both clouds' wander.</summary>
        public double AnchorSpacing => MinSpacing + 2.0 * WanderAmplitude;

        /// <summary>Margin kept between an anchor and its cell's edges, metres.</summary>
        public double AnchorInset => WanderAmplitude;

        /// <summary>
        /// Far clip a camera needs to see every drawn cloud whole, dissolving ones too: a cloud stands anywhere inside
        /// <see cref="WindowRadius"/> and reaches at most one full diameter above its altitude and half one across.
        /// </summary>
        public double RequiredFarClip {
            get {
                double altitude = 0.0;
                double diameter = 0.0;
                foreach (CloudClassSettings cloudClass in Classes) {
                    altitude = Math.Max(altitude, cloudClass.MaxAltitude);
                    diameter = Math.Max(diameter, cloudClass.MaxDiameter);
                }

                double top = altitude + diameter;
                return Math.Sqrt(WindowRadius * WindowRadius + top * top) + diameter * 0.5;
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

        /// <summary>Throws when a value is out of range or the classes do not fit.</summary>
        public void Validate() {
            if (!(CellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(CellSize), CellSize, "Must be positive.");
            if (WindowCells < 1) throw new ArgumentOutOfRangeException(nameof(WindowCells), WindowCells, "Must be at least 1.");
            if (CandidatesPerCell < 0) throw new ArgumentOutOfRangeException(nameof(CandidatesPerCell), CandidatesPerCell, "Must not be negative.");
            if (!(MinSpacing >= 0.0)) throw new ArgumentOutOfRangeException(nameof(MinSpacing), MinSpacing, "Must not be negative.");
            if (!(AnchorSpacing <= CellSize)) throw new ArgumentOutOfRangeException(nameof(MinSpacing), MinSpacing, "Spacing plus both wanders must fit in one cell (the scatter only looks one cell over).");
            if (!(WanderAmplitude >= 0.0 && 2.0 * AnchorInset < CellSize)) throw new ArgumentOutOfRangeException(nameof(WanderAmplitude), WanderAmplitude, "Must be non-negative and under half the cell.");
            if (!(WanderPeriodSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(WanderPeriodSeconds), WanderPeriodSeconds, "Must be positive.");
            if (!(DissolveShare > 0.0 && DissolveShare < 1.0)) throw new ArgumentOutOfRangeException(nameof(DissolveShare), DissolveShare, "Must be in (0, 1).");
            if (!(MinCoverage >= 0.0 && MinCoverage <= 1.0)) throw new ArgumentOutOfRangeException(nameof(MinCoverage), MinCoverage, "Must be in [0, 1].");
            if (!(CoveragePeriod > 0.0)) throw new ArgumentOutOfRangeException(nameof(CoveragePeriod), CoveragePeriod, "Must be positive.");
            if (!(MinScale > 0.0 && MaxScale >= MinScale)) throw new ArgumentOutOfRangeException(nameof(MaxScale), MaxScale, "Need 0 < min <= max scale.");
            ValidateClasses();
        }

        private void ValidateClasses() {
            if (Classes == null || Classes.Length != ClassCount) throw new ArgumentException($"Need one class setting per {nameof(CloudClass)} ({ClassCount}).", nameof(Classes));

            double total = 0.0;
            foreach (CloudClassSettings cloudClass in Classes) {
                if (cloudClass == null) throw new ArgumentException("A class setting is null.", nameof(Classes));

                cloudClass.Validate();
                if (!(cloudClass.MinDiameter / MinScale <= cloudClass.MaxDiameter / MaxScale)) throw new ArgumentOutOfRangeException(nameof(Classes), cloudClass.MaxDiameter, "The diameter range is narrower than the scale range.");
                total += cloudClass.Weight;
            }
            if (!(total > 0.0)) throw new ArgumentException("The class weights sum to 0.", nameof(Classes));
        }

        // Small Ø16–24 m at 120–160 m, Medium Ø28–40 m at 140–190 m, Large Ø44–60 m at 170–220 m.
        private static CloudClassSettings[] DefaultClasses() {
            return new[] {
                new CloudClassSettings(16.0, 24.0, 120.0, 160.0, 5, 7, 0.45, 260),
                new CloudClassSettings(28.0, 40.0, 140.0, 190.0, 7, 10, 0.35, 420),
                new CloudClassSettings(44.0, 60.0, 170.0, 220.0, 10, 14, 0.20, 700),
            };
        }
    }
}
