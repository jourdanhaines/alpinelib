using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// How a cloud field reacts to landmarks (<see cref="ILandmarkSkyProvider"/>): clouds lift clear of every
    /// landmark's top, and giants gather clouds (orographic clustering) in a zone about them stretched downwind, where
    /// extra latent clouds condense and every cloud grows a little.
    /// </summary>
    /// <remarks>
    /// A cloud's density is its coverage plus the giant bias at its current position (times its class weight); a
    /// latent cloud shows with a smooth envelope as the density passes its threshold, drawn in
    /// (1 + <see cref="EnvelopeWidth"/>, 1 + <see cref="GiantBias"/>], so no latent cloud shows away from giants.
    /// </remarks>
    public sealed class CloudLandmarkSettings {
        /// <summary>Least gap kept between a landmark's top and the base of a cloud over it, metres (plus the lift's own margin).</summary>
        public double LiftClearance { get; set; } = 25.0;

        /// <summary>Distance outside a hull over which the lift falls from full to none, metres.</summary>
        public double LiftFalloff { get; set; } = 150.0;

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

        public static CloudLandmarkSettings Default => new CloudLandmarkSettings();

        /// <summary>Farthest a landmark's hull can be from a cloud and still change it, metres (past the cloud's own reach).</summary>
        public double InfluenceReach => Math.Max(LiftFalloff, BiasRadius + BiasDownwind + BiasFade);

        public double WeightOf(CloudClass cloudClass) {
            return ClassWeights[(int)cloudClass];
        }

        public int LatentOf(CloudClass cloudClass) {
            return LatentPerCell[(int)cloudClass];
        }

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            if (!(LiftClearance >= 0.0)) throw new ArgumentOutOfRangeException(nameof(LiftClearance), LiftClearance, "Must not be negative.");
            if (!(LiftFalloff > 0.0)) throw new ArgumentOutOfRangeException(nameof(LiftFalloff), LiftFalloff, "Must be positive.");
            if (!(GiantBias > 0.0 && GiantBias <= 1.0)) throw new ArgumentOutOfRangeException(nameof(GiantBias), GiantBias, "Must be in (0, 1].");
            if (!(BiasRadius >= 0.0 && BiasDownwind >= 0.0 && BiasFade > 0.0)) throw new ArgumentOutOfRangeException(nameof(BiasFade), BiasFade, "Need radius, downwind >= 0 and fade > 0.");
            if (!(SizeBoost >= 0.0)) throw new ArgumentOutOfRangeException(nameof(SizeBoost), SizeBoost, "Must not be negative.");
            if (!(EnvelopeWidth > 0.0 && EnvelopeWidth < GiantBias)) throw new ArgumentOutOfRangeException(nameof(EnvelopeWidth), EnvelopeWidth, "Must be in (0, giant bias).");
            if (ClassWeights == null || ClassWeights.Length != CloudFieldSettings.ClassCount) throw new ArgumentException("Need one weight per class.", nameof(ClassWeights));
            if (LatentPerCell == null || LatentPerCell.Length != CloudFieldSettings.ClassCount) throw new ArgumentException("Need one latent count per class.", nameof(LatentPerCell));

            for (int index = 0; index < CloudFieldSettings.ClassCount; index++) {
                if (!(ClassWeights[index] >= 0.0)) throw new ArgumentOutOfRangeException(nameof(ClassWeights), ClassWeights[index], "Must not be negative.");
                if (LatentPerCell[index] < 0) throw new ArgumentOutOfRangeException(nameof(LatentPerCell), LatentPerCell[index], "Must not be negative.");
            }
        }
    }
}
