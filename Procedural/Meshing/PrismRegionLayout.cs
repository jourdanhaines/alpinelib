using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The look's height range and its <see cref="PrismRegion"/> (layers and footprints), built once per fit and shared
    /// by both refinement orders of <see cref="PrismEnvelopeFitter"/>.
    /// </summary>
    internal sealed class PrismRegionLayout {
        public PrismRegionLayout(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings) {
            if (corners.Count == 0) return;

            PrismEnvelopeFitter.LookRange(corners, out double low, out double high);
            High = high;
            ReachTop = Math.Min(high, ConvexPlan.Round(settings.LookHeight(settings.JumpReach)));
            Region = new PrismRegion(corners, PrismEnvelopeFitter.RegionSlabs(settings, low, ReachTop), settings);
        }

        /// <summary>The highest look point, rounded up to the grid.</summary>
        public double High { get; }

        /// <summary>The top of the region: the jump reach, or the look's top when lower.</summary>
        public double ReachTop { get; }

        /// <summary>The region, or null for an empty look.</summary>
        public PrismRegion Region { get; }
    }
}
