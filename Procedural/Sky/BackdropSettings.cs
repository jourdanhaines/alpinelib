using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of the backdrop ring: the band around the camera silhouettes are drawn in, the sector grid they
    /// are seeded on, how many each sector holds, their sizes, and the parallax of the frame they live in.
    /// </summary>
    /// <remarks>Distances are metres in the backdrop frame (see <see cref="ParallaxFactor"/>).</remarks>
    public sealed class BackdropSettings {
        /// <summary>Mesh variants per kind.</summary>
        public const int VariantsPerKind = 4;

        /// <summary>Kinds of <see cref="BackdropFeatureKind"/>.</summary>
        public const int KindCount = 4;

        /// <summary>Distinct silhouette meshes the ring draws.</summary>
        public const int MeshCount = VariantsPerKind * KindCount;

        /// <summary>Nearest a silhouette is drawn from the camera.</summary>
        public double InnerRadius { get; set; } = 420.0;

        /// <summary>Farthest a silhouette is drawn from the camera.</summary>
        public double OuterRadius { get; set; } = 820.0;

        /// <summary>Width of the band at each edge over which silhouettes sink (inner) or rise (outer) into view.</summary>
        public double FadeBand { get; set; } = 60.0;

        /// <summary>Edge of one seeding sector.</summary>
        public double SectorSize { get; set; } = 160.0;

        public int MinFeaturesPerSector { get; set; }
        public int MaxFeaturesPerSector { get; set; } = 2;

        /// <summary>
        /// Share of the camera's motion the frame follows: the ring sits at 0.6 × the camera's absolute xz, so
        /// silhouettes drift at 0.4 of the camera's speed and can never be reached.
        /// </summary>
        public double ParallaxFactor { get; set; } = 0.6;

        public double MinWidth { get; set; } = 60.0;
        public double MaxWidth { get; set; } = 220.0;
        public double MinHeight { get; set; } = 40.0;
        public double MaxHeight { get; set; } = 120.0;

        /// <summary>Share of the base a rock silhouette is sunk into the ground, so its foot never shows.</summary>
        public double BaseSink { get; set; } = 0.04;

        /// <summary>Chance a feature is a cloud bank rather than rock.</summary>
        public double CloudBankShare { get; set; } = 0.15;

        public double CloudBankInnerRadius { get; set; } = 600.0;
        public double CloudBankOuterRadius { get; set; } = 800.0;
        public double CloudBankMinAltitude { get; set; } = 60.0;
        public double CloudBankMaxAltitude { get; set; } = 150.0;
        public double CloudBankMinHeight { get; set; } = 18.0;
        public double CloudBankMaxHeight { get; set; } = 40.0;

        /// <summary>Most a cloud bank's width strays from height × its mesh's own aspect, as a share (0.1 = 0.9–1.1).</summary>
        public double CloudBankWidthJitter { get; set; } = 0.1;

        /// <summary>Most a cloud bank's depth strays from its height, as a share (0.2 = 0.8–1.2).</summary>
        public double CloudBankDepthJitter { get; set; } = 0.2;

        /// <summary>Widest a cloud bank may be, in heights (jitter included): bounds the far clip; the layout caps wider ones.</summary>
        public double CloudBankMaxAspect { get; set; } = 7.0;

        /// <summary>
        /// The flat ground annulus drawn under the ring, centred on the camera: it hides the edge of the streamed
        /// world from height and the sunk feet of near silhouettes.
        /// </summary>
        public double GroundInnerRadius { get; set; } = 380.0;
        public double GroundOuterRadius { get; set; } = 1000.0;

        /// <summary>How far below the ground height the annulus lies, so it never shows through streamed ground.</summary>
        public double GroundDrop { get; set; } = 0.3;

        /// <summary>Relative chances of mesa, butte and ridge among the rock silhouettes.</summary>
        public double MesaWeight { get; set; } = 0.4;
        public double ButteWeight { get; set; } = 0.25;
        public double RidgeWeight { get; set; } = 0.35;

        /// <summary>The kind whose silhouette is mesh <paramref name="meshIndex"/> (see <see cref="BackdropFeature.MeshIndex"/>).</summary>
        public static BackdropFeatureKind KindOf(int meshIndex) {
            return (BackdropFeatureKind)(meshIndex / VariantsPerKind);
        }

        /// <summary>The settings the plan starts from.</summary>
        public static BackdropSettings Default => new BackdropSettings();

        /// <summary>Least far clip a camera needs to see the whole ring: each band's outer radius plus half its widest silhouette.</summary>
        public double RequiredFarClip => Math.Max(OuterRadius + MaxWidth * 0.5, CloudBankOuterRadius + CloudBankMaxWidth * 0.5);

        /// <summary>Farthest any feature is drawn from the camera: the outer edge of the widest band.</summary>
        public double MaxBandRadius => Math.Max(OuterRadius, CloudBankOuterRadius);

        /// <summary>Widest a cloud bank can be, metres.</summary>
        public double CloudBankMaxWidth => CloudBankMaxHeight * CloudBankMaxAspect;

        /// <summary>The band <paramref name="kind"/> is drawn in, from the camera.</summary>
        public void BandOf(BackdropFeatureKind kind, out double inner, out double outer) {
            bool isCloud = kind == BackdropFeatureKind.CloudBank;
            inner = isCloud ? CloudBankInnerRadius : InnerRadius;
            outer = isCloud ? CloudBankOuterRadius : OuterRadius;
        }

        /// <summary>
        /// How far a <paramref name="kind"/> silhouette <paramref name="distance"/> from the camera has risen into
        /// view: 0 outside its band, ramping to 1 across <see cref="FadeBand"/> at either edge.
        /// </summary>
        public double Presence(BackdropFeatureKind kind, double distance) {
            BandOf(kind, out double inner, out double outer);
            if (!(distance > inner) || !(distance < outer)) return 0.0;

            double fade = Math.Min(FadeBand, (outer - inner) * 0.5);
            if (fade <= 0.0) return 1.0;

            return Math.Min(1.0, Math.Min(distance - inner, outer - distance) / fade);
        }

        /// <summary>Throws when a value is out of range or the bands are inverted.</summary>
        public void Validate() {
            RequirePositive(SectorSize, nameof(SectorSize));
            RequirePositive(InnerRadius, nameof(InnerRadius));
            RequireOrdered(InnerRadius, OuterRadius, nameof(OuterRadius));
            RequireOrdered(CloudBankInnerRadius, CloudBankOuterRadius, nameof(CloudBankOuterRadius));
            RequireOrdered(MinWidth, MaxWidth, nameof(MaxWidth));
            RequireOrdered(MinHeight, MaxHeight, nameof(MaxHeight));
            RequireOrdered(CloudBankMinAltitude, CloudBankMaxAltitude, nameof(CloudBankMaxAltitude));
            RequireOrdered(CloudBankMinHeight, CloudBankMaxHeight, nameof(CloudBankMaxHeight));
            RequirePositive(CloudBankMaxAspect, nameof(CloudBankMaxAspect));
            RequireShare(CloudBankWidthJitter, nameof(CloudBankWidthJitter));
            RequireShare(CloudBankDepthJitter, nameof(CloudBankDepthJitter));
            RequirePositive(GroundInnerRadius, nameof(GroundInnerRadius));
            RequireOrdered(GroundInnerRadius, GroundOuterRadius, nameof(GroundOuterRadius));
            if (GroundDrop < 0.0) throw new ArgumentOutOfRangeException(nameof(GroundDrop), GroundDrop, "Must not be negative.");
            RequirePositive(MinWidth, nameof(MinWidth));
            RequirePositive(MinHeight, nameof(MinHeight));
            RequirePositive(CloudBankMinHeight, nameof(CloudBankMinHeight));
            if (FadeBand < 0.0) throw new ArgumentOutOfRangeException(nameof(FadeBand), FadeBand, "Must not be negative.");
            if (MinFeaturesPerSector < 0 || MaxFeaturesPerSector < MinFeaturesPerSector) {
                throw new ArgumentOutOfRangeException(nameof(MaxFeaturesPerSector), MaxFeaturesPerSector, "Need 0 <= min <= max features per sector.");
            }
            if (!(ParallaxFactor >= 0.0 && ParallaxFactor < 1.0)) throw new ArgumentOutOfRangeException(nameof(ParallaxFactor), ParallaxFactor, "Must be in [0, 1).");
            if (!(CloudBankShare >= 0.0 && CloudBankShare <= 1.0)) throw new ArgumentOutOfRangeException(nameof(CloudBankShare), CloudBankShare, "Must be in [0, 1].");
            if (!(MesaWeight + ButteWeight + RidgeWeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(MesaWeight), "Rock weights must sum above 0.");
        }

        private static void RequirePositive(double value, string name) {
            if (!(value > 0.0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be positive and finite.");
        }

        private static void RequireShare(double value, string name) {
            if (!(value >= 0.0 && value < 1.0)) throw new ArgumentOutOfRangeException(name, value, "Must be in [0, 1).");
        }

        private static void RequireOrdered(double low, double high, string name) {
            if (!(high >= low) || double.IsInfinity(high)) throw new ArgumentOutOfRangeException(name, high, $"Must be finite and at least {low}.");
        }
    }
}
