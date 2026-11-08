using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How a cloud field reacts to landmarks (<see cref="ILandmarkSkyProvider"/>): a cloud whose base is below a
    /// landmark's top dissolves as it nears the rock and re-forms past it (never moving up or down), and giants gather
    /// clouds (orographic clustering) in a zone about them stretched downwind, where extra latent clouds condense
    /// (packed tighter and grown a little). The open sky's regular clouds are never changed.
    /// </summary>
    /// <remarks>
    /// A cloud's density is its coverage plus the giant bias at its current position (times its class weight); a
    /// latent cloud shows with a smooth envelope as the density passes its threshold, drawn in
    /// (1 + <see cref="EnvelopeWidth"/>, 1 + <see cref="GiantBias"/>], so no latent cloud shows away from giants.
    /// </remarks>
    public sealed class CloudLandmarkSettings {
        /// <summary>A landmark blocks a cloud whose base is below its top plus this, metres.</summary>
        public double BlockMargin { get; set; } = 5.0;

        /// <summary>Gap from a blocked cloud's footprint edge to the hull at which it has dissolved, metres.</summary>
        public double DissolveNear { get; set; } = 20.0;

        /// <summary>Gap at which a blocked cloud starts to dissolve, metres.</summary>
        public double DissolveFar { get; set; } = 180.0;

        /// <summary>Size share below which a dissolving cloud is not drawn at all.</summary>
        public double DissolveCutoff { get; set; } = 0.05;

        /// <summary>Density a giant adds to its zone at full strength.</summary>
        public double GiantBias { get; set; } = 0.6;

        /// <summary>Radius of the full-strength zone about a giant's anchor (and its downwind stretch), metres.</summary>
        public double BiasRadius { get; set; } = 400.0;

        /// <summary>How far downwind of the anchor the zone stretches, metres.</summary>
        public double BiasDownwind { get; set; } = 600.0;

        /// <summary>Width of the band outside the zone over which the bias falls to none, metres.</summary>
        public double BiasFade { get; set; } = 200.0;

        /// <summary>Size gained per unit of bias (scale × (1 + this × bias), capped so the largest pair still fits a field cell).</summary>
        public double SizeBoost { get; set; } = 0.3;

        /// <summary>Density span over which a latent cloud condenses from nothing to full size.</summary>
        public double EnvelopeWidth { get; set; } = 0.1;

        /// <summary>Weight on the giant bias per <see cref="CloudClass"/>: large clouds gather most.</summary>
        public double[] ClassWeights { get; set; } = { 1.0, 1.0, 1.5 };

        /// <summary>Latent candidates each field cell draws per <see cref="CloudClass"/>.</summary>
        public int[] LatentPerCell { get; set; } = { 10, 6, 3 };

        /// <summary>Share of the exclusion latent clouds keep from any other cloud: they may pack tighter inside a giant's zone.</summary>
        public double LatentSpacing { get; set; } = 0.7;

        /// <summary>Size share of latent clouds per <see cref="CloudClass"/>, on top of the boost.</summary>
        public double[] LatentScales { get; set; } = { 0.8, 1.0, 1.0 };

        public static CloudLandmarkSettings Default => new CloudLandmarkSettings();

        /// <summary>Farthest a landmark's hull can be from a cloud and still change it, metres (past the cloud's own reach).</summary>
        public double InfluenceReach => Math.Max(DissolveFar, BiasRadius + BiasDownwind + BiasFade);

        public double WeightOf(CloudClass cloudClass) {
            return ClassWeights[(int)cloudClass];
        }

        public double LatentScaleOf(CloudClass cloudClass) {
            return LatentScales[(int)cloudClass];
        }

        public int LatentOf(CloudClass cloudClass) {
            return LatentPerCell[(int)cloudClass];
        }

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (!(BlockMargin >= 0.0)) throw new ArgumentOutOfRangeException(nameof(BlockMargin), BlockMargin, "Must not be negative.");
            if (!(DissolveNear >= 0.0 && DissolveFar > DissolveNear)) throw new ArgumentOutOfRangeException(nameof(DissolveFar), DissolveFar, "Need 0 <= near < far.");
            if (!(DissolveCutoff > 0.0 && DissolveCutoff < 1.0)) throw new ArgumentOutOfRangeException(nameof(DissolveCutoff), DissolveCutoff, "Must be in (0, 1).");
            if (!(GiantBias > 0.0 && GiantBias <= 1.0)) throw new ArgumentOutOfRangeException(nameof(GiantBias), GiantBias, "Must be in (0, 1].");
            if (!(BiasRadius >= 0.0 && BiasDownwind >= 0.0 && BiasFade > 0.0)) throw new ArgumentOutOfRangeException(nameof(BiasFade), BiasFade, "Need radius, downwind >= 0 and fade > 0.");
            if (!(SizeBoost >= 0.0)) throw new ArgumentOutOfRangeException(nameof(SizeBoost), SizeBoost, "Must not be negative.");
            if (!(EnvelopeWidth > 0.0 && EnvelopeWidth < GiantBias)) throw new ArgumentOutOfRangeException(nameof(EnvelopeWidth), EnvelopeWidth, "Must be in (0, giant bias).");
            if (ClassWeights == null || ClassWeights.Length != CloudFieldSettings.ClassCount) throw new ArgumentException("Need one weight per class.", nameof(ClassWeights));
            if (LatentPerCell == null || LatentPerCell.Length != CloudFieldSettings.ClassCount) throw new ArgumentException("Need one latent count per class.", nameof(LatentPerCell));
            if (LatentScales == null || LatentScales.Length != CloudFieldSettings.ClassCount) throw new ArgumentException("Need one latent scale per class.", nameof(LatentScales));
            if (!(LatentSpacing > 0.0 && LatentSpacing <= 1.0)) throw new ArgumentOutOfRangeException(nameof(LatentSpacing), LatentSpacing, "Must be in (0, 1].");

            for (int index = 0; index < CloudFieldSettings.ClassCount; index++) {
                if (!(ClassWeights[index] >= 0.0)) throw new ArgumentOutOfRangeException(nameof(ClassWeights), ClassWeights[index], "Must not be negative.");
                if (!(LatentScales[index] > 0.0 && LatentScales[index] <= 1.0)) throw new ArgumentOutOfRangeException(nameof(LatentScales), LatentScales[index], "Must be in (0, 1].");
                if (LatentPerCell[index] < 0) throw new ArgumentOutOfRangeException(nameof(LatentPerCell), LatentPerCell[index], "Must not be negative.");
            }
        }
    }
}
