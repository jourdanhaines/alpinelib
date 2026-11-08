using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// What nearby landmarks do to a cloud at its current absolute position: how much of it is left as it nears a landmark
    /// that stands above its base (it dissolves before the rock), and how much giant bias its density gains there. Stateless and in double, so every
    /// client with the same landmarks, wind and time gets the same sky.
    /// </summary>
    public sealed class CloudLandmarkInfluence {
        public CloudLandmarkInfluence(CloudLandmarkSettings settings) {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
        }

        public CloudLandmarkSettings Settings { get; }

        /// <summary>
        /// Size share a cloud of footprint radius <paramref name="reach"/> centred at (<paramref name="x"/>, <paramref name="z"/>)
        /// with its base at <paramref name="altitude"/> is drawn at: every landmark whose top plus
        /// <see cref="CloudLandmarkSettings.BlockMargin"/> is above the base blocks it, and for the nearest blocking hull the
        /// gap d from the footprint's edge (negative when they overlap) gives smoothstep(<see cref="CloudLandmarkSettings.DissolveNear"/>,
        /// <see cref="CloudLandmarkSettings.DissolveFar"/>, d). 1 when nothing blocks it. Pure in position, so a cloud
        /// shrinks as it drifts toward the rock and re-forms past it.
        /// </summary>
        public double Dissolve(IReadOnlyList<LandmarkSkyShape> shapes, double x, double z, double reach, double altitude) {
            if (shapes == null) return 1.0;

            double gap = double.MaxValue;
            double far = Settings.DissolveFar;
            for (int index = 0; index < shapes.Count; index++) {
                LandmarkSkyShape shape = shapes[index];
                if (shape.PlateauY + Settings.BlockMargin <= altitude || IsBeyond(shape, x, z, shape.BoundRadius + reach + far)) continue;

                gap = Math.Min(gap, shape.DistanceTo(x, z) - reach);
            }
            return gap >= far ? 1.0 : SmoothStep(Settings.DissolveNear, far, gap);
        }

        /// <summary>True when a cloud drawn at <paramref name="share"/> of its size is too small to draw at all.</summary>
        public bool IsDissolved(double share) {
            return share < Settings.DissolveCutoff;
        }

        /// <summary>
        /// Giant bias at (<paramref name="x"/>, <paramref name="z"/>) under the unit wind <paramref name="heading"/>: full
        /// within <see cref="CloudLandmarkSettings.BiasRadius"/> of the segment from a giant's anchor
        /// <see cref="CloudLandmarkSettings.BiasDownwind"/> downwind, smoothly to none over
        /// <see cref="CloudLandmarkSettings.BiasFade"/>; the largest over all giants.
        /// </summary>
        public double GiantBias(IReadOnlyList<LandmarkSkyShape> shapes, double x, double z, WindVector heading) {
            if (shapes == null) return 0.0;

            double bias = 0.0;
            double inner = Settings.BiasRadius;
            double outer = inner + Settings.BiasFade;
            for (int index = 0; index < shapes.Count; index++) {
                LandmarkSkyShape shape = shapes[index];
                if (shape.Tier != LandmarkSkyTier.Giant) continue;

                double endX = shape.AnchorX + heading.X * Settings.BiasDownwind;
                double endZ = shape.AnchorZ + heading.Z * Settings.BiasDownwind;
                double squared = LandmarkSkyShape.SegmentDistanceSquared(x, z, shape.AnchorX, shape.AnchorZ, endX, endZ);
                if (squared >= outer * outer) continue;

                bias = Math.Max(bias, Settings.GiantBias * (1.0 - SmoothStep(inner, outer, Math.Sqrt(squared))));
            }
            return bias;
        }

        /// <summary>0 at or below <paramref name="low"/>, 1 at or above <paramref name="high"/>, smoothstepped between.</summary>
        public static double SmoothStep(double low, double high, double value) {
            if (value <= low) return 0.0;
            if (value >= high) return 1.0;

            double share = (value - low) / (high - low);
            return share * share * (3.0 - 2.0 * share);
        }

        private static bool IsBeyond(LandmarkSkyShape shape, double x, double z, double distance) {
            double deltaX = x - shape.BoundX;
            double deltaZ = z - shape.BoundZ;
            return deltaX * deltaX + deltaZ * deltaZ > distance * distance;
        }
    }
}
