using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of the backdrop ring: world-fixed silhouettes seeded on a grid of sectors in absolute xz, drawn only
    /// well clear of the streamed window around the camera and out to a radius the fog has long hidden, their sizes,
    /// and the ground lattice under them.
    /// </summary>
    /// <remarks>Distances are absolute-world metres. Nothing grows, sinks or fades: the fog alone reveals a silhouette.</remarks>
    public sealed class BackdropSettings {
        /// <summary>Mesh variants per kind.</summary>
        public const int VariantsPerKind = 4;

        /// <summary>Kinds of <see cref="BackdropFeatureKind"/>.</summary>
        public const int KindCount = 4;

        /// <summary>Distinct silhouette meshes the ring draws.</summary>
        public const int MeshCount = VariantsPerKind * KindCount;

        /// <summary>Farthest a silhouette's centre is drawn from the camera; the fog must have it fully hidden there.</summary>
        public double OuterRadius { get; set; } = 1050.0;

        /// <summary>Edge of one keep-out cell: the streamed world's cell, the camera's own being the one it stands in.</summary>
        public double KeepOutCellSize { get; set; } = 128.0;

        /// <summary>Cells the keep-out square reaches either side of the camera's cell (the streamed load radius).</summary>
        public int KeepOutRadiusCells { get; set; } = 3;

        /// <summary>
        /// Least distance from any point of the camera's cell to any part of a drawn silhouette: features closer to the
        /// keep-out square than <see cref="KeepOutMargin"/> are not drawn, so none ever stands inside the loaded world.
        /// </summary>
        public double ClearDistance { get; set; } = 700.0;

        /// <summary>Edge of one seeding sector.</summary>
        public double SectorSize { get; set; } = 160.0;

        public int MinFeaturesPerSector { get; set; }
        public int MaxFeaturesPerSector { get; set; } = 2;

        public double MinWidth { get; set; } = 60.0;
        public double MaxWidth { get; set; } = 220.0;
        public double MinHeight { get; set; } = 40.0;
        public double MaxHeight { get; set; } = 120.0;

        /// <summary>Widest a rock silhouette may be, in heights: low wide rolls are narrowed so none reads as a thin plank on the horizon.</summary>
        public double MaxRockAspect { get; set; } = 2.5;

        /// <summary>Share of the base a rock silhouette is sunk into the ground, so its foot never shows.</summary>
        public double BaseSink { get; set; } = 0.04;

        /// <summary>Chance a feature is a cloud bank rather than rock.</summary>
        public double CloudBankShare { get; set; } = 0.15;

        public double CloudBankMinAltitude { get; set; } = 60.0;
        public double CloudBankMaxAltitude { get; set; } = 150.0;
        public double CloudBankMinHeight { get; set; } = 18.0;
        public double CloudBankMaxHeight { get; set; } = 40.0;

        /// <summary>Most a cloud bank's width strays from height × its mesh's own aspect, as a share (0.1 = 0.9–1.1).</summary>
        public double CloudBankWidthJitter { get; set; } = 0.1;

        /// <summary>Most a cloud bank's depth strays from its height, as a share (0.2 = 0.8–1.2).</summary>
        public double CloudBankDepthJitter { get; set; } = 0.2;

        /// <summary>Widest a cloud bank may be, in heights (jitter included): bounds its footprint reach; the layout caps wider ones.</summary>
        public double CloudBankMaxAspect { get; set; } = 7.0;

        /// <summary>
        /// The flat ground annulus drawn under the ring, centred on the camera: it hides the edge of the streamed
        /// world from height and carries on under the silhouettes (the lattice's outer edge reaches past
        /// <see cref="OuterRadius"/> wherever its anchor sits).
        /// </summary>
        public double GroundInnerRadius { get; set; } = 380.0;
        public double GroundOuterRadius { get; set; } = 1080.0;

        /// <summary>How far below the ground height the annulus lies, so it never shows through streamed ground.</summary>
        public double GroundDrop { get; set; } = 0.3;

        /// <summary>Vertex spacing (m) of the world-anchored lattice that replaces the annulus when the consumer colours its ground.</summary>
        public double GroundLatticeSpacing { get; set; } = 16.0;

        /// <summary>Step (m) the lattice's anchor snaps to in absolute xz; it is recoloured only when the anchor moves.</summary>
        public double GroundLatticeSnap { get; set; } = 32.0;

        /// <summary>The lattice's hole about its anchor: the annulus's inner radius less the farthest the anchor sits from the camera.</summary>
        public double GroundLatticeHoleRadius => GroundInnerRadius - GroundLatticeSnap * Math.Sqrt(0.5);

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

        /// <summary>Least far clip a camera needs: every drawn silhouette's centre (parts beyond it are in full fog).</summary>
        public double RequiredFarClip => OuterRadius;

        /// <summary>Widest a cloud bank can be, metres.</summary>
        public double CloudBankMaxWidth => CloudBankMaxHeight * CloudBankMaxAspect;

        /// <summary>How far outside the keep-out square every part of a drawn silhouette lies.</summary>
        public double KeepOutMargin => ClearDistance - KeepOutRadiusCells * KeepOutCellSize;

        /// <summary>Farthest any part of a drawn silhouette reaches from its centre, metres.</summary>
        public double MaxReach => Math.Max(BackdropFeature.ReachOf(BackdropFeatureKind.Mesa, MaxWidth, MaxWidth),
            BackdropFeature.ReachOf(BackdropFeatureKind.CloudBank, CloudBankMaxWidth, CloudBankMaxHeight * (1.0 + CloudBankDepthJitter)));

        /// <summary>Throws when a value is out of range or the bands are inverted.</summary>
        public void Validate() {
            RequirePositive(SectorSize, nameof(SectorSize));
            RequirePositive(KeepOutCellSize, nameof(KeepOutCellSize));
            if (KeepOutRadiusCells < 0) throw new ArgumentOutOfRangeException(nameof(KeepOutRadiusCells), KeepOutRadiusCells, "Must not be negative.");
            RequireOrdered(KeepOutRadiusCells * KeepOutCellSize, ClearDistance, nameof(ClearDistance));
            RequireOrdered(ClearDistance, OuterRadius, nameof(OuterRadius));
            RequireOrdered(MinWidth, MaxWidth, nameof(MaxWidth));
            RequireOrdered(MinHeight, MaxHeight, nameof(MaxHeight));
            RequireOrdered(CloudBankMinAltitude, CloudBankMaxAltitude, nameof(CloudBankMaxAltitude));
            RequireOrdered(CloudBankMinHeight, CloudBankMaxHeight, nameof(CloudBankMaxHeight));
            RequirePositive(CloudBankMaxAspect, nameof(CloudBankMaxAspect));
            RequireShare(CloudBankWidthJitter, nameof(CloudBankWidthJitter));
            RequireShare(CloudBankDepthJitter, nameof(CloudBankDepthJitter));
            RequirePositive(GroundInnerRadius, nameof(GroundInnerRadius));
            RequireOrdered(GroundInnerRadius, GroundOuterRadius, nameof(GroundOuterRadius));
            RequireOrdered(OuterRadius + GroundLatticeSnap * Math.Sqrt(0.5), GroundOuterRadius, nameof(GroundOuterRadius));
            if (GroundDrop < 0.0) throw new ArgumentOutOfRangeException(nameof(GroundDrop), GroundDrop, "Must not be negative.");
            RequirePositive(GroundLatticeSpacing, nameof(GroundLatticeSpacing));
            RequirePositive(GroundLatticeSnap, nameof(GroundLatticeSnap));
            RequirePositive(GroundLatticeHoleRadius, nameof(GroundLatticeHoleRadius));
            RequirePositive(MinWidth, nameof(MinWidth));
            RequirePositive(MinHeight, nameof(MinHeight));
            RequirePositive(MaxRockAspect, nameof(MaxRockAspect));
            RequirePositive(CloudBankMinHeight, nameof(CloudBankMinHeight));
            if (MinFeaturesPerSector < 0 || MaxFeaturesPerSector < MinFeaturesPerSector) {
                throw new ArgumentOutOfRangeException(nameof(MaxFeaturesPerSector), MaxFeaturesPerSector, "Need 0 <= min <= max features per sector.");
            }
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
